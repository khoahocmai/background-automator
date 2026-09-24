using System.Windows.Controls;
using System.Windows.Input;
using BackgroundClicker.App.ViewModels;

namespace BackgroundClicker.App.Views;

public partial class ProfilesPage : Page
{
    private ProfilesViewModel ViewModel => (ProfilesViewModel)DataContext;

    public ProfilesPage(ProfilesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnProfilesListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedProfile != null)
        {
            ViewModel.LoadSelectedProfile();
        }
    }
}
