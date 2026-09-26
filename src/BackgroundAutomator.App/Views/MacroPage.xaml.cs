using System.Windows.Controls;
using System.Windows.Input;
using BackgroundAutomator.App.ViewModels;

namespace BackgroundAutomator.App.Views;

public partial class MacroPage : Page
{
    private MacroViewModel ViewModel => (MacroViewModel)DataContext;

    public MacroPage(MacroViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnActionsListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedAction != null)
        {
            ViewModel.EditAction(ViewModel.SelectedAction);
        }
    }
}
