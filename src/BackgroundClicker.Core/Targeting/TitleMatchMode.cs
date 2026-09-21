namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Mode used when matching window titles during target re-resolution.
/// </summary>
public enum TitleMatchMode
{
    Contains,
    Exact,
    StartsWith,
    Any
}
