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
        // 첫 행에서 '|' 위치 모두 수집
        var (firstStart, firstEnd) = LineSpan(t, lineStarts, firstLi);
        int fEnd = firstEnd;
        if (fEnd > firstStart && t[fEnd - 1] == '\r') fEnd--;

        var pipeXs = new List<double>();
        for (int i = firstStart; i < fEnd; i++)
        {
            if (t[i] != '|') continue;
            Rect r;
            try { r = _editor.GetRectFromCharacterIndex(i); }
            catch { return; }
            if (double.IsInfinity(r.X) || double.IsNaN(r.X)) continue;
            pipeXs.Add(r.X);
        }
        if (pipeXs.Count < 2) return;

        // 표 영역 Y 범위
        Rect topRect;
        try { topRect = _editor.GetRectFromCharacterIndex(firstStart); }
        catch { return; }
        if (double.IsInfinity(topRect.Y) || double.IsNaN(topRect.Y)) return;
        double tableTop = topRect.Y;

        var (lastStart, lastEnd) = LineSpan(t, lineStarts, lastLi);
        int lEnd = lastEnd;
        if (lEnd > lastStart && t[lEnd - 1] == '\r') lEnd--;
        Rect bottomRect;
        try { bottomRect = _editor.GetRectFromCharacterIndex(Math.Max(lastStart, lEnd - 1)); }
        catch { return; }
        if (double.IsInfinity(bottomRect.Y) || double.IsNaN(bottomRect.Y)) return;
        double tableBottom = bottomRect.Y + bottomRect.Height;

        double leftX = pipeXs[0];
        double rightX = pipeXs[^1];

        // 세로선 — 첫 행의 '|' X로 표 시작 → 끝까지
        foreach (var x in pipeXs)
            dc.DrawLine(_pen, new Point(x, tableTop), new Point(x, tableBottom));

        // 가로선 — 각 행의 상단 + 마지막 행 하단
        for (int li = firstLi; li <= lastLi; li++)
        {
            var (s, _) = LineSpan(t, lineStarts, li);
            Rect r;
            try { r = _editor.GetRectFromCharacterIndex(s); }
            catch { continue; }
            if (double.IsInfinity(r.Y) || double.IsNaN(r.Y)) continue;
            dc.DrawLine(_pen, new Point(leftX, r.Y), new Point(rightX, r.Y));
        }
        // 마지막 행 하단
        dc.DrawLine(_pen, new Point(leftX, tableBottom), new Point(rightX, tableBottom));
    }
}
