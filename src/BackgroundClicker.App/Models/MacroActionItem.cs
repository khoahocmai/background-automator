using CommunityToolkit.Mvvm.ComponentModel;
using BackgroundClicker.Core.Macro;

namespace BackgroundClicker.App.Models;

public sealed partial class MacroActionItem : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _details;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private string _statusColor = "#888888";

    public IMacroAction Action { get; }

    public MacroActionItem(int index, IMacroAction action)
    {
        _index = index;
        Action = action;
        _name = action.Name;
        _details = action.DisplayString;
    }

    public void SetStatus(string status, string color)
    {
        Status = status;
        StatusColor = color;
    }
}
