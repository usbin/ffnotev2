using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace ffnotev2.Controls;

/// <summary>
/// 리스트 드래그 재정렬 시 항목 사이에 표시되는 가로 삽입선.
/// AdornedElement(ListBox) 좌표계 기준 Y 위치에 그린다.
/// </summary>
public class InsertionLineAdorner : Adorner
{
    private double _y;
    private readonly System.Windows.Media.Pen _pen;

    public InsertionLineAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _pen = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x99, 0xFF)), 2);
        _pen.Freeze();
    }

    /// <summary>삽입선 Y 좌표(ListBox 기준)를 갱신하고 다시 그린다.</summary>
    public void SetY(double y)
    {
        if (Math.Abs(_y - y) < 0.5) return;
        _y = y;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = ((FrameworkElement)AdornedElement).ActualWidth;
        drawingContext.DrawLine(_pen, new Point(0, _y), new Point(w, _y));
    }
}
