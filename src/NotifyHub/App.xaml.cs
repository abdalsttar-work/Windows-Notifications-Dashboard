using System;
using System.Linq;
using System.Windows;

namespace NotifyHub;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static void ApplyTheme(string? theme)
    {
        try
        {
            if (Current == null) return;

            var dicts = Current.Resources.MergedDictionaries;
            var existingTheme = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("ModernTheme") || d.Source.OriginalString.Contains("LightTheme")));

            string uri = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? "Styles/LightTheme.xaml"
                : "Styles/ModernTheme.xaml";

            var newDict = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Relative)
            };

            dicts.Add(newDict);
            if (existingTheme != null)
            {
                dicts.Remove(existingTheme);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
        }
    }
}
