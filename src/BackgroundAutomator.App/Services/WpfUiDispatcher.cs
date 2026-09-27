using System;
using System.Windows;
using System.Windows.Threading;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// WPF implementation of IUiDispatcher that posts work to the WPF Dispatcher queue.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher? _dispatcher;

    public WpfUiDispatcher(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
    }

    public void InvokeAsync(Action action)
    {
        var dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // Non-WPF or unit test fallback
            action();
            return;
        }

        if (dispatcher.HasShutdownStarted)
            return;

        dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }
}
