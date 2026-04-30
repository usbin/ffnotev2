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
    // 본문 드래그 — 클릭 시작 시점에 이미 선택돼 있던 경우만 활성화 (첫 클릭으로의 선택 전환은 드래그 X)
    private bool _clickStartedSelected;
    // 본문 드래그 후보 상태: MouseDown 시 set → MouseMove에서 임계값 초과 시 실제 드래그로 승격
    private bool _bodyPotentialDrag;
    private Point _bodyDragStartScreen;
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

    private static bool IsInsideTextBox(object? source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d is not null)
        {
            if (d is TextBox) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

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

    // 본문 드래그 — 노트가 클릭 시작 시점에 이미 선택돼 있을 때만 동작.
    // 임계값(4px) 넘기 전에는 캡처/Handled 안 해서 더블클릭 편집 진입 + 하이퍼링크 클릭이 정상 작동.
    private void BodyDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        if (!_clickStartedSelected) return; // 첫 클릭으로 선택만 — 드래그 X
        if (e.ClickCount > 1) return; // 더블클릭은 자식 핸들러(TextDisplay_MouseLeftButtonDown)로 통과
        // 텍스트 편집 중이면 본문 클릭은 캐럿 이동 — 드래그 X
        if (Item.Type == NoteType.Text && TextEditor.Visibility == Visibility.Visible) return;

        var canvas = FindParentCanvas();
        if (canvas is null) return;
        _bodyPotentialDrag = true;
        _bodyDragStartScreen = e.GetPosition(canvas);
        // e.Handled = false 유지 — 자식의 MouseLeftButtonDown(예: TextBlock 더블클릭 누적, Hyperlink 활성화)도 정상 작동
    }

    private void BodyDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (_bodyPotentialDrag)
        {
            var canvas = FindParentCanvas();
            if (canvas is null) return;
            var pos = e.GetPosition(canvas);
            if (Math.Abs(pos.X - _bodyDragStartScreen.X) < 4 && Math.Abs(pos.Y - _bodyDragStartScreen.Y) < 4) return;

            // 임계값 초과 — 드래그로 승격. TitleBar_MouseLeftButtonDown과 동일한 setup.
            _bodyPotentialDrag = false;
            _isDragging = true;
            _dragStart = _bodyDragStartScreen;
            var group = App.MainVM.SelectedNotes.ToList();
            if (!group.Contains(Item!)) group = new List<NoteItem> { Item! };
            _dragGroup = group.Select(n => (n, n.X, n.Y)).ToList();
            ((UIElement)sender).CaptureMouse();
        }
        if (_isDragging) TitleBar_MouseMove(sender, e);
    }

    private void BodyDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_bodyPotentialDrag)
        {
            // 단순 클릭으로 끝 — 드래그 안 됨
            _bodyPotentialDrag = false;
            return;
        }
        if (_isDragging) TitleBar_MouseLeftButtonUp(sender, e);
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

        // 본문 드래그가 "이미 선택된 상태"에서만 트리거되도록 클릭 시작 시점의 선택 상태 보존.
        // 미선택 상태에서 첫 클릭은 SelectOnly에 의해 IsSelected=true가 되지만,
        // 이 클릭으로 곧바로 드래그가 시작되면 단순 선택 의도와 충돌함.
        _clickStartedSelected = Item.IsSelected;

        // 다른 노트의 TextBox에서 편집 중이라면 LostFocus를 트리거해 표시 모드로 복귀시킴.
        // TextBox 내부(글자 사이 클릭으로 캐럿 이동) 클릭은 제외해서 편집 흐름을 유지 —
        // 단순히 `e.OriginalSource is TextBox` 체크는 부족함. WPF TextBox의 OriginalSource는
        // 내부 visual(TextBoxView 등)이므로 visual tree를 거슬러 올라가 TextBox 부모를 찾아야 함.
        if (!IsInsideTextBox(e.OriginalSource) && Window.GetWindow(this) is MainWindow mw)
            mw.FocusCanvas();

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
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? "BottomRight";
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
        const double minW = 80, minH = 40;
        foreach (var (it, sx, sy, sw, sh) in _resizeGroup)
        {
            // 좌/상은 X/Y와 W/H 동시 변경(반대편 엣지 고정), 우/하는 W/H만
            bool left = _resizeEdge is "Left" or "TopLeft" or "BottomLeft";
            bool right = _resizeEdge is "Right" or "TopRight" or "BottomRight";
            bool top = _resizeEdge is "Top" or "TopLeft" or "TopRight";
            bool bottom = _resizeEdge is "Bottom" or "BottomLeft" or "BottomRight";

            if (right)
            {
                it.Width = Math.Max(minW, App.MainVM.MaybeSnap(sw + dx));
            }
            if (left)
            {
                var rightEdge = sx + sw;
                var newX = App.MainVM.MaybeSnap(sx + dx);
                if (rightEdge - newX < minW) newX = rightEdge - minW;
                it.X = newX;
                it.Width = rightEdge - newX;
            }
            if (bottom)
            {
                it.Height = Math.Max(minH, App.MainVM.MaybeSnap(sh + dy));
            }
            if (top)
            {
                var bottomEdge = sy + sh;
                var newY = App.MainVM.MaybeSnap(sy + dy);
                if (bottomEdge - newY < minH) newY = bottomEdge - minH;
                it.Y = newY;
                it.Height = bottomEdge - newY;
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
