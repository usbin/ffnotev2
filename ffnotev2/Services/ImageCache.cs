using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace ffnotev2.Services;

/// <summary>
/// 경로(+ 다운샘플 폭) 단위로 frozen <see cref="BitmapImage"/>를 메모리 캐시한다.
/// 동일 이미지를 여러 노트가 참조해도 디스크 read는 1회. Freeze된 인스턴스라 thread-safe.
/// 캐시는 노트 삭제(이미지 노트) 또는 마크다운 변경에서 디스크 파일이 사라지면 stale —
/// 그 경우 <see cref="Invalidate"/>로 명시 제거.
/// </summary>
internal static class ImageCache
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly object _lock = new();

    public static BitmapImage? Get(string path, int? decodePixelWidth = null)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var key = decodePixelWidth.HasValue ? $"{path}|{decodePixelWidth.Value}" : path;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existing)) return existing;
        }

        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth.HasValue) bmp.DecodePixelWidth = decodePixelWidth.Value;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch
        {
            return null;
        }

        lock (_lock)
        {
            // 다른 스레드가 먼저 넣었을 가능성 처리
            if (_cache.TryGetValue(key, out var raced)) return raced;
            _cache[key] = bmp;
        }
        return bmp;
    }

    public static void Invalidate(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        lock (_lock)
        {
            // 해당 경로의 모든 폭 변형 키 제거
            var prefix = path + "|";
            var toRemove = _cache.Keys.Where(k => k == path || k.StartsWith(prefix)).ToList();
            foreach (var k in toRemove) _cache.Remove(k);
        }
    }
}
