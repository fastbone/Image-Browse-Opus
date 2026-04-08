using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ImageBrowse.Helpers;

public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b) return 1.0;
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class RatingToStarsConverter : IValueConverter
{
    public static readonly RatingToStarsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rating && rating > 0)
            return new string('\u2605', rating) + new string('\u2606', 5 - rating);
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ThumbnailSizePlusConverter : IValueConverter
{
    public static readonly ThumbnailSizePlusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int size)
        {
            int extra = 50;
            if (parameter is string s && int.TryParse(s, out int e)) extra = e;
            return (double)(size + extra);
        }
        return 210.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
