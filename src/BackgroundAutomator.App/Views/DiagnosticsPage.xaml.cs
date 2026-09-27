using System.Windows.Controls;
using System.Windows.Threading;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.ViewModels;

namespace BackgroundAutomator.App.Views;

public partial class DiagnosticsPage : Page
{
    private DiagnosticsViewModel ViewModel => (DiagnosticsViewModel)DataContext;
    public ListBox LogItemsControl => LogListBox;
    private LogMessageItem? _pendingScrollTarget;
    private bool _scrollScheduled;

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ScrollRequested += OnScrollRequested;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel.AutoScroll && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }

    private void OnScrollRequested(LogMessageItem item)
    {
        _pendingScrollTarget = item;
        if (_scrollScheduled)
            return;

        _scrollScheduled = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollScheduled = false;
            var target = _pendingScrollTarget;
            _pendingScrollTarget = null;

            if (ViewModel.AutoScroll && target != null && LogListBox.Items.Count > 0)
            {
                try
                {
                    LogListBox.ScrollIntoView(target);
                }
                catch
                {
                    if (LogListBox.Items.Count > 0)
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                    }
                }
            }
        });
    }
}
