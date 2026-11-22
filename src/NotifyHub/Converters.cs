using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NotifyHub.Models;
using MediaColor = System.Windows.Media.Color;

namespace NotifyHub.Converters;

public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}

public static class SourceColorPalette
{
    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (168, 85, 247),  // Purple
        (59, 130, 246),  // Blue
        (34, 197, 94),   // Emerald Green
        (14, 165, 233),  // Sky Cyan
        (249, 115, 22),  // Orange
        (236, 72, 153),  // Pink
        (99, 102, 241),  // Indigo
        (20, 184, 166),  // Teal
        (234, 179, 8),   // Amber
        (244, 63, 94)    // Rose
    };

    public static (byte R, byte G, byte B) GetBaseRgb(string? appName)
    {
        string name = (appName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
            return (148, 163, 184);

        if (name.Contains("slack")) return (168, 85, 247);      // Purple
        if (name.Contains("whatsapp")) return (34, 197, 94);    // Green
        if (name.Contains("telegram")) return (14, 165, 233);   // Sky Blue
        if (name.Contains("teams")) return (99, 102, 241);      // Indigo Blue
        if (name.Contains("outlook") || name.Contains("mail")) return (59, 130, 246); // Blue
        if (name.Contains("discord")) return (129, 140, 248);   // Blurple
        if (name.Contains("chrome") || name.Contains("edge")) return (249, 115, 22);  // Orange
        if (name.Contains("security") || name.Contains("defender")) return (244, 63, 94); // Rose

        // Deterministic FNV-1a hash so every unique app always gets the same color
        uint hash = 2166136261;
        foreach (char c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return Palette[hash % (uint)Palette.Length];
    }
}

public class SourceRowBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string? appName = values.Length > 0 ? values[0] as string : null;
        NotificationStatus status = values.Length > 1 && values[1] is NotificationStatus s
            ? s
            : NotificationStatus.Pending;

        // When marked Done, row changes to a calm muted green/gray tint
        if (status == NotificationStatus.Done)
        {
            return new SolidColorBrush(MediaColor.FromArgb(28, 100, 116, 139));
        }

        if (status == NotificationStatus.Archived)
        {
            return new SolidColorBrush(MediaColor.FromArgb(18, 100, 116, 139));
        }

        var (r, g, b) = SourceColorPalette.GetBaseRgb(appName);
        return new SolidColorBrush(MediaColor.FromArgb(42, r, g, b));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SourceAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var (r, g, b) = SourceColorPalette.GetBaseRgb(value as string);
        byte alpha = parameter is string p && byte.TryParse(p, out byte a) ? a : (byte)220;
        return new SolidColorBrush(MediaColor.FromArgb(alpha, r, g, b));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NotificationStatus status)
        {
            return status switch
            {
                NotificationStatus.Pending => new SolidColorBrush(MediaColor.FromRgb(238, 212, 159)),
                NotificationStatus.InProgress => new SolidColorBrush(MediaColor.FromRgb(145, 215, 227)),
                NotificationStatus.Done => new SolidColorBrush(MediaColor.FromRgb(166, 218, 149)),
                NotificationStatus.Archived => new SolidColorBrush(MediaColor.FromRgb(128, 135, 162)),
                _ => new SolidColorBrush(System.Windows.Media.Colors.LightGray)
            };
        }
        return new SolidColorBrush(System.Windows.Media.Colors.LightGray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PriorityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NotificationPriority priority)
        {
            return priority switch
            {
                NotificationPriority.Urgent => new SolidColorBrush(MediaColor.FromRgb(237, 135, 150)),
                NotificationPriority.High => new SolidColorBrush(MediaColor.FromRgb(245, 169, 127)),
                NotificationPriority.Normal => new SolidColorBrush(MediaColor.FromRgb(138, 173, 244)),
                NotificationPriority.Low => new SolidColorBrush(MediaColor.FromRgb(128, 135, 162)),
                _ => new SolidColorBrush(System.Windows.Media.Colors.LightGray)
            };
        }
        return new SolidColorBrush(System.Windows.Media.Colors.LightGray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ForwardingStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ForwardingStatus status)
        {
            return status switch
            {
                ForwardingStatus.Success => "✅ Sent",
                ForwardingStatus.PartialSuccess => "⚠️ Partial",
                ForwardingStatus.Failed => "❌ Failed",
                ForwardingStatus.Pending => "⏳ Sending",
                _ => "-"
            };
        }
        return "-";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
