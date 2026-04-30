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

        // 스냅샷: Group을 leader(index 0)로 고정 후, 모든 선택 그룹+멤버하위그룹 수집.
        // 선택된 자유 노트(그룹 멤버 아닌 것)도 포함해 다중 선택 일괄 이동 지원.
        _groupSnapshot = new List<(NoteGroup, double, double)> { (Group, Group.X, Group.Y) };
        var seenG = new HashSet<NoteGroup> { Group };
        var allMemberNotes = new HashSet<NoteItem>();

        foreach (var g in App.MainVM.SelectedGroups)
        {
            if (!seenG.Add(g)) continue;
            _groupSnapshot.Add((g, g.X, g.Y));
        }
        // 모든 선택 그룹의 멤버 수집 (seenG 기준 중복 제거)
        foreach (var (g, _, _) in _groupSnapshot.ToList())
        {
            var (mNotes, mGroups) = App.MainVM.GetMembersOf(g);
            foreach (var sub in mGroups) if (seenG.Add(sub)) _groupSnapshot.Add((sub, sub.X, sub.Y));
            foreach (var n in mNotes) allMemberNotes.Add(n);
        }

        // 노트 스냅샷: 멤버 노트 + 선택된 자유 노트 (그룹 멤버 아닌 것)
        _noteSnapshot = allMemberNotes.Select(n => (n, n.X, n.Y)).ToList();
        foreach (var n in App.MainVM.SelectedNotes)
            if (!allMemberNotes.Contains(n))
                _noteSnapshot.Add((n, n.X, n.Y));

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

        // 격자 스냅 ON에서 그룹과 멤버를 독립 스냅하면 시작 위치가 비정렬일 때 각자
        // 다른 격자로 라운딩돼 상대 거리가 한 칸씩 어긋남(멤버가 한 칸씩 튀는 버그).
        // 그룹 자체를 leader로 잡아 snap된 실제 delta를 산출하고, 같은 delta를 그룹·멤버
        // 전체에 적용해 상대 위치를 보존.
        var leader = _groupSnapshot[0]; // 자기 자신
        var newLeaderX = App.MainVM.MaybeSnap(leader.StartX + dx);
        var newLeaderY = App.MainVM.MaybeSnap(leader.StartY + dy);
        var actualDx = newLeaderX - leader.StartX;
        var actualDy = newLeaderY - leader.StartY;

        foreach (var (g, sx, sy) in _groupSnapshot)
        {
            g.X = sx + actualDx;
            g.Y = sy + actualDy;
        }
        foreach (var (n, sx, sy) in _noteSnapshot)
        {
            n.X = sx + actualDx;
            n.Y = sy + actualDy;
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
