using System.Diagnostics;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Safe terminal command auto-confirmation macro action.
/// Observes the visible terminal viewport, extracts prompt/options/command, verifies against an explicit
/// approval rule set, briefly pulses foreground focus (if Confirm mode), revalidates all conditions,
/// dispatches Enter via SendInput (max 1 per action execution), and restores the previous foreground window.
/// </summary>
public sealed class SafeAutoConfirmAction : IMacroAction
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    public ApprovalRuleSet RuleSet { get; }

    /// <summary>
    /// Backward-compatible access to primary rule.
    /// </summary>
    public CommandApprovalRule Rule => RuleSet.Rules.FirstOrDefault() ?? new CommandApprovalRule
    {
        Name = RuleSet.Name,
        ExpectedProcess = RuleSet.ExpectedProcess,
        ExpectedWindowClass = RuleSet.ExpectedWindowClass,
        ExpectedPrompt = RuleSet.ExpectedPrompt,
        ExpectedSelectedOption = RuleSet.ExpectedSelectedOption
    };

    public AutoConfirmExecutionMode ExecutionMode { get; }
    public KeyDeliveryMode DeliveryMode { get; }
    public AutoConfirmWaitMode WaitMode { get; }
    public FocusBehavior FocusBehavior { get; }
    public ApprovalPolicyMode PolicyMode { get; } = ApprovalPolicyMode.ExactRules;
    public TimeSpan Timeout { get; }
    public TimeSpan PollInterval { get; }
    public IntPtr OverrideHwnd { get; }

    public static readonly TimeSpan DefaultUserIdleThreshold = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan MinUserIdleThreshold = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaxUserIdleThreshold = TimeSpan.FromMilliseconds(10000);
    public static readonly TimeSpan ForegroundStablePeriod = TimeSpan.FromMilliseconds(300);

    public bool RespectUserFocus { get; }
    public TimeSpan UserIdleThreshold { get; }

    public string Name => "Safe Auto Confirm";

    public string DisplayString
    {
        get
        {
            string waitDisplay = WaitMode == AutoConfirmWaitMode.Indefinite ? "Indefinite" : $"Timeout: {Timeout.TotalSeconds:F0}s";
            string focusDisplay = FocusBehavior == FocusBehavior.KeepTargetForeground ? " [KeepFG]" : string.Empty;
            if (PolicyMode == ApprovalPolicyMode.FoolMode)
            {
                return $"[{ExecutionMode}][FOOL MODE] \"{RuleSet.Name}\" -> Unrestricted ({waitDisplay}){focusDisplay}";
            }
            if (RuleSet.Rules.Count == 1)
            {
                return $"[{ExecutionMode}] \"{RuleSet.Name}\" -> {RuleSet.Rules[0].AllowedCommand} ({waitDisplay}){focusDisplay}";
            }
            return $"[{ExecutionMode}] \"{RuleSet.Name}\" -> {RuleSet.Rules.Count} rules ({waitDisplay}){focusDisplay}";
        }
    }

    public SafeAutoConfirmAction(
        ApprovalRuleSet ruleSet,
        AutoConfirmExecutionMode executionMode = AutoConfirmExecutionMode.ObserveOnly,
        KeyDeliveryMode deliveryMode = KeyDeliveryMode.ForegroundPulse,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        IntPtr overrideHwnd = default,
        AutoConfirmWaitMode waitMode = AutoConfirmWaitMode.FixedTimeout,
        FocusBehavior focusBehavior = FocusBehavior.FastPulse,
        ApprovalPolicyMode policyMode = ApprovalPolicyMode.ExactRules,
        bool respectUserFocus = true,
        TimeSpan? userIdleThreshold = null)
    {
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        ExecutionMode = executionMode;
        DeliveryMode = deliveryMode;
        Timeout = timeout ?? DefaultTimeout;
        PollInterval = pollInterval ?? DefaultPollInterval;
        OverrideHwnd = overrideHwnd;
        WaitMode = waitMode;
        FocusBehavior = focusBehavior;
        PolicyMode = policyMode;
        RespectUserFocus = respectUserFocus;

        if (userIdleThreshold.HasValue)
        {
            var rawMs = (int)userIdleThreshold.Value.TotalMilliseconds;
            var clampedMs = Math.Clamp(rawMs, (int)MinUserIdleThreshold.TotalMilliseconds, (int)MaxUserIdleThreshold.TotalMilliseconds);
            UserIdleThreshold = TimeSpan.FromMilliseconds(clampedMs);
        }
        else
        {
            UserIdleThreshold = DefaultUserIdleThreshold;
        }
    }

    public SafeAutoConfirmAction(
        CommandApprovalRule rule,
        AutoConfirmExecutionMode executionMode = AutoConfirmExecutionMode.ObserveOnly,
        KeyDeliveryMode deliveryMode = KeyDeliveryMode.ForegroundPulse,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        IntPtr overrideHwnd = default,
        AutoConfirmWaitMode waitMode = AutoConfirmWaitMode.FixedTimeout,
        FocusBehavior focusBehavior = FocusBehavior.FastPulse,
        ApprovalPolicyMode policyMode = ApprovalPolicyMode.ExactRules,
        bool respectUserFocus = true,
        TimeSpan? userIdleThreshold = null)
        : this(
            ApprovalRuleSet.FromSingleRule(rule ?? throw new ArgumentNullException(nameof(rule))),
            executionMode,
            deliveryMode,
            timeout,
            pollInterval,
            overrideHwnd,
            waitMode,
            focusBehavior,
            policyMode,
            respectUserFocus,
            userIdleThreshold)
    {
    }

    public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (PolicyMode == ApprovalPolicyMode.ExactRules && (RuleSet.Rules.Count == 0 || !RuleSet.Rules.Any(r => r.Enabled && !string.IsNullOrWhiteSpace(r.AllowedCommand))))
        {
            return MacroActionResult.InvalidConfiguration("Rule set must contain at least one enabled rule with a non-empty allowed command.");
        }

        if (string.IsNullOrWhiteSpace(RuleSet.ExpectedPrompt))
        {
            return MacroActionResult.InvalidConfiguration("Rule ExpectedPrompt cannot be empty.");
        }

        if (WaitMode == AutoConfirmWaitMode.FixedTimeout && Timeout <= TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"Timeout must be positive: {Timeout}");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"PollInterval must be positive: {PollInterval}");
        }

        // Section 9/10: Guard FOOL MODE Confirm execution with explicit session authorization
        if (PolicyMode == ApprovalPolicyMode.FoolMode && ExecutionMode == AutoConfirmExecutionMode.Confirm && !context.IsFoolModeAuthorized)
        {
            context.Logger?.Warning("[AutoConfirm][FOOL MODE] Execution attempted without explicit session authorization. Blocked.");
            return MacroActionResult.ApprovalBlocked("FOOL MODE execution requires explicit user authorization for this runner session.");
        }

        IntPtr targetHwnd = OverrideHwnd != IntPtr.Zero ? OverrideHwnd : context.TargetHwnd;
        if (targetHwnd == IntPtr.Zero || !context.ForegroundService.IsWindow(targetHwnd))
        {
            context.Logger?.Warning($"[AutoConfirm] Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
            return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
        }

        IntPtr targetRootHwnd = context.ForegroundService.GetRootWindow(targetHwnd);
        IntPtr effectiveRoot = targetRootHwnd != IntPtr.Zero ? targetRootHwnd : targetHwnd;

        // Transient pause if target window is minimized at start
        if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
            (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
        {
            string warnMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                ? "[AutoConfirm][FOOL MODE] PAUSED — target window is minimized."
                : "[AutoConfirm] PAUSED — target window is minimized.";
            context.Logger?.Warning(warnMsg);
            context.ReportProgress("PAUSED — Target minimized");

            while (context.ForegroundService.IsWindow(targetHwnd) &&
                   (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                    (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot))))
            {
                if (ct.IsCancellationRequested)
                {
                    context.Logger?.Info("[AutoConfirm] Cancelled.");
                    return MacroActionResult.Cancelled();
                }
                try
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return MacroActionResult.Cancelled();
                }
            }

            if (!context.ForegroundService.IsWindow(targetHwnd))
            {
                context.Logger?.Warning($"[AutoConfirm] Target HWND {HwndFormatter.Format(targetHwnd)} closed while minimized.");
                return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed while minimized.");
            }

            string infoMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                ? "[AutoConfirm][FOOL MODE] Target window restored; resuming prompt evaluation."
                : "[AutoConfirm] Target window restored; resuming prompt evaluation.";
            context.Logger?.Info(infoMsg);
        }

        // Section 3: Check UIPI elevation compatibility before any foreground or input attempt
        int initialPid = context.ForegroundService.GetProcessId(targetHwnd);
        var initialUipi = context.ElevationService.CheckCompatibility(initialPid);
        if (initialUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
        {
            context.Logger?.Warning($"[AutoConfirm] UIPI mismatch: Target PID {initialPid} is elevated while BackgroundAutomator is not. Auto-confirm blocked.");
            return MacroActionResult.ApprovalBlocked("UIPI mismatch: Target is running as Administrator while BackgroundAutomator is not. Auto-confirm blocked.");
        }

        if (PolicyMode == ApprovalPolicyMode.FoolMode)
        {
            context.Logger?.Info($"[AutoConfirm][FOOL MODE] Started. RuleSet: \"{RuleSet.Name}\", Mode: {ExecutionMode}, Wait: {WaitMode}, Delivery: {DeliveryMode}, Policy: {PolicyMode}");
        }
        else
        {
            string rulesSummary = string.Join(", ", RuleSet.Rules.Where(r => r.Enabled).Select(r => $"\"{r.AllowedCommand}\""));
            context.Logger?.Info($"[AutoConfirm] Started. RuleSet: \"{RuleSet.Name}\", Mode: {ExecutionMode}, Wait: {WaitMode}, Delivery: {DeliveryMode}, Rules: [{rulesSummary}]");
        }

        // Section 1.1: Always inspect visible viewport only for approval automation
        var request = new TextDetectionRequest(
            RuleSet.ExpectedPrompt,
            TextMatchMode.Contains,
            TextDetectionScope.VisibleViewportOnly);

        var sw = Stopwatch.StartNew();
        string? lastBlockedReason = null;
        ApprovalBlockReason? lastBlockReason = null;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                context.Logger?.Info("[AutoConfirm] Cancelled.");
                return MacroActionResult.Cancelled();
            }

            if (!context.ForegroundService.IsWindow(targetHwnd))
            {
                context.Logger?.Warning($"[AutoConfirm] Target HWND {HwndFormatter.Format(targetHwnd)} closed during evaluation.");
                return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed during evaluation.");
            }

            // Transient pause if target window is minimized during polling loop
            if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
            {
                sw.Stop();
                string warnMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                    ? "[AutoConfirm][FOOL MODE] PAUSED — target window is minimized."
                    : "[AutoConfirm] PAUSED — target window is minimized.";
                context.Logger?.Warning(warnMsg);
                context.ReportProgress("PAUSED — Target minimized");

                while (context.ForegroundService.IsWindow(targetHwnd) &&
                       (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                        (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot))))
                {
                    if (ct.IsCancellationRequested)
                    {
                        context.Logger?.Info("[AutoConfirm] Cancelled.");
                        return MacroActionResult.Cancelled();
                    }
                    try
                    {
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return MacroActionResult.Cancelled();
                    }
                }

                if (!context.ForegroundService.IsWindow(targetHwnd))
                {
                    context.Logger?.Warning($"[AutoConfirm] Target HWND {HwndFormatter.Format(targetHwnd)} closed while minimized.");
                    return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed while minimized.");
                }

                string infoMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                    ? "[AutoConfirm][FOOL MODE] Target window restored; resuming prompt evaluation."
                    : "[AutoConfirm] Target window restored; resuming prompt evaluation.";
                context.Logger?.Info(infoMsg);
                sw.Start();
                continue;
            }

            TextDetectionResult detectResult;
            try
            {
                detectResult = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return MacroActionResult.Cancelled();
            }
            catch (Exception ex)
            {
                context.Logger?.Warning($"[AutoConfirm] Detector failure: {ex.Message}");
                return MacroActionResult.TextDetectionFailed($"Text detection failed: {ex.Message}");
            }

            if (detectResult.Matched)
            {
                string rawText = detectResult.RawText ?? detectResult.ObservedText ?? string.Empty;
                var extraction = context.CommandPromptParser.Parse(
                    rawText,
                    RuleSet.ExpectedPrompt,
                    RuleSet.ExpectedSelectedOption);

                var snapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, rawText, extraction);

                string actualProc = context.ForegroundService.GetProcessName(targetHwnd);
                string actualClass = context.ForegroundService.GetWindowClass(targetHwnd);

                var decision = CommandApprovalEvaluator.Evaluate(RuleSet, snapshot, actualProc, actualClass, PolicyMode);

                if (decision.IsAllowed)
                {
                    string commandDisplay = snapshot.CommandText
                        ?? (snapshot.Envelope != null ? snapshot.Envelope.GetCommandPreview(100) : "[Unknown]");

                    if (PolicyMode == ApprovalPolicyMode.FoolMode)
                    {
                        context.Logger?.Info("[AutoConfirm][FOOL MODE] approval prompt detected");
                        context.Logger?.Info($"[AutoConfirm][FOOL MODE] command: \"{commandDisplay}\"");

                        // 1. Observe-only mode: strictly passive (zero focus changes, zero keystrokes)
                        if (ExecutionMode == AutoConfirmExecutionMode.ObserveOnly)
                        {
                            context.Logger?.Info($"[AutoConfirm][FOOL MODE] WOULD APPROVE: \"{commandDisplay}\"");
                            return MacroActionResult.Success($"WOULD APPROVE: {decision.Explanation}");
                        }

                        context.Logger?.Info("[AutoConfirm][FOOL MODE] unrestricted approval authorized for this runner session");
                        context.Logger?.Warning($"WARNING [AutoConfirm][FOOL MODE] approving unrestricted command: \"{commandDisplay}\"");
                    }
                    else
                    {
                        context.Logger?.Info("[AutoConfirm] approval prompt detected");
                        context.Logger?.Info($"[AutoConfirm] command extracted: \"{commandDisplay}\"");
                        string matchedRuleMsg = !string.IsNullOrEmpty(decision.MatchedRuleName)
                            ? $" using rule '{decision.MatchedRuleName}'"
                            : string.Empty;
                        context.Logger?.Info($"[AutoConfirm] Approved command '{commandDisplay}'{matchedRuleMsg}.");

                        // 1. Observe-only mode: strictly passive (zero focus changes, zero keystrokes)
                        if (ExecutionMode == AutoConfirmExecutionMode.ObserveOnly)
                        {
                            context.Logger?.Info($"[AutoConfirm] WOULD APPROVE (ObserveOnly): \"{commandDisplay}\"{matchedRuleMsg}");
                            return MacroActionResult.Success($"WOULD APPROVE: {decision.Explanation}");
                        }
                    }

                    // 2. Confirm mode: execute controlled foreground pulse with retry & transient pause
                    sw.Stop();
                    var pulseResult = await ExecuteConfirmPulseAsync(
                        context,
                        targetHwnd,
                        effectiveRoot,
                        request,
                        snapshot,
                        ct).ConfigureAwait(false);

                    if (pulseResult != null)
                    {
                        return pulseResult;
                    }

                    // Foreground wait ended because prompt disappeared or changed; resume active wait loop
                    sw.Start();
                    continue;
                }
                else
                {
                    lastBlockReason = decision.BlockReason;
                    string currentReason = $"{decision.BlockReason}: {decision.Explanation}";
                    if (currentReason != lastBlockedReason)
                    {
                        lastBlockedReason = currentReason;
                        context.Logger?.Info($"[AutoConfirm] BLOCKED — {currentReason}");
                    }

                    string liveStatus = FormatLiveBlocker(decision.BlockReason, decision.Explanation, extraction);
                    context.ReportProgress(liveStatus);
                }
            }
            else
            {
                context.ReportProgress("Waiting — Prompt not visible");
            }

            if (WaitMode == AutoConfirmWaitMode.FixedTimeout)
            {
                if (sw.Elapsed >= Timeout)
                {
                    break;
                }

                TimeSpan remaining = Timeout - sw.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }
            }

            TimeSpan delay = PollInterval;
            if (WaitMode == AutoConfirmWaitMode.FixedTimeout)
            {
                TimeSpan remaining = Timeout - sw.Elapsed;
                delay = remaining < PollInterval ? remaining : PollInterval;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return MacroActionResult.Cancelled();
            }
        }

        string timeoutMsg = !string.IsNullOrEmpty(lastBlockedReason)
            ? $"Timed out waiting for approval prompt under rule set '{RuleSet.Name}' after {Timeout.TotalMilliseconds:F0}ms. Last blocker: {lastBlockedReason}"
            : $"Timed out waiting for approval prompt under rule set '{RuleSet.Name}' after {Timeout.TotalMilliseconds:F0}ms.";

        context.Logger?.Warning($"[AutoConfirm] {timeoutMsg}");
        return MacroActionResult.Timeout(timeoutMsg, lastBlockReason?.ToString());
    }

    private async Task<MacroActionResult?> ExecuteConfirmPulseAsync(
        MacroExecutionContext context,
        IntPtr targetHwnd,
        IntPtr effectiveRoot,
        TextDetectionRequest request,
        CommandPromptSnapshot initialSnapshot,
        CancellationToken ct)
    {
        // Re-check UIPI compatibility before requesting foreground activation
        int pulsePid = context.ForegroundService.GetProcessId(targetHwnd);
        var pulseUipi = context.ElevationService.CheckCompatibility(pulsePid);
        if (pulseUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
        {
            context.Logger?.Warning($"[AutoConfirm] UIPI mismatch detected prior to foreground activation for PID {pulsePid}.");
            return MacroActionResult.ApprovalBlocked("UIPI mismatch: Target is running as Administrator while BackgroundAutomator is not. Foreground activation blocked.");
        }

        IntPtr restoreTargetHwnd = IntPtr.Zero;
        IntPtr initialFg = context.ForegroundService.GetForegroundWindow();
        bool isAlreadyForeground = (initialFg == effectiveRoot || initialFg == targetHwnd);
        context.Logger?.Info($"[AutoConfirm] initial foreground = 0x{initialFg.ToInt64():X8}{(isAlreadyForeground ? " (target is already foreground)" : string.Empty)}");

        var pulseSw = Stopwatch.StartNew();
        long activationConfirmedAtMs = 0;
        long enterSentAtMs = 0;
        long previousRestoredAtMs = 0;
        bool foregroundRestored = false;
        bool wasActivated = false;

        bool foregroundAcquired = isAlreadyForeground;
        bool pausedLogged = false;
        bool userActivePausedLogged = false;
        bool userActivityFailureLogged = false;

        while (!foregroundAcquired)
        {
            if (ct.IsCancellationRequested)
            {
                context.Logger?.Info("[AutoConfirm] Cancelled during foreground acquisition.");
                return MacroActionResult.Cancelled();
            }

            if (!context.ForegroundService.IsWindow(targetHwnd))
            {
                context.Logger?.Warning("[AutoConfirm] Target window closed during foreground acquisition.");
                return MacroActionResult.TargetUnavailable("Target window closed during foreground acquisition.");
            }

            // Check if target window became minimized during retry
            if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
            {
                string warnMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                    ? "[AutoConfirm][FOOL MODE] PAUSED — target window is minimized."
                    : "[AutoConfirm] PAUSED — target window is minimized.";
                context.Logger?.Warning(warnMsg);
                context.ReportProgress("PAUSED — Target minimized");

                while (context.ForegroundService.IsWindow(targetHwnd) &&
                       (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                        (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot))))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return MacroActionResult.Cancelled();
                    }
                    try
                    {
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return MacroActionResult.Cancelled();
                    }
                }

                if (!context.ForegroundService.IsWindow(targetHwnd))
                {
                    return MacroActionResult.TargetUnavailable("Target window closed while minimized.");
                }

                string infoMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                    ? "[AutoConfirm][FOOL MODE] Target window restored; resuming prompt evaluation."
                    : "[AutoConfirm] Target window restored; resuming prompt evaluation.";
                context.Logger?.Info(infoMsg);

                // Discard stale state: check if prompt is still active
                var recheckAfterRestore = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
                if (!recheckAfterRestore.Matched)
                {
                    context.Logger?.Info("[AutoConfirm] Foreground wait ended because the permission prompt is no longer active.");
                    return null;
                }
                continue;
            }

            // Check if target acquired foreground naturally (e.g. user clicked on it)
            IntPtr currentFg = context.ForegroundService.GetForegroundWindow();
            if (currentFg == effectiveRoot || currentFg == targetHwnd)
            {
                context.Logger?.Info("[AutoConfirm] Target became foreground; skipping focus pulse.");
                foregroundAcquired = true;
                isAlreadyForeground = true;
                break;
            }

            // Step A: Check Respect User Focus Guard
            if (RespectUserFocus && context.UserActivityService != null)
            {
                bool gotIdle = context.UserActivityService.TryGetIdleDuration(out TimeSpan idleDuration);
                if (!gotIdle)
                {
                    if (!userActivityFailureLogged)
                    {
                        userActivityFailureLogged = true;
                        context.Logger?.Warning("[AutoConfirm] GetLastInputInfo failed; falling back to immediate focus pulse.");
                    }
                }
                else if (idleDuration < UserIdleThreshold &&
                         currentFg != effectiveRoot &&
                         currentFg != targetHwnd)
                {
                    if (!userActivePausedLogged)
                    {
                        userActivePausedLogged = true;
                        string warnMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                            ? "[AutoConfirm][FOOL MODE] PAUSED — user is active in another foreground window; foreground pulse deferred."
                            : "[AutoConfirm] PAUSED — user is active in another foreground window; foreground pulse deferred.";
                        context.Logger?.Warning(warnMsg);
                    }

                    string progressMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                        ? "⚠ FOOL MODE — PAUSED: User active in another window"
                        : "PAUSED — User active in another window";
                    context.ReportProgress(progressMsg);

                    IntPtr stableCandidateHwnd = currentFg;

                    // Pause loop: wait until user is idle and foreground is stable
                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return MacroActionResult.Cancelled();
                        }

                        if (!context.ForegroundService.IsWindow(targetHwnd))
                        {
                            return MacroActionResult.TargetUnavailable("Target window closed during user active wait.");
                        }

                        // Target minimized takes priority
                        if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                            (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
                        {
                            break;
                        }

                        // Natural foreground acquisition (user clicked terminal)
                        currentFg = context.ForegroundService.GetForegroundWindow();
                        if (currentFg == effectiveRoot || currentFg == targetHwnd)
                        {
                            context.Logger?.Info("[AutoConfirm] Target became foreground; skipping focus pulse.");
                            foregroundAcquired = true;
                            isAlreadyForeground = true;
                            break;
                        }

                        if (!context.UserActivityService.TryGetIdleDuration(out idleDuration) || idleDuration >= UserIdleThreshold)
                        {
                            // User appears idle. Test foreground stability window (300 ms).
                            stableCandidateHwnd = currentFg;
                            try
                            {
                                await Task.Delay(ForegroundStablePeriod, ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                return MacroActionResult.Cancelled();
                            }

                            if (!context.ForegroundService.IsWindow(targetHwnd))
                            {
                                return MacroActionResult.TargetUnavailable("Target window closed during stability wait.");
                            }

                            if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                                (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
                            {
                                break;
                            }

                            IntPtr fgAfter = context.ForegroundService.GetForegroundWindow();
                            if (fgAfter == effectiveRoot || fgAfter == targetHwnd)
                            {
                                context.Logger?.Info("[AutoConfirm] Target became foreground; skipping focus pulse.");
                                foregroundAcquired = true;
                                isAlreadyForeground = true;
                                break;
                            }

                            bool finalIdleOk = context.UserActivityService.TryGetIdleDuration(out var idleAfter);
                            if (finalIdleOk && idleAfter >= UserIdleThreshold && fgAfter == stableCandidateHwnd)
                            {
                                // Foreground stable and user idle!
                                userActivePausedLogged = false;
                                pausedLogged = false;
                                context.Logger?.Info("[AutoConfirm] RESUMED — user idle window detected; prompt will be re-read before foreground activation.");
                                break;
                            }

                            // Otherwise, either new input arrived or foreground changed during the 300ms.
                            // Continue waiting in PAUSED_USER_ACTIVE.
                        }

                        try
                        {
                            await Task.Delay(150, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return MacroActionResult.Cancelled();
                        }
                    }

                    if (foregroundAcquired)
                    {
                        break;
                    }

                    if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                        (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
                    {
                        continue;
                    }

                    // Fresh prompt re-read after user active pause
                    TextDetectionResult idleRecheckDetect;
                    try
                    {
                        idleRecheckDetect = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return MacroActionResult.Cancelled();
                    }
                    catch (Exception ex)
                    {
                        context.Logger?.Warning($"[AutoConfirm] Detector failure after user-idle wait: {ex.Message}");
                        return MacroActionResult.TextDetectionFailed($"Text detection failed: {ex.Message}");
                    }

                    if (!idleRecheckDetect.Matched)
                    {
                        context.Logger?.Info("[AutoConfirm] Foreground wait ended because the permission prompt is no longer active.");
                        return null;
                    }

                    // Re-evaluate with fresh viewport
                    string idleRecheckRaw = idleRecheckDetect.RawText ?? idleRecheckDetect.ObservedText ?? string.Empty;
                    var idleRecheckExtraction = context.CommandPromptParser.Parse(idleRecheckRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
                    var idleRecheckSnapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, idleRecheckRaw, idleRecheckExtraction);
                    string idleRecheckProc = context.ForegroundService.GetProcessName(targetHwnd);
                    string idleRecheckClass = context.ForegroundService.GetWindowClass(targetHwnd);

                    var idleRecheckDecision = CommandApprovalEvaluator.Evaluate(RuleSet, idleRecheckSnapshot, idleRecheckProc, idleRecheckClass, PolicyMode);
                    if (!idleRecheckDecision.IsAllowed)
                    {
                        context.Logger?.Info($"[AutoConfirm] Prompt changed during user-idle wait: {idleRecheckDecision.BlockReason} — {idleRecheckDecision.Explanation}");
                        return null;
                    }

                    // Final guard immediately before ActivateWindow (User adjustment 2)
                    IntPtr prePulseFg = context.ForegroundService.GetForegroundWindow();
                    if (prePulseFg == effectiveRoot || prePulseFg == targetHwnd)
                    {
                        context.Logger?.Info("[AutoConfirm] Target became foreground; skipping focus pulse.");
                        foregroundAcquired = true;
                        isAlreadyForeground = true;
                        break;
                    }

                    if (!context.ForegroundService.IsWindow(targetHwnd) ||
                        context.ForegroundService.IsWindowMinimized(targetHwnd))
                    {
                        continue;
                    }

                    bool gotFinalIdle = context.UserActivityService.TryGetIdleDuration(out var finalIdleBeforePulse);
                    if (prePulseFg != stableCandidateHwnd || (gotFinalIdle && finalIdleBeforePulse < UserIdleThreshold))
                    {
                        context.Logger?.Info("[AutoConfirm] User activity or foreground changed during revalidation; deferring foreground activation.");
                        continue;
                    }
                }
            }

            // Immediately before ActivateWindow, capture pulse-local restoreTargetHwnd
            IntPtr fgBeforeActivate = context.ForegroundService.GetForegroundWindow();
            if (fgBeforeActivate == effectiveRoot || fgBeforeActivate == targetHwnd)
            {
                context.Logger?.Info("[AutoConfirm] Target became foreground; skipping focus pulse.");
                foregroundAcquired = true;
                isAlreadyForeground = true;
                restoreTargetHwnd = IntPtr.Zero;
                break;
            }

            restoreTargetHwnd = fgBeforeActivate;
            context.Logger?.Info($"[AutoConfirm] Capturing restore target = 0x{restoreTargetHwnd.ToInt64():X8} immediately prior to activation.");

            // Try to activate
            bool activated = context.ForegroundService.ActivateWindow(effectiveRoot);
            if (activated)
            {
                wasActivated = true;
                var fgSw = Stopwatch.StartNew();
                while (fgSw.ElapsedMilliseconds < 500)
                {
                    currentFg = context.ForegroundService.GetForegroundWindow();
                    if (currentFg == effectiveRoot || currentFg == targetHwnd)
                    {
                        foregroundAcquired = true;
                        break;
                    }
                    try
                    {
                        await Task.Delay(25, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return MacroActionResult.Cancelled();
                    }
                }

                if (foregroundAcquired)
                {
                    break;
                }
            }

            // If we reach here, foreground activation failed or was denied by OS
            if (!pausedLogged)
            {
                pausedLogged = true;
                string warnMsg = PolicyMode == ApprovalPolicyMode.FoolMode
                    ? "[AutoConfirm][FOOL MODE] PAUSED — Windows denied foreground activation; waiting for state change before retrying."
                    : "[AutoConfirm] PAUSED — Windows denied foreground activation; waiting for state change before retrying.";
                context.Logger?.Warning(warnMsg);
            }
            context.ReportProgress("PAUSED — Waiting for Terminal focus");

            // Wait for meaningful state change without repeatedly hammering SetForegroundWindow
            IntPtr deniedFgHwnd = restoreTargetHwnd;
            var denialPromptRecheckSw = Stopwatch.StartNew();

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    return MacroActionResult.Cancelled();
                }

                if (!context.ForegroundService.IsWindow(targetHwnd))
                {
                    return MacroActionResult.TargetUnavailable("Target window closed during foreground acquisition.");
                }

                if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
                    (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
                {
                    break;
                }

                IntPtr checkFg = context.ForegroundService.GetForegroundWindow();

                // 1. Natural foreground acquisition
                if (checkFg == effectiveRoot || checkFg == targetHwnd)
                {
                    context.Logger?.Info("[AutoConfirm] Target acquired foreground naturally while waiting; proceeding.");
                    foregroundAcquired = true;
                    isAlreadyForeground = true;
                    restoreTargetHwnd = IntPtr.Zero;
                    break;
                }

                // 2. User resumed physical activity
                if (RespectUserFocus && context.UserActivityService != null &&
                    context.UserActivityService.TryGetIdleDuration(out var denialIdle) &&
                    denialIdle < UserIdleThreshold)
                {
                    context.Logger?.Info("[AutoConfirm] User activity detected while waiting for focus; transitioning to PAUSED_USER_ACTIVE.");
                    userActivePausedLogged = false;
                    pausedLogged = false;
                    break;
                }

                // 3. Foreground window changed to another application
                if (checkFg != deniedFgHwnd)
                {
                    context.Logger?.Info($"[AutoConfirm] Foreground window changed (0x{deniedFgHwnd.ToInt64():X8} -> 0x{checkFg.ToInt64():X8}); resetting activation opportunity.");
                    pausedLogged = false;
                    break;
                }

                // Periodic prompt re-validation while waiting
                int checkIntervalMs = Math.Min(200, Math.Max(20, (int)PollInterval.TotalMilliseconds));
                if (denialPromptRecheckSw.ElapsedMilliseconds >= checkIntervalMs)
                {
                    denialPromptRecheckSw.Restart();
                    TextDetectionResult promptRecheck;
                    try
                    {
                        promptRecheck = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return MacroActionResult.Cancelled();
                    }
                    catch (Exception ex)
                    {
                        context.Logger?.Warning($"[AutoConfirm] Detector failure during foreground retry: {ex.Message}");
                        return MacroActionResult.TextDetectionFailed($"Text detection failed: {ex.Message}");
                    }

                    if (!promptRecheck.Matched)
                    {
                        context.Logger?.Info("[AutoConfirm] Foreground wait ended because permission prompt is no longer active.");
                        return null;
                    }

                    // Re-evaluate with fresh viewport
                    string recheckRaw = promptRecheck.RawText ?? promptRecheck.ObservedText ?? string.Empty;
                    var recheckExtraction = context.CommandPromptParser.Parse(recheckRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
                    var recheckSnapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, recheckRaw, recheckExtraction);
                    string recheckProc = context.ForegroundService.GetProcessName(targetHwnd);
                    string recheckClass = context.ForegroundService.GetWindowClass(targetHwnd);

                    var recheckDecision = CommandApprovalEvaluator.Evaluate(RuleSet, recheckSnapshot, recheckProc, recheckClass, PolicyMode);
                    if (!recheckDecision.IsAllowed)
                    {
                        context.Logger?.Info($"[AutoConfirm] Prompt changed during foreground wait: {recheckDecision.BlockReason} — {recheckDecision.Explanation}");
                        return null;
                    }
                }

                int waitDelayMs = Math.Min(100, Math.Max(20, (int)PollInterval.TotalMilliseconds));
                try
                {
                    await Task.Delay(waitDelayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return MacroActionResult.Cancelled();
                }
            }
        }

        activationConfirmedAtMs = pulseSw.ElapsedMilliseconds;
        if (isAlreadyForeground)
        {
            context.Logger?.Info($"[AutoConfirm] terminal already foreground (+{activationConfirmedAtMs}ms)");
        }
        else
        {
            context.Logger?.Info($"[AutoConfirm] terminal foreground confirmed (+{activationConfirmedAtMs}ms)");
        }

        try
        {
            // Step 3: CRITICAL REVALIDATION
            var revalDetect = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
            if (!revalDetect.Matched)
            {
                context.Logger?.Warning("[AutoConfirm] Rule revalidation failed: prompt is no longer visible after foreground activation.");
                return MacroActionResult.ApprovalBlocked("Approval prompt disappeared after foreground activation. RevalidationFailed.");
            }

            if (!context.ForegroundService.IsWindow(targetHwnd))
            {
                context.Logger?.Warning("[AutoConfirm] Target window closed after foreground activation.");
                return MacroActionResult.TargetUnavailable("Target window closed after foreground activation.");
            }

            IntPtr revalRoot = context.ForegroundService.GetRootWindow(targetHwnd);
            if (revalRoot != effectiveRoot)
            {
                context.Logger?.Warning("[AutoConfirm] Target root window changed after foreground activation. TargetMismatch.");
                return MacroActionResult.ApprovalBlocked("Target root window changed after foreground activation. TargetMismatch.");
            }

            string freshProc = context.ForegroundService.GetProcessName(targetHwnd);
            string freshClass = context.ForegroundService.GetWindowClass(targetHwnd);
            if (string.IsNullOrEmpty(freshProc))
            {
                context.Logger?.Warning("[AutoConfirm] Target process unavailable after foreground activation.");
                return MacroActionResult.TargetUnavailable("Target process unavailable after foreground activation.");
            }

            int revalPid = context.ForegroundService.GetProcessId(targetHwnd);
            var revalUipi = context.ElevationService.CheckCompatibility(revalPid);
            if (revalUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
            {
                context.Logger?.Warning($"[AutoConfirm] UIPI mismatch detected during revalidation for PID {revalPid}.");
                return MacroActionResult.ApprovalBlocked("UIPI mismatch detected during revalidation. Foreground input blocked.");
            }

            string revalRaw = revalDetect.RawText ?? revalDetect.ObservedText ?? string.Empty;
            var revalExtraction = context.CommandPromptParser.Parse(revalRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
            var revalSnapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, revalRaw, revalExtraction);

            var revalDecision = CommandApprovalEvaluator.Evaluate(RuleSet, revalSnapshot, freshProc, freshClass, PolicyMode);
            if (!revalDecision.IsAllowed)
            {
                context.Logger?.Warning($"[AutoConfirm] Rule revalidation blocked: {revalDecision.BlockReason} — {revalDecision.Explanation}");
                return MacroActionResult.ApprovalBlocked($"Rule revalidation failed: {revalDecision.Explanation}");
            }

            if (PolicyMode == ApprovalPolicyMode.FoolMode)
            {
                context.Logger?.Info("[AutoConfirm][FOOL MODE] unrestricted approval revalidated");
            }
            else
            {
                context.Logger?.Info("[AutoConfirm] rule revalidated");
            }

            // Step 4: Verify foreground ownership IMMEDIATELY before SendInput (no intervening async delay)
            IntPtr immediateFg = context.ForegroundService.GetForegroundWindow();
            if (immediateFg != effectiveRoot && immediateFg != targetHwnd)
            {
                context.Logger?.Warning($"[AutoConfirm] Foreground changed unexpectedly before SendInput! Active: 0x{immediateFg.ToInt64():X8}. Retrying.");
                return null;
            }

            // Step 5: Dispatch Enter (maximum 1 dispatch per action execution)
            string cmdLog = revalSnapshot.CommandText
                ?? (revalSnapshot.Envelope != null ? revalSnapshot.Envelope.GetCommandPreview(100) : "[Unknown]");

            if (PolicyMode == ApprovalPolicyMode.FoolMode)
            {
                context.Logger?.Warning($"WARNING [AutoConfirm][FOOL MODE] approving unrestricted command: \"{cmdLog}\"");
            }

            // Capture pre-injection idle duration before dispatching SendInput
            TimeSpan preInjectionIdle = TimeSpan.Zero;
            if (context.UserActivityService != null && context.UserActivityService.TryGetIdleDuration(out var capturedIdle))
            {
                preInjectionIdle = capturedIdle;
            }

            if (DeliveryMode == KeyDeliveryMode.ForegroundPulse)
            {
                bool sent = context.ForegroundKeyboard.SendEnter();
                if (!sent)
                {
                    context.Logger?.Warning("[AutoConfirm] SendInput Enter failed.");
                    return MacroActionResult.ApprovalFailed("SendInput Enter failed.");
                }
            }
            else
            {
                context.Keyboard.PressKey(targetHwnd, BackgroundKey.Enter);
            }

            enterSentAtMs = pulseSw.ElapsedMilliseconds;
            context.UserActivityService?.NotifyInputInjected(preInjectionIdle);
            context.Logger?.Info($"[AutoConfirm] Enter sent (+{enterSentAtMs}ms)");

            // FastPulse optimization: Restore previous foreground IMMEDIATELY after Enter!
            if (FocusBehavior == FocusBehavior.FastPulse &&
                !isAlreadyForeground &&
                restoreTargetHwnd != IntPtr.Zero &&
                restoreTargetHwnd != targetHwnd &&
                restoreTargetHwnd != effectiveRoot)
            {
                IntPtr currentFg = context.ForegroundService.GetForegroundWindow();
                if (currentFg != effectiveRoot && currentFg != targetHwnd)
                {
                    context.Logger?.Info("[AutoConfirm] User changed foreground during pulse; skipping automatic restoration.");
                }
                else if (context.ForegroundService.IsWindow(restoreTargetHwnd))
                {
                    try
                    {
                        bool restored = context.ForegroundService.RestoreForegroundWindow(restoreTargetHwnd);
                        previousRestoredAtMs = pulseSw.ElapsedMilliseconds;
                        foregroundRestored = true;
                        if (restored)
                        {
                            long pulseDurationMs = previousRestoredAtMs;
                            context.Logger?.Info($"[AutoConfirm] ForegroundPulse: activation confirmed at +{activationConfirmedAtMs}ms, Enter sent at +{enterSentAtMs}ms, previous foreground restored at +{previousRestoredAtMs}ms, pulse duration = {pulseDurationMs}ms");
                        }
                        else
                        {
                            context.Logger?.Warning("[AutoConfirm] Previous foreground window could not be restored by OS. (Command approval was already processed).");
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger?.Warning($"[AutoConfirm] Foreground restore exception: {ex.Message}");
                    }
                }
                else
                {
                    context.Logger?.Info("[AutoConfirm] Previous foreground window was closed during execution; skipping restoration.");
                }
            }
            else if (isAlreadyForeground)
            {
                context.Logger?.Info($"[AutoConfirm] ForegroundPulse (target already foreground): Enter sent at +{enterSentAtMs}ms (no focus switch).");
            }
            else if (FocusBehavior == FocusBehavior.KeepTargetForeground)
            {
                context.Logger?.Info($"[AutoConfirm] ForegroundPulse (KeepTargetForeground): activation confirmed at +{activationConfirmedAtMs}ms, Enter sent at +{enterSentAtMs}ms, target kept foreground.");
            }

            // Step 6: Wait for prompt acknowledgement based on prompt fingerprint
            var ackSw = Stopwatch.StartNew();
            bool promptAcknowledged = false;
            string initialFingerprint = revalSnapshot.Envelope?.ComputePromptFingerprint()
                ?? $"{revalSnapshot.CommandText}\n{revalSnapshot.PromptText}\n{revalSnapshot.SelectedOptionText}";

            while (ackSw.ElapsedMilliseconds < 2000)
            {
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return MacroActionResult.Cancelled();
                }

                var ackDetect = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
                if (!ackDetect.Matched)
                {
                    promptAcknowledged = true;
                    break;
                }

                string ackRaw = ackDetect.RawText ?? ackDetect.ObservedText ?? string.Empty;
                var currentExtraction = context.CommandPromptParser.Parse(ackRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
                var currentSnapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, ackRaw, currentExtraction);
                string currentFingerprint = currentSnapshot.Envelope?.ComputePromptFingerprint()
                    ?? $"{currentSnapshot.CommandText}\n{currentSnapshot.PromptText}\n{currentSnapshot.SelectedOptionText}";

                if (!string.Equals(currentFingerprint, initialFingerprint, StringComparison.Ordinal))
                {
                    promptAcknowledged = true;
                    break;
                }
            }

            if (!promptAcknowledged)
            {
                context.Logger?.Warning("[AutoConfirm] Prompt remained visible after Enter injection. ConfirmationNotAcknowledged.");
                return MacroActionResult.ApprovalBlocked("Prompt remained visible after Enter injection. ConfirmationNotAcknowledged.");
            }

            if (PolicyMode == ApprovalPolicyMode.FoolMode)
            {
                context.Logger?.Info($"[AutoConfirm][FOOL MODE] Prompt dismissed in {ackSw.ElapsedMilliseconds}ms. Execution confirmed.");
                return MacroActionResult.Success($"[FOOL MODE] Auto-confirmed unrestricted command \"{cmdLog}\".");
            }
            else
            {
                string matchedRuleMsg = !string.IsNullOrEmpty(revalDecision.MatchedRuleName)
                    ? $" using rule '{revalDecision.MatchedRuleName}'"
                    : string.Empty;
                context.Logger?.Info($"[AutoConfirm] Prompt dismissed in {ackSw.ElapsedMilliseconds}ms. Execution confirmed.");
                return MacroActionResult.Success($"Auto-confirmed command \"{cmdLog}\"{matchedRuleMsg}.");
            }
        }
        finally
        {
            // Restore previous foreground window if activation occurred and restoration was not already completed
            if (wasActivated &&
                !isAlreadyForeground &&
                !foregroundRestored &&
                FocusBehavior == FocusBehavior.FastPulse &&
                restoreTargetHwnd != IntPtr.Zero &&
                restoreTargetHwnd != targetHwnd &&
                restoreTargetHwnd != effectiveRoot)
            {
                IntPtr currentFg = context.ForegroundService.GetForegroundWindow();
                if (currentFg != effectiveRoot && currentFg != targetHwnd)
                {
                    context.Logger?.Info("[AutoConfirm] User changed foreground during pulse; skipping automatic restoration in cleanup.");
                }
                else if (context.ForegroundService.IsWindow(restoreTargetHwnd))
                {
                    try
                    {
                        bool restored = context.ForegroundService.RestoreForegroundWindow(restoreTargetHwnd);
                        if (restored)
                        {
                            context.Logger?.Info("[AutoConfirm] previous foreground restored in cleanup");
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger?.Warning($"[AutoConfirm] Foreground restore exception in cleanup: {ex.Message}");
                    }
                }
            }
        }
    }

    private static string FormatLiveBlocker(
        ApprovalBlockReason? reason,
        string? explanation,
        CommandExtractionResult extraction)
    {
        return reason switch
        {
            ApprovalBlockReason.AmbiguousPrompt =>
                FormatAmbiguousStatus(extraction.AmbiguityReason ?? explanation ?? string.Empty),
            ApprovalBlockReason.CommandNotAllowed => "Blocked — Command not allowed",
            ApprovalBlockReason.OptionNotSelected => "Waiting — Option not selected",
            ApprovalBlockReason.CommandNotFound => "Waiting — Command not found",
            ApprovalBlockReason.PromptNotVisible => "Waiting — Prompt not visible",
            ApprovalBlockReason.TargetMismatch => "Blocked — Target mismatch",
            ApprovalBlockReason.TargetNotInteractable => "Blocked — Target not interactable",
            ApprovalBlockReason.ForegroundActivationFailed => "Blocked — Activation failed",
            _ => $"Blocked — {reason?.ToString() ?? "Unknown"}"
        };
    }

    private static string FormatAmbiguousStatus(string reason)
    {
        var match = System.Text.RegularExpressions.Regex.Match(reason, @"conflicting command candidates \((\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"Waiting — Ambiguous command ({match.Groups[1].Value} candidates)";
        }
        return "Waiting — Ambiguous command";
    }
}
