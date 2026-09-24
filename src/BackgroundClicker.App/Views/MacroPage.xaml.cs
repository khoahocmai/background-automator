using System.Windows.Controls;
using BackgroundClicker.App.ViewModels;

namespace BackgroundClicker.App.Views;

public partial class MacroPage : Page
{
    public MacroPage(MacroViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
