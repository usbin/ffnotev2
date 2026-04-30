using System.Windows;
using System.Windows.Controls;
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

    private void GroupBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        var menu = new ContextMenu();
        var ungroup = new MenuItem { Header = "그룹 해제 (Ctrl+Shift+G)" };
        ungroup.Click += (_, _) => App.MainVM.DeleteGroup(Group);
        menu.Items.Add(ungroup);
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
