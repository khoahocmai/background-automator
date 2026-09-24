using System.Windows.Controls;
using System.Windows.Input;
using BackgroundClicker.App.ViewModels;
using BackgroundClicker.Win32;

namespace BackgroundClicker.App.Views;

public partial class TargetPage : Page
{
    private TargetViewModel ViewModel => (TargetViewModel)DataContext;

    public TargetPage(TargetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCrosshairMouseDown(object sender, MouseButtonEventArgs e)
    {
        CrosshairButton.CaptureMouse();
        Mouse.OverrideCursor = Cursors.Cross;
        ViewModel.StartCrosshairDrag();
    }

    private void OnCrosshairMouseMove(object sender, MouseEventArgs e)
    {
        if (CrosshairButton.IsMouseCaptured)
        {
            if (User32.GetCursorPos(out POINT pt))
            {
                ViewModel.UpdateCrosshairHover(pt);
            }
        }
    }

    private void OnCrosshairMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (CrosshairButton.IsMouseCaptured)
        {
            CrosshairButton.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
            ViewModel.FinishCrosshairDrag();
        }
    }

    private void OnCrosshairLostMouseCapture(object sender, MouseEventArgs e)
    {
        Mouse.OverrideCursor = null;
        ViewModel.CancelCrosshairDrag();
    }
}
