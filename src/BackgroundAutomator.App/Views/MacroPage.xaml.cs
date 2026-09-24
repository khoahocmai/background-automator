using System.Windows.Controls;
using BackgroundAutomator.App.ViewModels;

namespace BackgroundAutomator.App.Views;

public partial class MacroPage : Page
{
    public MacroPage(MacroViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
