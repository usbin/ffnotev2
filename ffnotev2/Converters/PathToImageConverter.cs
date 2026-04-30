using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ffnotev2.Converters;

public class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // 큰 원본(예: 4K 스크린샷)을 매 리사이즈 프레임마다 풀 해상도로 스케일링하면 비싸므로
        // 디코드 단계에서 가로 1200px로 다운샘플 (비율은 자동 유지). 일반 표시엔 충분한 화질
        bitmap.DecodePixelWidth = 1200;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
