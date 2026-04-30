using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using ffnotev2.Models;
using Point = System.Windows.Point;

namespace ffnotev2.Controls;

public partial class DraggableNoteControl : UserControl
{
    private bool _isDragging;
    private Point _dragStart;
    // 일괄 드래그를 위해 선택된 모든 노트의 시작 좌표 캡처
    private List<(NoteItem Item, double StartX, double StartY)> _dragGroup = new();
    // 일괄 리사이즈를 위해 선택된 모든 노트의 시작 X/Y/W/H 캡처 (좌/상 엣지가 X/Y도 변경)
    private List<(NoteItem Item, double StartX, double StartY, double StartW, double StartH)> _resizeGroup = new();
    private string _resizeEdge = "Corner";

    public DraggableNoteControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NoteItem old) old.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewValue is NoteItem fresh) fresh.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 외부에서 IsEditing=true 설정 시 (방향키 이동, Enter 등) 이미 로드된 컨트롤도 편집 모드로 진입
        // 텍스트 외 타입은 BeginEdit를 무시하지만 플래그는 항상 리셋해 stuck 방지
        if (e.PropertyName == nameof(NoteItem.IsEditing) && Item?.IsEditing == true)
        {
            Item.IsEditing = false;
            if (Item.Type == NoteType.Text) BeginEdit();
        }
    }

    private NoteItem? Item => DataContext as NoteItem;

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        _isDragging = true;
        _dragStart = e.GetPosition(canvas);

        // 선택된 노트가 있으면 모두 함께 이동, 없거나 이 노트가 선택 안 됐으면 이 노트만
        var group = App.MainVM.SelectedNotes.ToList();
        if (!group.Contains(Item)) group = new List<NoteItem> { Item };
        _dragGroup = group.Select(n => (n, n.X, n.Y)).ToList();

        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        var pos = e.GetPosition(canvas);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        foreach (var (it, sx, sy) in _dragGroup)
        {
            it.X = App.MainVM.MaybeSnap(sx + dx);
            it.Y = App.MainVM.MaybeSnap(sy + dy);
        }
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        // 변경된 항목만 Undo 스택에 등록 (실제 위치가 바뀐 경우만)
        var changes = _dragGroup
            .Where(g => g.Item.X != g.StartX || g.Item.Y != g.StartY)
            .Select(g => (
                Old: new Services.ItemSnapshot(g.Item, g.StartX, g.StartY, g.Item.Width, g.Item.Height),
                New: new Services.ItemSnapshot(g.Item, g.Item.X, g.Item.Y, g.Item.Width, g.Item.Height)))
            .ToList();
        foreach (var (it, _, _) in _dragGroup)
            App.MainVM.UpdateNotePosition(it);
        if (changes.Count > 0) App.MainVM.RecordTransform(changes);
        _dragGroup.Clear();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        App.MainVM.DeleteNote(Item);
    }

    private void TextDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        BeginEdit();
        e.Handled = true;
    }

    private void BeginEdit()
    {
        if (Item is null || Item.Type != NoteType.Text) return;
        // 표시 모드 → 편집 모드로 스왑 (IsReadOnly 토글 회피로 한글 IME 정상 동작)
        TextDisplayScroll.Visibility = Visibility.Collapsed;
        TextEditor.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Keyboard.Focus(TextEditor);
            TextEditor.CaretIndex = TextEditor.Text.Length;
        }), DispatcherPriority.Input);
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // 새로 생성된 노트는 IsEditing=true로 만들어져서 자동으로 편집 모드로 진입
        if (Item is { IsEditing: true, Type: NoteType.Text })
        {
            Item.IsEditing = false;
            BeginEdit();
        }
    }

    private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (shift)
        {
            // Shift+클릭: 토글, 드래그/편집 차단
            Item.IsSelected = !Item.IsSelected;
            e.Handled = true;
            return;
        }
        // 미선택 상태에서 일반 클릭: 다른 선택 모두 해제 후 이 노트만 선택
        // 이미 선택된 경우는 그대로 두고 그룹 드래그가 진행되도록 함
        if (!Item.IsSelected)
            App.MainVM.SelectOnly(Item);
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        TextEditor.Visibility = Visibility.Collapsed;
        TextDisplayScroll.Visibility = Visibility.Visible;
        if (Item is not null)
            App.MainVM.UpdateNoteContent(Item);
    }

    private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // ESC로 편집 종료. CanvasArea(Focusable=True)에 포커스를 옮겨 LostFocus 트리거 +
            // 이후 화살표가 Window_PreviewKeyDown에 도달하도록 함
            if (Window.GetWindow(this) is MainWindow mw) mw.FocusCanvas();
            e.Handled = true;
        }
        // Alt+방향키 노트 이동은 Window_PreviewKeyDown에서 통합 처리
    }

    // 리사이즈 시작 시점의 마우스 절대 위치(부모 캔버스 좌표계). thumb 자체가 리사이즈로
    // 같이 움직일 때 thumb-local 기준 누적 델타가 양자화된 스냅과 만나 진동(점프 백)을
    // 유발하므로, 캔버스 기준의 안정적인 좌표를 직접 비교한다.
    private Point _resizeMouseStart;

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? "Corner";
        var group = App.MainVM.SelectedNotes.ToList();
        if (!group.Contains(Item)) group = new List<NoteItem> { Item };
        _resizeGroup = group.Select(n => (n, n.X, n.Y, n.Width, n.Height)).ToList();
        _resizeMouseStart = Mouse.GetPosition(canvas);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeGroup.Count == 0) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        var mouseNow = Mouse.GetPosition(canvas);
        var dx = mouseNow.X - _resizeMouseStart.X;
        var dy = mouseNow.Y - _resizeMouseStart.Y;
        foreach (var (it, sx, sy, sw, sh) in _resizeGroup)
        {
            switch (_resizeEdge)
            {
                case "Right":
                    it.Width = Math.Max(80, App.MainVM.MaybeSnap(sw + dx));
                    break;
                case "Bottom":
                    it.Height = Math.Max(40, App.MainVM.MaybeSnap(sh + dy));
                    break;
                case "Left":
                {
                    var right = sx + sw;
                    var newX = App.MainVM.MaybeSnap(sx + dx);
                    if (right - newX < 80) newX = right - 80;
                    it.X = newX;
                    it.Width = right - newX;
                    break;
                }
                case "Top":
                {
                    var bottom = sy + sh;
                    var newY = App.MainVM.MaybeSnap(sy + dy);
                    if (bottom - newY < 40) newY = bottom - 40;
                    it.Y = newY;
                    it.Height = bottom - newY;
                    break;
                }
                default: // Corner
                    it.Width = Math.Max(80, App.MainVM.MaybeSnap(sw + dx));
                    it.Height = Math.Max(40, App.MainVM.MaybeSnap(sh + dy));
                    break;
            }
        }
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var changes = _resizeGroup
            .Where(t => t.Item.X != t.StartX || t.Item.Y != t.StartY
                     || t.Item.Width != t.StartW || t.Item.Height != t.StartH)
            .Select(t => (
                Old: new Services.ItemSnapshot(t.Item, t.StartX, t.StartY, t.StartW, t.StartH),
                New: new Services.ItemSnapshot(t.Item, t.Item.X, t.Item.Y, t.Item.Width, t.Item.Height)))
            .ToList();
        foreach (var (it, _, _, _, _) in _resizeGroup)
            App.MainVM.UpdateNoteContent(it);
        if (changes.Count > 0) App.MainVM.RecordTransform(changes);
        _resizeGroup.Clear();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // 잘못된 URL은 조용히 무시
        }
        e.Handled = true;
    }
}
