using System.Collections.Specialized;
using System.Windows.Controls;
using BackgroundClicker.App.ViewModels;

namespace BackgroundClicker.App.Views;

public partial class DiagnosticsPage : Page
{
    private DiagnosticsViewModel ViewModel => (DiagnosticsViewModel)DataContext;

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        ((INotifyCollectionChanged)viewModel.FilteredEntries).CollectionChanged += (s, e) =>
        {
            if (ViewModel.AutoScroll && e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            }
        };
    }
}
