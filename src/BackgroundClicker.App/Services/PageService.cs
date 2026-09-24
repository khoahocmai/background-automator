using System.Windows;
using Wpf.Ui;

namespace BackgroundClicker.App.Services;

public sealed class PageService : IPageService
{
    private readonly Dictionary<Type, FrameworkElement> _pages = new();

    public void RegisterPage<T>(T page) where T : FrameworkElement
    {
        _pages[typeof(T)] = page;
    }

    public T? GetPage<T>() where T : class
    {
        return _pages.TryGetValue(typeof(T), out var page) ? page as T : null;
    }

    public FrameworkElement? GetPage(Type pageType)
    {
        return _pages.TryGetValue(pageType, out var page) ? page : null;
    }
}
