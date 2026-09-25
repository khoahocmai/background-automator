using System.Diagnostics;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Macro action that polls a target window until expected text appears or a timeout expires.
/// Operates non-intrusively in the background using text detection backends (e.g. Windows UI Automation).
/// </summary>
public sealed class WaitForTextAction : IMacroAction
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    public string ExpectedText { get; }
    public TextMatchMode MatchMode { get; }
    public TimeSpan Timeout { get; }
    public TimeSpan PollInterval { get; }
    public TextDetectionScope Scope { get; }
    public bool VisibleOnly => Scope == TextDetectionScope.VisibleViewportOnly;
    public IntPtr OverrideHwnd { get; }

    public string Name => "Wait for Text";

    public string DisplayString =>
        $"{MatchMode} \"{ExpectedText}\" (Timeout: {Timeout.TotalSeconds:F0}s)";

    public WaitForTextAction(
        string expectedText,
        TextMatchMode matchMode = TextMatchMode.Contains,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        TextDetectionScope scope = TextDetectionScope.VisibleViewportOnly,
        IntPtr overrideHwnd = default)
    {
        ExpectedText = expectedText ?? string.Empty;
        MatchMode = matchMode;
        Timeout = timeout ?? DefaultTimeout;
        PollInterval = pollInterval ?? DefaultPollInterval;
        Scope = scope;
        OverrideHwnd = overrideHwnd;
    }

    public WaitForTextAction(
        string expectedText,
        TextMatchMode matchMode,
        TimeSpan? timeout,
        TimeSpan? pollInterval,
        bool visibleOnly,
        IntPtr overrideHwnd = default)
        : this(expectedText, matchMode, timeout, pollInterval, visibleOnly ? TextDetectionScope.VisibleViewportOnly : TextDetectionScope.DocumentBuffer, overrideHwnd)
    {
    }

    public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(ExpectedText))
        {
            return MacroActionResult.InvalidConfiguration("ExpectedText cannot be empty.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"Timeout must be positive: {Timeout}");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"PollInterval must be positive: {PollInterval}");
        }

        IntPtr targetHwnd = OverrideHwnd != IntPtr.Zero ? OverrideHwnd : context.TargetHwnd;
        if (targetHwnd == IntPtr.Zero || !User32.IsWindow(targetHwnd))
        {
            context.Logger?.Warning($"[WaitForText] Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
            return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
        }

        context.Logger?.Info($"[WaitForText] Started. Expected: \"{ExpectedText}\" (MatchMode: {MatchMode}, Timeout: {Timeout.TotalMilliseconds:F0}ms, Poll: {PollInterval.TotalMilliseconds:F0}ms)");

        var request = new TextDetectionRequest(ExpectedText, MatchMode, Scope);
        var sw = Stopwatch.StartNew();

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                context.Logger?.Info("[WaitForText] Cancelled.");
                return MacroActionResult.Cancelled();
            }

            if (!User32.IsWindow(targetHwnd))
            {
                context.Logger?.Warning($"[WaitForText] Target HWND {HwndFormatter.Format(targetHwnd)} closed during WaitForText.");
                return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed during WaitForText.");
            }

            TextDetectionResult result;
            try
            {
                result = await context.TextDetector.DetectAsync(targetHwnd, request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                context.Logger?.Info("[WaitForText] Cancelled during text detection.");
                return MacroActionResult.Cancelled();
            }
            catch (Exception ex)
            {
                context.Logger?.Warning($"[WaitForText] Detector failure: {ex.Message}");
                return MacroActionResult.TextDetectionFailed($"Text detection failed: {ex.Message}");
            }

            if (result.Matched)
            {
                context.Logger?.Info($"[WaitForText] Text matched: \"{ExpectedText}\" in {sw.ElapsedMilliseconds}ms");
                return MacroActionResult.Success($"Matched text \"{ExpectedText}\" in {sw.ElapsedMilliseconds}ms.");
            }

            if (sw.Elapsed >= Timeout)
            {
                break;
            }

            TimeSpan remaining = Timeout - sw.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            TimeSpan delay = remaining < PollInterval ? remaining : PollInterval;
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                context.Logger?.Info("[WaitForText] Cancelled during delay.");
                return MacroActionResult.Cancelled();
            }
        }

        context.Logger?.Warning($"[WaitForText] Timed out waiting for \"{ExpectedText}\" after {Timeout.TotalMilliseconds:F0}ms.");
        return MacroActionResult.Timeout(
            $"Timed out: Text \"{ExpectedText}\" ({MatchMode}) was not detected within {Timeout.TotalMilliseconds:F0}ms.");
    }
}
