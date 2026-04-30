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
        // 외부에서 IsEditing=true 설정 시 (방향키 이동 등) 이미 로드된 컨트롤도 편집 모드로 진입
        if (e.PropertyName == nameof(NoteItem.IsEditing)
            && Item is { IsEditing: true, Type: NoteType.Text })
        {
            Item.IsEditing = false;
            BeginEdit();
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
        foreach (var (it, _, _) in _dragGroup)
            App.MainVM.UpdateNotePosition(it);
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
            // ESC로 편집 종료 (LostFocus가 저장 처리)
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        // Alt+방향키: 인접 노트로 편집 이동 (Alt 눌리면 Key=System, 실제 키는 SystemKey)
        if (Item is not null && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            var actual = e.Key == Key.System ? e.SystemKey : e.Key;
            string? dir = actual switch
            {
                Key.Left => "Left",
                Key.Right => "Right",
                Key.Up => "Up",
                Key.Down => "Down",
                _ => null
            };
            if (dir is not null)
            {
                var next = App.MainVM.FindNeighborNote(Item, dir);
                if (next is not null)
                {
                    App.MainVM.SelectOnly(next);
                    // 현재 포커스 해제 → LostFocus가 Content 저장. 그 다음 다음 노트의 IsEditing=true로
                    // 트리거해 BeginEdit (DataContextChanged의 PropertyChanged 핸들러가 받음).
                    Keyboard.ClearFocus();
                    Dispatcher.BeginInvoke(new Action(() => next.IsEditing = true), DispatcherPriority.Loaded);
                }
                e.Handled = true;
            }
        }
    }

    private double _resizeAccumDx;
    private double _resizeAccumDy;

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (Item is null) return;
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? "Corner";
        var group = App.MainVM.SelectedNotes.ToList();
        if (!group.Contains(Item)) group = new List<NoteItem> { Item };
        _resizeGroup = group.Select(n => (n, n.X, n.Y, n.Width, n.Height)).ToList();
        _resizeAccumDx = 0;
        _resizeAccumDy = 0;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeGroup.Count == 0) return;
        _resizeAccumDx += e.HorizontalChange;
        _resizeAccumDy += e.VerticalChange;
        foreach (var (it, sx, sy, sw, sh) in _resizeGroup)
        {
            switch (_resizeEdge)
            {
                case "Right":
                    it.Width = Math.Max(80, App.MainVM.MaybeSnap(sw + _resizeAccumDx));
                    break;
                case "Bottom":
                    it.Height = Math.Max(40, App.MainVM.MaybeSnap(sh + _resizeAccumDy));
                    break;
                case "Left":
                {
                    var right = sx + sw;
                    var newX = App.MainVM.MaybeSnap(sx + _resizeAccumDx);
                    if (right - newX < 80) newX = right - 80;
                    it.X = newX;
                    it.Width = right - newX;
                    break;
                }
                case "Top":
                {
                    var bottom = sy + sh;
                    var newY = App.MainVM.MaybeSnap(sy + _resizeAccumDy);
                    if (bottom - newY < 40) newY = bottom - 40;
                    it.Y = newY;
                    it.Height = bottom - newY;
                    break;
                }
                default: // Corner
                    it.Width = Math.Max(80, App.MainVM.MaybeSnap(sw + _resizeAccumDx));
                    it.Height = Math.Max(40, App.MainVM.MaybeSnap(sh + _resizeAccumDy));
                    break;
            }
        }
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        foreach (var (it, _, _, _, _) in _resizeGroup)
            App.MainVM.UpdateNoteContent(it);
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
