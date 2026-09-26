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

    // SafeAutoConfirm properties (legacy single-rule compatibility)
    public string? RuleName { get; set; }
    public string? ExpectedProcess { get; set; }
    public string? ExpectedWindowClass { get; set; }
    public string? ExpectedPrompt { get; set; }
    public string? ExpectedSelectedOption { get; set; }
    public string? AllowedCommand { get; set; }
    public string? CommandMatchMode { get; set; }
    public string? ExecutionMode { get; set; }
    public string? DeliveryMode { get; set; }
    public string? WaitMode { get; set; }
    public string? FocusBehavior { get; set; }

    // SafeAutoConfirm multi-rule set property
    public ApprovalRuleSetConfig? RuleSet { get; set; }

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
                RuleName = saca.RuleSet.Name,
                ExpectedProcess = saca.RuleSet.ExpectedProcess,
                ExpectedWindowClass = saca.RuleSet.ExpectedWindowClass,
                ExpectedPrompt = saca.RuleSet.ExpectedPrompt,
                ExpectedSelectedOption = saca.RuleSet.ExpectedSelectedOption,
                AllowedCommand = saca.RuleSet.Rules.FirstOrDefault()?.AllowedCommand ?? string.Empty,
                CommandMatchMode = (saca.RuleSet.Rules.FirstOrDefault()?.CommandMatchMode ?? Approval.CommandMatchMode.Exact).ToString(),
                ExecutionMode = saca.ExecutionMode.ToString(),
                DeliveryMode = saca.DeliveryMode.ToString(),
                WaitMode = saca.WaitMode.ToString(),
                FocusBehavior = saca.FocusBehavior.ToString(),
                TimeoutMs = (int)saca.Timeout.TotalMilliseconds,
                PollIntervalMs = (int)saca.PollInterval.TotalMilliseconds,
                RuleSet = new ApprovalRuleSetConfig
                {
                    Id = saca.RuleSet.Id,
                    Name = saca.RuleSet.Name,
                    ExpectedProcess = saca.RuleSet.ExpectedProcess,
                    ExpectedWindowClass = saca.RuleSet.ExpectedWindowClass,
                    ExpectedPrompt = saca.RuleSet.ExpectedPrompt,
                    ExpectedSelectedOption = saca.RuleSet.ExpectedSelectedOption,
                    Rules = saca.RuleSet.Rules.Select(r => new CommandApprovalRuleConfig
                    {
                        Id = r.Id,
                        Name = r.Name,
                        AllowedCommand = r.AllowedCommand,
                        CommandMatchMode = r.CommandMatchMode.ToString(),
                        Enabled = r.Enabled
                    }).ToList()
                }
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
            "safeautoconfirm" => CreateSafeAutoConfirmAction(),
            _ => throw new InvalidOperationException($"Unknown or unsupported macro action type '{ActionType}'.")
        };
    }

    private SafeAutoConfirmAction CreateSafeAutoConfirmAction()
    {
        ApprovalRuleSet ruleSet;
        if (RuleSet != null && RuleSet.Rules.Count > 0)
        {
            ruleSet = new ApprovalRuleSet
            {
                Id = RuleSet.Id ?? Guid.NewGuid(),
                Name = RuleSet.Name ?? RuleName ?? "Safe Auto Confirm",
                ExpectedProcess = RuleSet.ExpectedProcess ?? ExpectedProcess ?? "WindowsTerminal.exe",
                ExpectedWindowClass = RuleSet.ExpectedWindowClass ?? ExpectedWindowClass,
                ExpectedPrompt = RuleSet.ExpectedPrompt ?? ExpectedPrompt ?? "Run this command?",
                ExpectedSelectedOption = RuleSet.ExpectedSelectedOption ?? ExpectedSelectedOption ?? "Yes, run command",
                Rules = RuleSet.Rules.Select(rc => new CommandApprovalRule
                {
                    Id = rc.Id ?? Guid.NewGuid(),
                    Name = rc.Name ?? "Approve Command",
                    AllowedCommand = rc.AllowedCommand ?? string.Empty,
                    CommandMatchMode = ParseCommandMatchMode(rc.CommandMatchMode),
                    Enabled = rc.Enabled,
                    ExpectedProcess = RuleSet.ExpectedProcess ?? ExpectedProcess ?? "WindowsTerminal.exe",
                    ExpectedWindowClass = RuleSet.ExpectedWindowClass ?? ExpectedWindowClass,
                    ExpectedPrompt = RuleSet.ExpectedPrompt ?? ExpectedPrompt ?? "Run this command?",
                    ExpectedSelectedOption = RuleSet.ExpectedSelectedOption ?? ExpectedSelectedOption ?? "Yes, run command"
                }).ToList()
            };
        }
        else
        {
            var singleRule = new CommandApprovalRule
            {
                Name = RuleName ?? "Safe Auto Confirm",
                ExpectedProcess = ExpectedProcess ?? "WindowsTerminal.exe",
                ExpectedWindowClass = ExpectedWindowClass,
                ExpectedPrompt = ExpectedPrompt ?? "Run this command?",
                ExpectedSelectedOption = ExpectedSelectedOption ?? "Yes, run command",
                AllowedCommand = AllowedCommand ?? string.Empty,
                CommandMatchMode = ParseCommandMatchMode(CommandMatchMode),
                Enabled = true
            };
            ruleSet = ApprovalRuleSet.FromSingleRule(singleRule);
        }

        return new SafeAutoConfirmAction(
            ruleSet,
            executionMode: ParseExecutionMode(ExecutionMode),
            deliveryMode: ParseDeliveryMode(DeliveryMode),
            timeout: TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(TimeoutMs.Value) : null,
            pollInterval: PollIntervalMs.HasValue ? TimeSpan.FromMilliseconds(PollIntervalMs.Value) : null,
            waitMode: ParseWaitMode(WaitMode),
            focusBehavior: ParseFocusBehavior(FocusBehavior));
    }

    private static FocusBehavior ParseFocusBehavior(string? behavior)
    {
        if (Enum.TryParse<FocusBehavior>(behavior, ignoreCase: true, out var result))
        {
            return result;
        }
        return Keyboard.FocusBehavior.FastPulse;
    }

    private static AutoConfirmWaitMode ParseWaitMode(string? waitMode)
    {
        if (Enum.TryParse<AutoConfirmWaitMode>(waitMode, ignoreCase: true, out var result))
        {
            return result;
        }
        return AutoConfirmWaitMode.FixedTimeout;
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
