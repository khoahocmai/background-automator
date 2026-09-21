using System.Drawing;
using BackgroundClicker.Core.Macro;

namespace BackgroundClicker.Core.Profiles;

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
            _ => throw new InvalidOperationException($"Unknown or unsupported macro action type '{ActionType}'.")
        };
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
