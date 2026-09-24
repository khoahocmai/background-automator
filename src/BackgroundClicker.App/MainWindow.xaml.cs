using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using BackgroundClicker.App.Hotkeys;
using BackgroundClicker.App.Services;
using BackgroundClicker.App.ViewModels;
using BackgroundClicker.App.Views;
using BackgroundClicker.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BackgroundClicker.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly PageService _pageService;
    private GlobalHotkeyManager? _hotkeyManager;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        InitializeComponent();

        _pageService = new PageService();
        _pageService.RegisterPage(new TargetPage(_viewModel.TargetVM));
        _pageService.RegisterPage(new SimplePage(_viewModel.SimpleVM));
        _pageService.RegisterPage(new MacroPage(_viewModel.MacroVM));
        _pageService.RegisterPage(new ProfilesPage(_viewModel.ProfilesVM));
        _pageService.RegisterPage(new DiagnosticsPage(_viewModel.DiagnosticsVM));

        RootNavigation.SetPageService(_pageService);
        RootNavigation.Navigated += OnNavigated;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        _viewModel.Initialize(hwnd);

        // Register Global Hotkeys (F6 and F7)
        _hotkeyManager = new GlobalHotkeyManager(
            hwnd,
            onStartStop: () => Dispatcher.InvokeAsync(() => _viewModel.HandleF6()),
            onEmergencyStop: () => Dispatcher.InvokeAsync(() => _viewModel.HandleF7()));

        _hotkeyManager.RegisterHotkeys();

        // Navigate to default page
        RootNavigation.Navigate(typeof(TargetPage));
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page != null)
        {
            _viewModel.CurrentActivePageType = args.Page.GetType();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)NativeConstants.WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (_hotkeyManager?.ProcessHotkey(hotkeyId) == true)
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        var newTheme = currentTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(newTheme);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        _hotkeyManager?.Dispose();
        _viewModel.Cleanup();
    }
}
