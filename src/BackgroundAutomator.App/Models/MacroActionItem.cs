using CommunityToolkit.Mvvm.ComponentModel;
using BackgroundAutomator.Core.Macro;

namespace BackgroundAutomator.App.Models;

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

    public IMacroAction Action { get; private set; }

    public MacroActionItem(int index, IMacroAction action)
    {
        _index = index;
        Action = action;
        _name = action.Name;
        _details = action.DisplayString;
    }

    public void UpdateAction(IMacroAction newAction)
    {
        Action = newAction ?? throw new ArgumentNullException(nameof(newAction));
        Name = newAction.Name;
        Details = newAction.DisplayString;
    }

    public void SetStatus(string status, string color)
    {
        Status = status;
        StatusColor = color;
    }
}
