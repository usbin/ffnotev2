using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace ffnotev2.Controls;

/// <summary>
/// 편집 모드 TextBox 위에 마크다운 표 셀 그리드(세로선·가로선)를 덧그린다.
/// 입력 자체에는 관여하지 않음(IsHitTestVisible=false). 한글 IME / undo / 캐럿 모두 무관.
/// 표 행은 양 끝이 '|'이고 가운데에 '|'가 1개 이상 있는 줄로 판정.
/// 각 '|'의 X 좌표는 TextBox.GetRectFromCharacterIndex로 정확히 얻어 monospace/가변폭 모두에서 동작.
/// </summary>
public class TableGridAdorner : Adorner
{
    private readonly TextBox _editor;
    private readonly Pen _pen;

    public TableGridAdorner(TextBox editor) : base(editor)
    {
        _editor = editor;
        IsHitTestVisible = false;
        var brush = new SolidColorBrush(Color.FromArgb(0x88, 0x77, 0x77, 0x77));
        brush.Freeze();
        _pen = new Pen(brush, 1);
        _pen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = _editor.Text;
        if (string.IsNullOrEmpty(t)) return;

        // AdornerLayer는 기본적으로 adorned 요소 경계로 클립하지 않는다. 표가 스크롤로
        // 가려졌거나 노트 영역보다 크면 GetRectFromCharacterIndex가 에디터 밖 좌표를
        // 반환해 그리드 선이 노트 바깥까지 삐져나온다. 에디터 가시 영역으로 클립.
        var clip = new RectangleGeometry(new Rect(new Point(0, 0), _editor.RenderSize));
        clip.Freeze();
        dc.PushClip(clip);
        try
        {
            RenderGrid(dc, t);
        }
        finally
        {
            dc.Pop();
        }
    }

    private void RenderGrid(DrawingContext dc, string t)
    {
        // 각 줄 시작 char index 캐시
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < t.Length; i++)
            if (t[i] == '\n') lineStarts.Add(i + 1);

        int lineCount = lineStarts.Count;
        int li = 0;
        while (li < lineCount)
        {
            var (lineStart, lineEnd) = LineSpan(t, lineStarts, li);
            if (IsTableRowLine(t, lineStart, lineEnd))
            {
                int firstLi = li;
                int lastLi = li;
                while (lastLi + 1 < lineCount)
                {
                    var (ns, ne) = LineSpan(t, lineStarts, lastLi + 1);
                    if (!IsTableRowLine(t, ns, ne)) break;
                    lastLi++;
                }
                DrawTable(dc, t, lineStarts, firstLi, lastLi);
                li = lastLi + 1;
            }
            else li++;
        }
    }

    private static (int Start, int EndExclusive) LineSpan(string t, List<int> lineStarts, int li)
    {
        int start = lineStarts[li];
        int end = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] - 1 : t.Length;
        return (start, end);
    }

    private static bool IsTableRowLine(string t, int start, int endExclusive)
    {
        // 양 끝이 '|', 가운데에도 '|' 1개 이상. '\r' 끝 처리.
        int e = endExclusive;
        if (e > start && t[e - 1] == '\r') e--;
        int len = e - start;
        if (len < 3) return false;
        if (t[start] != '|' || t[e - 1] != '|') return false;
        for (int i = start + 1; i < e - 1; i++)
            if (t[i] == '|') return true;
        return false;
    }

    private void DrawTable(DrawingContext dc, string t, List<int> lineStarts, int firstLi, int lastLi)
    {
        // 각 행을 독립 박스로 그림. 행마다 자기 '|' 위치를 사용해 글자와 어긋남 없음.
        // 인접 행은 Y가 이어지므로 시각적으로 표 형태로 합쳐져 보임.
        for (int li = firstLi; li <= lastLi; li++)
            DrawRowBox(dc, t, lineStarts, li);
    }

    private void DrawRowBox(DrawingContext dc, string t, List<int> lineStarts, int li)
    {
        var (s, eExcl) = LineSpan(t, lineStarts, li);
        int e = eExcl;
        if (e > s && t[e - 1] == '\r') e--;

        // 이 행의 '|' cell-center X 수집. monospace에서 '|' glyph는 cell 가운데에 그려지므로
        // r.X(좌측 경계)가 아니라 r.X + r.Width / 2로 글자 위에 정확히 겹치게 한다.
        var xs = new List<double>();
        Rect first = default;
        for (int i = s; i < e; i++)
        {
            if (t[i] != '|') continue;
            Rect r;
            try { r = _editor.GetRectFromCharacterIndex(i); }
            catch { continue; }
            if (double.IsInfinity(r.X) || double.IsNaN(r.X)) continue;
            if (xs.Count == 0) first = r;
            xs.Add(r.X + r.Width / 2.0);
        }
        if (xs.Count < 2) return;
        if (double.IsInfinity(first.Y) || double.IsNaN(first.Y)) return;

        double top = first.Y;
        double bottom = first.Y + first.Height;
        double left = xs[0];
        double right = xs[^1];

        // 상단·하단 가로선
        dc.DrawLine(_pen, new Point(left, top), new Point(right, top));
        dc.DrawLine(_pen, new Point(left, bottom), new Point(right, bottom));
        // 세로선 (각 '|' cell 중앙)
        foreach (var x in xs)
            dc.DrawLine(_pen, new Point(x, top), new Point(x, bottom));
    }
}
