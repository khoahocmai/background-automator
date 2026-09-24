using System.Windows.Controls;
using BackgroundClicker.App.ViewModels;

namespace BackgroundClicker.App.Views;

public partial class SimplePage : Page
{
    public SimplePage(SimpleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
