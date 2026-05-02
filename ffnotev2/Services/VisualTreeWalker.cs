using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ffnotev2.Services;

/// <summary>
/// 마우스 이벤트의 OriginalSource는 FlowDocument 내부의 `Run`/`Span` 같은 비-Visual 요소일 수 있다.
/// `VisualTreeHelper.GetParent`는 Visual/Visual3D만 받아서 비-Visual에 대해 InvalidOperationException을 던지므로,
/// 이 헬퍼는 Visual이면 Visual 트리, 아니면 Logical 트리를 따라 부모로 거슬러 올라간다.
/// </summary>
internal static class VisualTreeWalker
{
    public static DependencyObject? GetAnyParent(DependencyObject d)
    {
        return (d is Visual || d is Visual3D)
            ? VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);
    }
}
