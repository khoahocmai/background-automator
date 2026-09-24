using CommunityToolkit.Mvvm.ComponentModel;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.Models;

public sealed partial class SimpleClickPointItem : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private IntPtr _hwnd;

    [ObservableProperty]
    private int _clientX;

    [ObservableProperty]
    private int _clientY;

    [ObservableProperty]
    private ClickType _clickType;

    public string HwndFormatted => HwndFormatter.Format(Hwnd);
    public string HwndShort => HwndFormatter.FormatShort(Hwnd);
    public string ActionDescription => ClickType == ClickType.Double ? "Double Click" : "Single Click";

    public SimpleClickPointItem(int index, ClickPoint point)
    {
        _index = index;
        _hwnd = point.Hwnd;
        _clientX = point.ClientX;
        _clientY = point.ClientY;
        _clickType = point.ClickType;
    }

    public ClickPoint ToClickPoint() => new(Hwnd, ClientX, ClientY, ClickType);
}
