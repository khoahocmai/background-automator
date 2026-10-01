using System;
using System.Threading;
using System.Windows;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// WPF implementation of clipboard service with bounded retry to handle temporary OS locks gracefully.
/// </summary>
public class WpfClipboardService : IClipboardService
{
    public bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        for (int i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception)
            {
                if (i < 2)
                {
                    Thread.Sleep(50);
                }
            }
        }

        return false;
    }

    public string? GetText()
    {
        try
        {
            return Clipboard.GetText();
        }
        catch
        {
            return null;
        }
    }
}
