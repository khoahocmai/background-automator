using System.Windows.Controls;
using BackgroundAutomator.App.ViewModels;

namespace BackgroundAutomator.App.Views;

public partial class SimplePage : Page
{
    public SimplePage(SimpleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
