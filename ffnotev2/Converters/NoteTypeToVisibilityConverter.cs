using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ffnotev2.Models;

namespace ffnotev2.Converters;

public class NoteTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NoteType type && parameter is string expected
            && Enum.TryParse<NoteType>(expected, out var match))
        {
            return type == match ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
