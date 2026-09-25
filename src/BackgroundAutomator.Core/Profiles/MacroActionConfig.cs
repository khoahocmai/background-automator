using System.Drawing;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Macro;

namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Serializable configuration representing an individual action within a macro.
/// </summary>
public sealed class MacroActionConfig
{
    public string ActionType { get; set; } = string.Empty;
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? DelayMs { get; set; }
    public string? ColorHex { get; set; }
    public int? Tolerance { get; set; }
    public int? TimeoutMs { get; set; }
    public int? PollIntervalMs { get; set; }
    public string? Key { get; set; }
    public string? ExpectedText { get; set; }
    public string? TextMatchMode { get; set; }

    // SafeAutoConfirm properties
    public string? RuleName { get; set; }
    public string? ExpectedProcess { get; set; }
    public string? ExpectedWindowClass { get; set; }
    public string? ExpectedPrompt { get; set; }
    public string? ExpectedSelectedOption { get; set; }
    public string? AllowedCommand { get; set; }
    public string? CommandMatchMode { get; set; }
    public string? ExecutionMode { get; set; }
    public string? DeliveryMode { get; set; }

    public static MacroActionConfig FromMacroAction(IMacroAction action)
    {
        return action switch
        {
            ClickAction ca => new MacroActionConfig
            {
                ActionType = "Click",
                X = ca.ClientX,
                Y = ca.ClientY
            },
            DoubleClickAction dca => new MacroActionConfig
            {
                ActionType = "DoubleClick",
                X = dca.ClientX,
                Y = dca.ClientY
            },
            DelayAction da => new MacroActionConfig
            {
                ActionType = "Delay",
                DelayMs = da.Milliseconds
            },
            WaitColorAction wca => new MacroActionConfig
            {
                ActionType = "WaitColor",
                X = wca.ClientX,
                Y = wca.ClientY,
                ColorHex = $"#{wca.TargetColor.R:X2}{wca.TargetColor.G:X2}{wca.TargetColor.B:X2}",
                Tolerance = wca.Tolerance,
                TimeoutMs = (int)wca.Timeout.TotalMilliseconds,
                PollIntervalMs = (int)wca.PollInterval.TotalMilliseconds
            },
            PressKeyAction pka => new MacroActionConfig
            {
                ActionType = "PressKey",
                Key = pka.Key.ToString()
            },
            WaitForTextAction wta => new MacroActionConfig
            {
                ActionType = "WaitForText",
                ExpectedText = wta.ExpectedText,
                TextMatchMode = wta.MatchMode.ToString(),
                TimeoutMs = (int)wta.Timeout.TotalMilliseconds,
                PollIntervalMs = (int)wta.PollInterval.TotalMilliseconds
            },
            SafeAutoConfirmAction saca => new MacroActionConfig
            {
                ActionType = "SafeAutoConfirm",
                RuleName = saca.Rule.Name,
                ExpectedProcess = saca.Rule.ExpectedProcess,
                ExpectedWindowClass = saca.Rule.ExpectedWindowClass,
                ExpectedPrompt = saca.Rule.ExpectedPrompt,
                ExpectedSelectedOption = saca.Rule.ExpectedSelectedOption,
                AllowedCommand = saca.Rule.AllowedCommand,
                CommandMatchMode = saca.Rule.CommandMatchMode.ToString(),
                ExecutionMode = saca.ExecutionMode.ToString(),
                DeliveryMode = saca.DeliveryMode.ToString(),
                TimeoutMs = (int)saca.Timeout.TotalMilliseconds,
                PollIntervalMs = (int)saca.PollInterval.TotalMilliseconds
            },
            _ => throw new NotSupportedException($"Macro action type '{action.GetType().Name}' is not supported for serialization.")
        };
    }

    public IMacroAction ToMacroAction()
    {
        return ActionType?.ToLowerInvariant() switch
        {
            "click" => new ClickAction(X ?? 0, Y ?? 0),
            "doubleclick" => new DoubleClickAction(X ?? 0, Y ?? 0),
            "delay" => new DelayAction(Math.Max(1, DelayMs ?? 1000)),
            "waitcolor" => new WaitColorAction(
                X ?? 0,
                Y ?? 0,
                ParseColor(ColorHex),
                tolerance: Tolerance ?? 0,
                timeout: TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(TimeoutMs.Value) : null,
                pollInterval: PollIntervalMs.HasValue ? TimeSpan.FromMilliseconds(PollIntervalMs.Value) : null),
            "presskey" => new PressKeyAction(ParseKey(Key)),
            "waitfortext" => new WaitForTextAction(
                ExpectedText ?? string.Empty,
                matchMode: ParseMatchMode(TextMatchMode),
                timeout: TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(TimeoutMs.Value) : null,
                pollInterval: PollIntervalMs.HasValue ? TimeSpan.FromMilliseconds(PollIntervalMs.Value) : null),
            "safeautoconfirm" => new SafeAutoConfirmAction(
                new CommandApprovalRule
                {
                    Name = RuleName ?? "Safe Auto Confirm",
                    ExpectedProcess = ExpectedProcess ?? "WindowsTerminal.exe",
                    ExpectedWindowClass = ExpectedWindowClass,
                    ExpectedPrompt = ExpectedPrompt ?? "Run this command?",
                    ExpectedSelectedOption = ExpectedSelectedOption ?? "Yes, run command",
                    AllowedCommand = AllowedCommand ?? string.Empty,
                    CommandMatchMode = ParseCommandMatchMode(CommandMatchMode),
                    Enabled = true
                },
                executionMode: ParseExecutionMode(ExecutionMode),
                deliveryMode: ParseDeliveryMode(DeliveryMode),
                timeout: TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(TimeoutMs.Value) : null,
                pollInterval: PollIntervalMs.HasValue ? TimeSpan.FromMilliseconds(PollIntervalMs.Value) : null),
            _ => throw new InvalidOperationException($"Unknown or unsupported macro action type '{ActionType}'.")
        };
    }

    private static TextDetection.TextMatchMode ParseMatchMode(string? matchMode)
    {
        if (Enum.TryParse<TextDetection.TextMatchMode>(matchMode, ignoreCase: true, out var result))
        {
            return result;
        }
        return TextDetection.TextMatchMode.Contains;
    }

    private static BackgroundKey ParseKey(string? key)
    {
        if (Enum.TryParse<BackgroundKey>(key, ignoreCase: true, out var result))
        {
            return result;
        }
        return BackgroundKey.Enter;
    }

    private static Approval.CommandMatchMode ParseCommandMatchMode(string? matchMode)
    {
        if (Enum.TryParse<Approval.CommandMatchMode>(matchMode, ignoreCase: true, out var result))
        {
            return result;
        }
        return Approval.CommandMatchMode.Exact;
    }

    private static AutoConfirmExecutionMode ParseExecutionMode(string? mode)
    {
        if (Enum.TryParse<AutoConfirmExecutionMode>(mode, ignoreCase: true, out var result))
        {
            return result;
        }
        return AutoConfirmExecutionMode.ObserveOnly;
    }

    private static KeyDeliveryMode ParseDeliveryMode(string? mode)
    {
        if (Enum.TryParse<KeyDeliveryMode>(mode, ignoreCase: true, out var result))
        {
            return result;
        }
        return KeyDeliveryMode.ForegroundPulse;
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Color.Black;

        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Color.Black;
        }
    }
}
