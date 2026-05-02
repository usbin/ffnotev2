using System.Globalization;
using System.Windows.Data;
using ffnotev2.Services;

namespace ffnotev2.Converters;

public class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 큰 원본(예: 4K 스크린샷)을 매 리사이즈 프레임마다 풀 해상도로 스케일링하면 비싸므로
        // 디코드 단계에서 가로 1200px로 다운샘플 (비율은 자동 유지). 일반 표시엔 충분한 화질.
        // ImageCache로 동일 경로 재로드 시 디스크 read 0회.
        if (value is not string path) return null;
        return ImageCache.Get(path, 1200);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
