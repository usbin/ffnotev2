using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ffnotev2.Models;
using Point = System.Windows.Point;

namespace ffnotev2.Controls;

public partial class GroupBoxControl : UserControl
{
    private bool _isDragging;
    private Point _dragStart;
    // 그룹 자신 + 멤버 노트 + 멤버 그룹의 시작 좌표 (Δ를 같이 적용)
    private List<(NoteGroup Group, double StartX, double StartY)> _groupSnapshot = new();
    private List<(NoteItem Item, double StartX, double StartY)> _noteSnapshot = new();

    public GroupBoxControl()
    {
        InitializeComponent();
    }

    private NoteGroup? Group => DataContext as NoteGroup;

    private Canvas? FindParentCanvas()
    {
        DependencyObject? cur = this;
        while (cur is not null)
        {
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            if (cur is Canvas canvas) return canvas;
        }
        return null;
    }

    private void GroupBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        // Shift+클릭: 토글, 드래그 시작 안 함
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            Group.IsSelected = !Group.IsSelected;
            e.Handled = true;
            return;
        }

        // 미선택 상태에서 일반 클릭: 다른 선택 모두 해제 후 이 그룹만
        if (!Group.IsSelected)
            App.MainVM.SelectOnlyGroup(Group);

        _isDragging = true;
        _dragStart = e.GetPosition(canvas);

        // 멤버 스냅샷 (드래그 시작 시점 기준 bbox 내포)
        var (notes, groups) = App.MainVM.GetMembersOf(Group);
        _groupSnapshot = new List<(NoteGroup, double, double)> { (Group, Group.X, Group.Y) };
        foreach (var sub in groups) _groupSnapshot.Add((sub, sub.X, sub.Y));
        _noteSnapshot = notes.Select(n => (n, n.X, n.Y)).ToList();

        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void GroupBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        var pos = e.GetPosition(canvas);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        // 그룹 자체는 스냅 적용 X (그룹 bbox는 자유). 단 멤버는 스냅 적용
        // 그러나 일관성을 위해 그룹도 MaybeSnap. 노트북 토글 ON일 때만 격자 정렬.
        foreach (var (g, sx, sy) in _groupSnapshot)
        {
            g.X = App.MainVM.MaybeSnap(sx + dx);
            g.Y = App.MainVM.MaybeSnap(sy + dy);
        }
        foreach (var (n, sx, sy) in _noteSnapshot)
        {
            n.X = App.MainVM.MaybeSnap(sx + dx);
            n.Y = App.MainVM.MaybeSnap(sy + dy);
        }
    }

    private void GroupBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();

        // 변경된 항목만 Undo 스택에 등록
        var changes = new List<(Services.ItemSnapshot Old, Services.ItemSnapshot New)>();
        foreach (var (g, sx, sy) in _groupSnapshot)
        {
            if (g.X != sx || g.Y != sy)
                changes.Add((new Services.ItemSnapshot(g, sx, sy, g.Width, g.Height),
                             new Services.ItemSnapshot(g, g.X, g.Y, g.Width, g.Height)));
        }
        foreach (var (n, sx, sy) in _noteSnapshot)
        {
            if (n.X != sx || n.Y != sy)
                changes.Add((new Services.ItemSnapshot(n, sx, sy, n.Width, n.Height),
                             new Services.ItemSnapshot(n, n.X, n.Y, n.Width, n.Height)));
        }

        // DB 저장
        foreach (var (g, _, _) in _groupSnapshot) App.MainVM.UpdateGroupPosition(g);
        foreach (var (n, _, _) in _noteSnapshot) App.MainVM.UpdateNotePosition(n);

        if (changes.Count > 0) App.MainVM.RecordTransform(changes);

        _groupSnapshot.Clear();
        _noteSnapshot.Clear();
    }

    // 리사이즈: 그룹 자체 bbox만 변경. 멤버는 이동시키지 않음 (멤버십은 동적 — 새 bbox로 재판정)
    private string _resizeEdge = "Corner";
    private (double X, double Y, double W, double H) _resizeStart;
    private Point _resizeMouseStart;

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (Group is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? "BottomRight";
        _resizeStart = (Group.X, Group.Y, Group.Width, Group.Height);
        _resizeMouseStart = Mouse.GetPosition(canvas);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Group is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        var mouseNow = Mouse.GetPosition(canvas);
        var dx = mouseNow.X - _resizeMouseStart.X;
        var dy = mouseNow.Y - _resizeMouseStart.Y;
        const double minW = 60, minH = 40;

        bool left = _resizeEdge is "Left" or "TopLeft" or "BottomLeft";
        bool right = _resizeEdge is "Right" or "TopRight" or "BottomRight";
        bool top = _resizeEdge is "Top" or "TopLeft" or "TopRight";
        bool bottom = _resizeEdge is "Bottom" or "BottomLeft" or "BottomRight";

        if (right)
        {
            Group.Width = Math.Max(minW, App.MainVM.MaybeSnap(_resizeStart.W + dx));
        }
        if (left)
        {
            var rightEdge = _resizeStart.X + _resizeStart.W;
            var newX = App.MainVM.MaybeSnap(_resizeStart.X + dx);
            if (rightEdge - newX < minW) newX = rightEdge - minW;
            Group.X = newX;
            Group.Width = rightEdge - newX;
        }
        if (bottom)
        {
            Group.Height = Math.Max(minH, App.MainVM.MaybeSnap(_resizeStart.H + dy));
        }
        if (top)
        {
            var bottomEdge = _resizeStart.Y + _resizeStart.H;
            var newY = App.MainVM.MaybeSnap(_resizeStart.Y + dy);
            if (bottomEdge - newY < minH) newY = bottomEdge - minH;
            Group.Y = newY;
            Group.Height = bottomEdge - newY;
        }
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (Group is null) return;
        if (Group.X == _resizeStart.X && Group.Y == _resizeStart.Y
            && Group.Width == _resizeStart.W && Group.Height == _resizeStart.H) return;
        var changes = new List<(Services.ItemSnapshot Old, Services.ItemSnapshot New)>
        {
            (new Services.ItemSnapshot(Group, _resizeStart.X, _resizeStart.Y, _resizeStart.W, _resizeStart.H),
             new Services.ItemSnapshot(Group, Group.X, Group.Y, Group.Width, Group.Height))
        };
        App.MainVM.UpdateGroupPosition(Group);
        App.MainVM.RecordTransform(changes);
    }
}
