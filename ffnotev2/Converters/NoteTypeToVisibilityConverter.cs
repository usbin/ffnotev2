using ffnotev2.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ffnotev2.Converters;

public class NoteTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NoteType type && parameter is string target &&
            Enum.TryParse<NoteType>(target, out var targetType2))
            return type == targetType2 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
