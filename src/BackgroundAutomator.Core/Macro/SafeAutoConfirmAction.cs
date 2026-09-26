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
    public TimeSpan Timeout { get; }
    public TimeSpan PollInterval { get; }
    public IntPtr OverrideHwnd { get; }

    public string Name => "Safe Auto Confirm";

    public string DisplayString
    {
        get
        {
            string waitDisplay = WaitMode == AutoConfirmWaitMode.Indefinite ? "Indefinite" : $"Timeout: {Timeout.TotalSeconds:F0}s";
            string focusDisplay = FocusBehavior == FocusBehavior.KeepTargetForeground ? " [KeepFG]" : string.Empty;
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
        FocusBehavior focusBehavior = FocusBehavior.FastPulse)
    {
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        ExecutionMode = executionMode;
        DeliveryMode = deliveryMode;
        Timeout = timeout ?? DefaultTimeout;
        PollInterval = pollInterval ?? DefaultPollInterval;
        OverrideHwnd = overrideHwnd;
        WaitMode = waitMode;
        FocusBehavior = focusBehavior;
    }

    public SafeAutoConfirmAction(
        CommandApprovalRule rule,
        AutoConfirmExecutionMode executionMode = AutoConfirmExecutionMode.ObserveOnly,
        KeyDeliveryMode deliveryMode = KeyDeliveryMode.ForegroundPulse,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        IntPtr overrideHwnd = default,
        AutoConfirmWaitMode waitMode = AutoConfirmWaitMode.FixedTimeout,
        FocusBehavior focusBehavior = FocusBehavior.FastPulse)
        : this(
            ApprovalRuleSet.FromSingleRule(rule ?? throw new ArgumentNullException(nameof(rule))),
            executionMode,
            deliveryMode,
            timeout,
            pollInterval,
            overrideHwnd,
            waitMode,
            focusBehavior)
    {
    }

    public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (RuleSet.Rules.Count == 0 || !RuleSet.Rules.Any(r => r.Enabled && !string.IsNullOrWhiteSpace(r.AllowedCommand)))
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

        IntPtr targetHwnd = OverrideHwnd != IntPtr.Zero ? OverrideHwnd : context.TargetHwnd;
        if (targetHwnd == IntPtr.Zero || !context.ForegroundService.IsWindow(targetHwnd))
        {
            context.Logger?.Warning($"[AutoConfirm] Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
            return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
        }

        // Section 18: Fail closed if target window is minimized
        IntPtr targetRootHwnd = context.ForegroundService.GetRootWindow(targetHwnd);
        if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
            (targetRootHwnd != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(targetRootHwnd)))
        {
            context.Logger?.Warning("[AutoConfirm] Target window is minimized. Auto-confirm blocked: TargetNotInteractable.");
            return MacroActionResult.ApprovalBlocked("Target window is minimized. TargetNotInteractable.");
        }

        // Section 3: Check UIPI elevation compatibility before any foreground or input attempt
        int initialPid = context.ForegroundService.GetProcessId(targetHwnd);
        var initialUipi = context.ElevationService.CheckCompatibility(initialPid);
        if (initialUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
        {
            context.Logger?.Warning($"[AutoConfirm] UIPI mismatch: Target PID {initialPid} is elevated while BackgroundAutomator is not. Auto-confirm blocked.");
            return MacroActionResult.ApprovalBlocked("UIPI mismatch: Target is running as Administrator while BackgroundAutomator is not. Auto-confirm blocked.");
        }

        string rulesSummary = string.Join(", ", RuleSet.Rules.Where(r => r.Enabled).Select(r => $"\"{r.AllowedCommand}\""));
        context.Logger?.Info($"[AutoConfirm] Started. RuleSet: \"{RuleSet.Name}\", Mode: {ExecutionMode}, Wait: {WaitMode}, Delivery: {DeliveryMode}, Rules: [{rulesSummary}]");

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

                var decision = CommandApprovalEvaluator.Evaluate(RuleSet, snapshot, actualProc, actualClass);

                if (decision.IsAllowed)
                {
                    context.Logger?.Info("[AutoConfirm] approval prompt detected");
                    context.Logger?.Info($"[AutoConfirm] command extracted: \"{snapshot.CommandText}\"");
                    string matchedRuleMsg = !string.IsNullOrEmpty(decision.MatchedRuleName)
                        ? $" using rule '{decision.MatchedRuleName}'"
                        : string.Empty;
                    context.Logger?.Info($"[AutoConfirm] Approved command '{snapshot.CommandText}'{matchedRuleMsg}.");

                    // 1. Observe-only mode: strictly passive (zero focus changes, zero keystrokes)
                    if (ExecutionMode == AutoConfirmExecutionMode.ObserveOnly)
                    {
                        context.Logger?.Info($"[AutoConfirm] WOULD APPROVE (ObserveOnly): \"{snapshot.CommandText}\"{matchedRuleMsg}");
                        return MacroActionResult.Success($"WOULD APPROVE: {decision.Explanation}");
                    }

                    // 2. Confirm mode: execute controlled foreground pulse
                    return await ExecuteConfirmPulseAsync(
                        context,
                        targetHwnd,
                        targetRootHwnd,
                        request,
                        snapshot,
                        ct).ConfigureAwait(false);
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

    private async Task<MacroActionResult> ExecuteConfirmPulseAsync(
        MacroExecutionContext context,
        IntPtr targetHwnd,
        IntPtr targetRootHwnd,
        TextDetectionRequest request,
        CommandPromptSnapshot initialSnapshot,
        CancellationToken ct)
    {
        IntPtr effectiveRoot = targetRootHwnd != IntPtr.Zero ? targetRootHwnd : targetHwnd;

        // Re-check minimized state before foreground pulse
        if (context.ForegroundService.IsWindowMinimized(targetHwnd) ||
            (effectiveRoot != IntPtr.Zero && context.ForegroundService.IsWindowMinimized(effectiveRoot)))
        {
            context.Logger?.Warning("[AutoConfirm] Target window is minimized prior to foreground pulse. TargetNotInteractable.");
            return MacroActionResult.ApprovalBlocked("Target window is minimized. TargetNotInteractable.");
        }

        // Re-check UIPI compatibility before requesting foreground activation
        int pulsePid = context.ForegroundService.GetProcessId(targetHwnd);
        var pulseUipi = context.ElevationService.CheckCompatibility(pulsePid);
        if (pulseUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
        {
            context.Logger?.Warning($"[AutoConfirm] UIPI mismatch detected prior to foreground activation for PID {pulsePid}.");
            return MacroActionResult.ApprovalBlocked("UIPI mismatch: Target is running as Administrator while BackgroundAutomator is not. Foreground activation blocked.");
        }

        IntPtr previousForeground = context.ForegroundService.GetForegroundWindow();
        bool isAlreadyForeground = (previousForeground == effectiveRoot || previousForeground == targetHwnd);
        context.Logger?.Info($"[AutoConfirm] previous foreground = 0x{previousForeground.ToInt64():X8}{(isAlreadyForeground ? " (target is already foreground)" : string.Empty)}");

        var pulseSw = Stopwatch.StartNew();
        long activationConfirmedAtMs = 0;
        long enterSentAtMs = 0;
        long previousRestoredAtMs = 0;
        bool foregroundRestored = false;
        bool wasActivated = false;

        if (!isAlreadyForeground)
        {
            // Step 1: Request foreground activation of the terminal root window
            bool activated = context.ForegroundService.ActivateWindow(effectiveRoot);
            if (!activated)
            {
                context.Logger?.Warning("[AutoConfirm] Failed to activate terminal window. ForegroundActivationFailed.");
                return MacroActionResult.ApprovalBlocked("Failed to request foreground activation of target terminal. ForegroundActivationFailed.");
            }
            wasActivated = true;

            // Step 2: Poll briefly until target/root window is confirmed as foreground
            bool confirmedForeground = false;
            var fgSw = Stopwatch.StartNew();
            while (fgSw.ElapsedMilliseconds < 500)
            {
                IntPtr currentFg = context.ForegroundService.GetForegroundWindow();
                if (currentFg == effectiveRoot || currentFg == targetHwnd)
                {
                    confirmedForeground = true;
                    break;
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }

            if (!confirmedForeground)
            {
                context.Logger?.Warning("[AutoConfirm] Target window did not become foreground. ForegroundActivationFailed.");
                return MacroActionResult.ApprovalBlocked("Terminal window failed to gain foreground focus. ForegroundActivationFailed.");
            }

            activationConfirmedAtMs = pulseSw.ElapsedMilliseconds;
            context.Logger?.Info($"[AutoConfirm] terminal foreground confirmed (+{activationConfirmedAtMs}ms)");
        }
        else
        {
            activationConfirmedAtMs = pulseSw.ElapsedMilliseconds;
            context.Logger?.Info($"[AutoConfirm] terminal already foreground (+{activationConfirmedAtMs}ms)");
        }

        try
        {
            // Step 3: CRITICAL REVALIDATION
            // Re-read visible viewport
            var revalDetect = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
            if (!revalDetect.Matched)
            {
                context.Logger?.Warning("[AutoConfirm] Rule revalidation failed: prompt is no longer visible after foreground activation.");
                return MacroActionResult.ApprovalBlocked("Approval prompt disappeared after foreground activation. RevalidationFailed.");
            }

            // Verify target window identity has not changed or closed
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

            // Re-check UIPI compatibility after activation
            int revalPid = context.ForegroundService.GetProcessId(targetHwnd);
            var revalUipi = context.ElevationService.CheckCompatibility(revalPid);
            if (revalUipi.Compatibility == Security.ElevationCompatibility.UipiMismatch)
            {
                context.Logger?.Warning($"[AutoConfirm] UIPI mismatch detected during revalidation for PID {revalPid}.");
                return MacroActionResult.ApprovalBlocked("UIPI mismatch detected during revalidation. Foreground input blocked.");
            }

            // Re-parse command
            string revalRaw = revalDetect.RawText ?? revalDetect.ObservedText ?? string.Empty;
            var revalExtraction = context.CommandPromptParser.Parse(revalRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
            var revalSnapshot = CommandPromptSnapshot.FromExtraction(targetHwnd, revalRaw, revalExtraction);

            // Re-evaluate complete rule set with fresh process identity and fresh snapshot
            var revalDecision = CommandApprovalEvaluator.Evaluate(RuleSet, revalSnapshot, freshProc, freshClass);
            if (!revalDecision.IsAllowed)
            {
                context.Logger?.Warning($"[AutoConfirm] Rule revalidation blocked: {revalDecision.BlockReason} — {revalDecision.Explanation}");
                return MacroActionResult.ApprovalBlocked($"Rule revalidation failed: {revalDecision.Explanation}");
            }

            context.Logger?.Info("[AutoConfirm] rule revalidated");

            // Step 4: Verify foreground ownership IMMEDIATELY before SendInput (no intervening async delay)
            IntPtr immediateFg = context.ForegroundService.GetForegroundWindow();
            if (immediateFg != effectiveRoot && immediateFg != targetHwnd)
            {
                context.Logger?.Warning($"[AutoConfirm] Foreground changed unexpectedly before SendInput! Active: 0x{immediateFg.ToInt64():X8}. Aborting.");
                return MacroActionResult.ApprovalBlocked("Foreground changed unexpectedly before input injection. ForegroundChanged.");
            }

            // Step 5: Dispatch Enter (maximum 1 dispatch per action execution)
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
            context.Logger?.Info($"[AutoConfirm] Enter sent (+{enterSentAtMs}ms)");

            // FastPulse optimization: Restore previous foreground IMMEDIATELY after Enter!
            if (FocusBehavior == FocusBehavior.FastPulse &&
                !isAlreadyForeground &&
                previousForeground != IntPtr.Zero &&
                previousForeground != targetHwnd &&
                previousForeground != effectiveRoot)
            {
                if (context.ForegroundService.IsWindow(previousForeground))
                {
                    try
                    {
                        bool restored = context.ForegroundService.RestoreForegroundWindow(previousForeground);
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

            // Step 6: Wait for prompt acknowledgement based on prompt fingerprint (performed while terminal is background again)
            var ackSw = Stopwatch.StartNew();
            bool promptAcknowledged = false;
            string initialCmd = revalSnapshot.CommandText ?? string.Empty;
            string initialOpt = revalSnapshot.SelectedOptionText ?? string.Empty;

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
                    // Generic prompt text disappeared completely
                    promptAcknowledged = true;
                    break;
                }

                // If prompt text matched, check if the specific command or selected option changed
                string ackRaw = ackDetect.RawText ?? ackDetect.ObservedText ?? string.Empty;
                var currentExtraction = context.CommandPromptParser.Parse(ackRaw, RuleSet.ExpectedPrompt, RuleSet.ExpectedSelectedOption);
                if (!currentExtraction.Success ||
                    !string.Equals(currentExtraction.CommandText, initialCmd, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentExtraction.SelectedOptionText, initialOpt, StringComparison.OrdinalIgnoreCase))
                {
                    // Fingerprint changed! Command was dismissed or replaced by subsequent prompt
                    promptAcknowledged = true;
                    break;
                }
            }

            if (!promptAcknowledged)
            {
                context.Logger?.Warning("[AutoConfirm] Prompt remained visible after Enter injection. ConfirmationNotAcknowledged.");
                return MacroActionResult.ApprovalBlocked("Prompt remained visible after Enter injection. ConfirmationNotAcknowledged.");
            }

            string matchedRuleMsg = !string.IsNullOrEmpty(revalDecision.MatchedRuleName)
                ? $" using rule '{revalDecision.MatchedRuleName}'"
                : string.Empty;
            context.Logger?.Info($"[AutoConfirm] Prompt dismissed in {ackSw.ElapsedMilliseconds}ms. Execution confirmed.");
            return MacroActionResult.Success($"Auto-confirmed command \"{revalSnapshot.CommandText}\"{matchedRuleMsg}.");
        }
        finally
        {
            // Restore previous foreground window if activation occurred and restoration was not already completed
            if (wasActivated &&
                !isAlreadyForeground &&
                !foregroundRestored &&
                FocusBehavior == FocusBehavior.FastPulse &&
                previousForeground != IntPtr.Zero &&
                previousForeground != targetHwnd &&
                previousForeground != effectiveRoot)
            {
                if (context.ForegroundService.IsWindow(previousForeground))
                {
                    try
                    {
                        bool restored = context.ForegroundService.RestoreForegroundWindow(previousForeground);
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
