using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Implements text detection using the Windows UI Automation / Accessibility tree.
/// Supports modern terminals (Windows Terminal / ConPTY TermControl via TextPattern),
/// standard Win32 edit controls, and WPF/UWP/WinUI accessibility elements.
/// </summary>
public sealed class UiAutomationTextDetectionService : ITextDetectionService
{
    private readonly IAppLogger? _logger;

    public UiAutomationTextDetectionService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public async Task<TextDetectionResult> DetectAsync(
        IntPtr targetHwnd,
        TextDetectionRequest request,
        CancellationToken ct = default)
    {
        if (targetHwnd == IntPtr.Zero || !User32.IsWindow(targetHwnd))
        {
            return TextDetectionResult.Failed($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
        }

        if (string.IsNullOrEmpty(request.ExpectedText))
        {
            return TextDetectionResult.Failed("Expected text cannot be null or empty.");
        }

        if (User32.IsHungAppWindow(targetHwnd))
        {
            _logger?.Warning($"Target window {HwndFormatter.Format(targetHwnd)} is hung.");
            return TextDetectionResult.Failed("Target window is hung and unresponsive.");
        }

        if (ct.IsCancellationRequested)
        {
            return TextDetectionResult.Failed("Detection cancelled.");
        }

        return await Task.Run(() => DetectInternal(targetHwnd, request, ct), ct).ConfigureAwait(false);
    }

    private TextDetectionResult DetectInternal(
        IntPtr targetHwnd,
        TextDetectionRequest request,
        CancellationToken ct)
    {
        try
        {
            if (!User32.IsWindow(targetHwnd))
            {
                return TextDetectionResult.Failed($"Target HWND {HwndFormatter.Format(targetHwnd)} closed before detection.");
            }

            AutomationElement root;
            try
            {
                root = AutomationElement.FromHandle(targetHwnd);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"FromHandle failed for HWND {HwndFormatter.Format(targetHwnd)}: {ex.Message}");
                return TextDetectionResult.Failed($"Failed to get AutomationElement: {ex.Message}");
            }

            if (root == null)
            {
                return TextDetectionResult.Failed($"AutomationElement.FromHandle returned null for HWND {HwndFormatter.Format(targetHwnd)}.");
            }

            // 1. Inspect root element directly
            if (TryEvaluateElement(root, request, out TextDetectionResult? rootResult))
            {
                return rootResult!;
            }

            if (ct.IsCancellationRequested)
            {
                return TextDetectionResult.Failed("Detection cancelled.");
            }

            // 2. Look for specialized terminal/editor controls first (e.g. TermControl in Windows Terminal)
            try
            {
                var termCond = new PropertyCondition(AutomationElement.ClassNameProperty, "TermControl");
                var termControls = root.FindAll(TreeScope.Descendants, termCond);
                if (termControls != null && termControls.Count > 0)
                {
                    var paneList = new List<TerminalPaneInfo>();
                    TextDetectionResult? firstMatchResult = null;

                    for (int i = 0; i < termControls.Count; i++)
                    {
                        var tc = termControls[i];
                        try
                        {
                            bool hasFocus = tc.Current.HasKeyboardFocus;
                            var bounds = tc.Current.BoundingRectangle;
                            string clsName = tc.Current.ClassName;

                            string paneRawText = string.Empty;
                            if (tc.TryGetCurrentPattern(TextPattern.Pattern, out object tpObj) && tpObj is TextPattern tp)
                            {
                                if (request.Scope == TextDetectionScope.VisibleViewportOnly)
                                {
                                    var visRanges = tp.GetVisibleRanges();
                                    if (visRanges != null && visRanges.Length > 0)
                                    {
                                        var sb = new StringBuilder();
                                        foreach (var r in visRanges)
                                        {
                                            sb.Append(r.GetText(-1));
                                        }
                                        paneRawText = sb.ToString();
                                    }
                                }
                                else
                                {
                                    paneRawText = tp.DocumentRange.GetText(-1) ?? string.Empty;
                                }
                            }

                            var paneInfo = new TerminalPaneInfo(paneRawText, hasFocus, bounds, tc, clsName);
                            paneList.Add(paneInfo);

                            if (!string.IsNullOrEmpty(paneRawText) &&
                                TextMatcher.IsMatch(paneRawText, request.ExpectedText, request.MatchMode))
                            {
                                firstMatchResult ??= TextDetectionResult.Success(paneRawText, paneRawText);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug($"Error reading TermControl [{i}]: {ex.Message}");
                        }
                    }

                    if (paneList.Count > 0)
                    {
                        if (firstMatchResult != null)
                        {
                            return TextDetectionResult.Success(firstMatchResult.ObservedText!, firstMatchResult.RawText, paneList);
                        }

                        string lastText = paneList[^1].RawText;
                        return TextDetectionResult.NotFound(lastText, $"Expected text '{request.ExpectedText}' was not found in terminal pane(s).", lastText, paneList);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"TermControl search exception: {ex.Message}");
            }

            if (ct.IsCancellationRequested)
            {
                return TextDetectionResult.Failed("Detection cancelled.");
            }

            // 3. Inspect descendants for TextPattern or ValuePattern or Name
            AutomationElementCollection descendants;
            try
            {
                descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"FindAll descendants failed: {ex.Message}");
                return TextDetectionResult.Failed($"Failed to enumerate descendants: {ex.Message}");
            }

            string? lastObserved = null;

            foreach (AutomationElement child in descendants)
            {
                if (ct.IsCancellationRequested)
                {
                    return TextDetectionResult.Failed("Detection cancelled.");
                }

                if (TryEvaluateElement(child, request, out TextDetectionResult? childResult))
                {
                    return childResult!;
                }

                if (childResult?.ObservedText != null)
                {
                    lastObserved = childResult.ObservedText;
                }
            }

            return TextDetectionResult.NotFound(lastObserved, $"Expected text '{request.ExpectedText}' was not found in target window.");
        }
        catch (COMException comEx)
        {
            _logger?.Debug($"COM exception during UI Automation detection: {comEx.Message}");
            return TextDetectionResult.Failed($"UI Automation COM error: {comEx.Message}");
        }
        catch (ElementNotAvailableException enaEx)
        {
            _logger?.Debug($"Element not available during UI Automation detection: {enaEx.Message}");
            return TextDetectionResult.Failed($"UI Automation element unavailable: {enaEx.Message}");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Unexpected exception during UI Automation detection: {ex.Message}");
            return TextDetectionResult.Failed($"Detection failed: {ex.Message}");
        }
    }

    private static bool TryEvaluateElement(
        AutomationElement element,
        TextDetectionRequest request,
        out TextDetectionResult? result)
    {
        result = null;

        try
        {
            // A. TextPattern (richest accessibility text provider, used by Windows Terminal, Word, RichEdit)
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object tpObj) && tpObj is TextPattern tp)
            {
                // When VisibleViewportOnly is requested, strictly inspect visible viewport ranges only.
                // Do NOT fall back to DocumentRange because DocumentRange contains scrolled-away history prompts.
                if (request.Scope == TextDetectionScope.VisibleViewportOnly)
                {
                    try
                    {
                        TextPatternRange[] visibleRanges = tp.GetVisibleRanges();
                        if (visibleRanges != null && visibleRanges.Length > 0)
                        {
                            var sbVis = new StringBuilder();
                            foreach (var r in visibleRanges)
                            {
                                sbVis.Append(r.GetText(-1));
                            }

                            string rawVisText = sbVis.ToString();
                            if (!string.IsNullOrEmpty(rawVisText))
                            {
                                if (TextMatcher.IsMatch(rawVisText, request.ExpectedText, request.MatchMode))
                                {
                                    result = TextDetectionResult.Success(rawVisText, rawVisText);
                                    return true;
                                }

                                result = TextDetectionResult.NotFound(rawVisText, $"Expected text '{request.ExpectedText}' was not found in visible viewport.", rawVisText);
                                return false;
                            }
                        }

                        // Visible ranges extraction returned null or empty
                        result = TextDetectionResult.Failed("Failed to extract visible viewport ranges from target control.");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        // Explicitly fail closed: do NOT search terminal history when VisibleViewportOnly is requested
                        result = TextDetectionResult.Failed($"Visible viewport extraction failed: {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    // DocumentBuffer scope: inspect full document range
                    try
                    {
                        string fullText = tp.DocumentRange.GetText(-1);
                        if (!string.IsNullOrEmpty(fullText))
                        {
                            if (TextMatcher.IsMatch(fullText, request.ExpectedText, request.MatchMode))
                            {
                                result = TextDetectionResult.Success(fullText, fullText);
                                return true;
                            }

                            result = TextDetectionResult.NotFound(fullText, $"Expected text '{request.ExpectedText}' was not found in document buffer.", fullText);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        result = TextDetectionResult.Failed($"Document buffer extraction failed: {ex.Message}");
                        return false;
                    }
                }
            }

            // B. ValuePattern (standard Win32/WPF edit controls and text boxes)
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object vpObj) && vpObj is ValuePattern vp)
            {
                string val = vp.Current.Value;
                if (!string.IsNullOrEmpty(val))
                {
                    if (TextMatcher.IsMatch(val, request.ExpectedText, request.MatchMode))
                    {
                        result = TextDetectionResult.Success(val, val);
                        return true;
                    }

                    result = TextDetectionResult.NotFound(val, $"Expected text '{request.ExpectedText}' was not found in control value.", val);
                }
            }

            // C. Name Property (static text, labels, buttons, list items)
            string name = element.Current.Name;
            if (!string.IsNullOrEmpty(name))
            {
                if (TextMatcher.IsMatch(name, request.ExpectedText, request.MatchMode))
                {
                    result = TextDetectionResult.Success(name, name);
                    return true;
                }

                result = TextDetectionResult.NotFound(name, $"Expected text '{request.ExpectedText}' was not found in control name.", name);
            }
        }
        catch
        {
            // Element state might have changed or disconnected
        }

        return false;
    }
}
