using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ffnotev2.Dialogs;
using ffnotev2.Models;
using ffnotev2.Services;
using Point = System.Windows.Point;

namespace ffnotev2;

public partial class MainWindow : Window
{
    private readonly HotkeyService _hotkey = new();

    private const double SidebarWidth = 220;
    private const double MinZoom = 0.2;
    private const double MaxZoom = 4.0;
    private const double PanDragThreshold = 4;

    private bool _sidebarCollapsed;

    private MouseButton? _panButton;
    private Point _panStart;
    private double _panStartTx;
    private double _panStartTy;
    private bool _panMoved;

    private bool _marqueeActive;
    private Point _marqueeStart;

    // 우클릭 down 시점의 원본 visual을 저장해 up 시 컨텍스트 메뉴 대상 식별에 사용.
    // 캔버스가 마우스 캡처 후 MouseRightButtonUp의 OriginalSource는 캔버스로 바뀌므로
    // down 시점에 저장해야 노트/그룹을 정확히 식별 가능.
    private DependencyObject? _rightClickOrigin;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainVM;
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Title = ver is null ? "ffnote v2" : $"ffnote v2 — v{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey.Initialize(this);
        ReregisterHotkeys();

        // 자동 시작(트레이 전용)일 때는 다이얼로그가 갑자기 뜨지 않도록,
        // 메인 창이 처음 사용자에게 표시되는 시점(IsVisible=true)에 1회만 업데이트 체크.
        IsVisibleChanged += MainWindow_OnFirstVisible_CheckUpdate;
    }

    private bool _updateChecked;
    private void MainWindow_OnFirstVisible_CheckUpdate(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_updateChecked || !IsVisible) return;
        _updateChecked = true;
        IsVisibleChanged -= MainWindow_OnFirstVisible_CheckUpdate;
        _ = new UpdateService().CheckAndPromptAsync(this);
    }

    /// <summary>설정에 따라 모든 글로벌 핫키 등록. 일부 실패하면 false.</summary>
    public bool ReregisterHotkeys()
    {
        var app = (App)Application.Current;
        var settings = app.SettingsService.Settings;
        _hotkey.UnregisterAll();

        var ok1 = _hotkey.Register(settings.ShowMain.ModifiersFlags,
            settings.ShowMain.VirtualKey, () => app.ShowMain());
        var ok2 = _hotkey.Register(settings.ToggleOverlay.ModifiersFlags,
            settings.ToggleOverlay.VirtualKey, () => app.ToggleOverlay());
        var ok3 = _hotkey.Register(settings.ToggleClickThrough.ModifiersFlags,
            settings.ToggleClickThrough.VirtualKey, () => app.ToggleOverlayClickThrough());

        return ok1 && ok2 && ok3;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 트레이로 숨김 (앱 종료는 트레이 메뉴에서)
        e.Cancel = true;
        Hide();
    }

    /// <summary>편집 종료/비텍스트 노트 이동 후 키 이벤트가 Window_PreviewKeyDown까지 도달하도록
    /// 캔버스 영역(Focusable=True)으로 키보드 포커스를 옮긴다.</summary>
    public void FocusCanvas() => Keyboard.Focus(CanvasArea);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 편집 중인 TextBox에서의 Esc는 편집 종료가 우선 (TextEditor_PreviewKeyDown이 e.Handled 처리)
            if (e.OriginalSource is not TextBox)
            {
                App.MainVM.ClearSelection();
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Z 언두 / Ctrl+Y 또는 Ctrl+Shift+Z 리두 (편집 중인 TextBox는 자체 undo 사용)
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && e.OriginalSource is not TextBox)
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (e.Key == Key.Z && !shift)
            {
                App.Undo.Undo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y || (e.Key == Key.Z && shift))
            {
                App.Undo.Redo();
                e.Handled = true;
                return;
            }
        }

        // Ctrl+G: 선택 노트로 그룹 만들기. Ctrl+Shift+G: 선택된 그룹 해제(멤버 처리는 다이얼로그)
        if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && e.OriginalSource is not TextBox)
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (shift)
            {
                DeleteGroupsWithPrompt(App.MainVM.SelectedGroups.ToList());
            }
            else
            {
                App.MainVM.CreateGroupFromSelectedNotes();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // 편집 가능한 텍스트박스 안에서의 Ctrl+V는 기본 붙여넣기 살리기
            if (e.OriginalSource is TextBox tb && !tb.IsReadOnly) return;

            var (x, y) = GetWorldMousePosition();
            App.MainVM.PasteFromClipboard(x, y);
            e.Handled = true;
            return;
        }

        // Ctrl+C: 선택 노트를 시스템 클립보드에 복사 (다른 앱에서도 받을 수 있음)
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && e.OriginalSource is not TextBox)
        {
            App.MainVM.CopySelectedToClipboard();
            e.Handled = true;
            return;
        }

        // Delete: 비편집 + 선택 노트/그룹 삭제 (그룹은 멤버 처리 다이얼로그)
        if (e.Key == Key.Delete && e.OriginalSource is not TextBox)
        {
            var notes = App.MainVM.SelectedNotes.ToList();
            var groups = App.MainVM.SelectedGroups.ToList();
            if (notes.Count > 0 || groups.Count > 0)
            {
                DeleteGroupsWithPrompt(groups, notes);
                e.Handled = true;
                return;
            }
        }

        // Enter: 비편집 + 단일 선택 텍스트 노트 → 편집 모드 진입
        if (e.Key == Key.Enter && e.OriginalSource is not TextBox)
        {
            if (Keyboard.Modifiers == ModifierKeys.None && !IsInsideListBox(e.OriginalSource))
            {
                var sel = App.MainVM.SelectedNotes.ToList();
                if (sel.Count == 1 && sel[0].Type == Models.NoteType.Text)
                {
                    sel[0].IsEditing = true;
                    e.Handled = true;
                    return;
                }
            }
        }

        // 노트북 빠른 전환 (Ctrl+1 ~ Ctrl+0, 사용자 지정 가능)
        if (e.OriginalSource is not TextBox && TryHandleNotebookSwitch(e)) return;

        // Ctrl+화살표(1px), Ctrl+Shift+화살표(10px) — 비편집 + 선택 노트들 이동
        if (e.OriginalSource is not TextBox && TryHandleNoteNudge(e)) return;

        // 화살표 노트 이동:
        //   편집 중(TextBox 포커스): Alt+화살표 → 인접 노트로 편집 이동
        //   비편집: 화살표(Alt 무관) → 단일 선택 노트의 인접 노트로 선택 이동
        TryHandleArrowNavigation(e);
    }

    private void TryHandleArrowNavigation(KeyEventArgs e)
    {
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        string? dir = actualKey switch
        {
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ => null
        };
        if (dir is null) return;

        var inTextBox = e.OriginalSource is TextBox;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        Models.NoteItem? current = null;
        bool resumeEdit = false;

        if (inTextBox)
        {
            // 편집 중: Alt 없이는 일반 화살표(커서 이동) 양보
            if (!alt) return;
            current = FindNoteFromVisual(e.OriginalSource as DependencyObject);
            resumeEdit = true;
        }
        else
        {
            // 비편집: 사이드바/리스트박스에 포커스면 양보. 모디파이어는 무관(Alt+화살표도 작동)
            if (IsInsideListBox(e.OriginalSource)) return;
            // Ctrl/Shift는 다른 동작에 쓰일 수 있으니 양보
            var mods = Keyboard.Modifiers & ~ModifierKeys.Alt;
            if (mods != ModifierKeys.None) return;
            var sel = App.MainVM.SelectedNotes.ToList();
            if (sel.Count == 1) current = sel[0];
        }

        if (current is null) return;

        var next = App.MainVM.FindNeighborNote(current, dir);
        if (next is null)
        {
            // 인접 노트 없음 — 편집 중이면 화살표가 텍스트박스로 흐르지 않게 차단
            if (resumeEdit) e.Handled = true;
            return;
        }

        App.MainVM.SelectOnly(next);
        if (resumeEdit)
        {
            if (next.Type == Models.NoteType.Text)
            {
                // 다음도 텍스트면: 현재 포커스 해제 → LostFocus 저장 → 다음 노트 IsEditing 트리거 → BeginEdit
                Keyboard.ClearFocus();
                Dispatcher.BeginInvoke(new Action(() => next.IsEditing = true), DispatcherPriority.Loaded);
            }
            else
            {
                // 다음이 비텍스트(이미지/링크): 편집 종료, 캔버스에 포커스 → 이후 화살표가 다시 작동
                FocusCanvas();
            }
        }
        e.Handled = true;
    }

    private bool TryHandleNotebookSwitch(KeyEventArgs e)
    {
        if (IsInsideListBox(e.OriginalSource)) return false;
        var settings = ((App)Application.Current).SettingsService.Settings;
        var switches = settings.NotebookSwitches;
        if (switches is null) return false;
        for (int i = 0; i < switches.Length; i++)
        {
            if (switches[i].MatchesLocal(e))
            {
                if (i < App.MainVM.Notebooks.Count)
                {
                    App.MainVM.CurrentNotebook = App.MainVM.Notebooks[i];
                }
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    private bool TryHandleNoteNudge(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return false;
        // Alt 같이 눌리면 우리 케이스 아님
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) return false;

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        var (dxUnit, dyUnit) = actualKey switch
        {
            Key.Left  => (-1, 0),
            Key.Right => ( 1, 0),
            Key.Up    => ( 0, -1),
            Key.Down  => ( 0, 1),
            _ => (0, 0)
        };
        if (dxUnit == 0 && dyUnit == 0) return false;

        var selNotes = App.MainVM.SelectedNotes.ToList();
        var selGroups = App.MainVM.SelectedGroups.ToList();
        if (selNotes.Count == 0 && selGroups.Count == 0) return false;

        // 그룹의 멤버(노트·하위그룹)도 같이 이동. 중복 제거.
        var notesToMove = new HashSet<Models.NoteItem>(selNotes);
        var groupsToMove = new HashSet<Models.NoteGroup>(selGroups);
        foreach (var g in selGroups)
        {
            var (mNotes, mGroups) = App.MainVM.GetMembersOf(g);
            foreach (var n in mNotes) notesToMove.Add(n);
            foreach (var sg in mGroups) groupsToMove.Add(sg);
        }

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        double dx, dy;
        if (shift)
        {
            // 격자 정렬 이동: pivot 기준으로 격자에 맞도록 dx/dy 계산.
            // 모두 같은 Δ를 적용하여 다중 선택 시에도 상대 위치 유지.
            // 그룹 선택 시 첫 그룹을, 노트만 선택 시 첫 노트를 pivot으로 사용.
            const double G = ViewModels.MainViewModel.GridSize;
            double pivotX = selGroups.Count > 0 ? selGroups[0].X : selNotes[0].X;
            double pivotY = selGroups.Count > 0 ? selGroups[0].Y : selNotes[0].Y;
            dx = dxUnit != 0 ? (Math.Floor(pivotX / G) * G + dxUnit * G - pivotX) : 0;
            dy = dyUnit != 0 ? (Math.Floor(pivotY / G) * G + dyUnit * G - pivotY) : 0;
        }
        else
        {
            // 1px 정밀 이동
            dx = dxUnit;
            dy = dyUnit;
        }
        var changes = new List<(Services.ItemSnapshot Old, Services.ItemSnapshot New)>();
        foreach (var n in notesToMove)
        {
            var ox = n.X; var oy = n.Y;
            n.X += dx;
            n.Y += dy;
            App.MainVM.UpdateNotePosition(n);
            if (n.X != ox || n.Y != oy)
                changes.Add((new Services.ItemSnapshot(n, ox, oy, n.Width, n.Height),
                             new Services.ItemSnapshot(n, n.X, n.Y, n.Width, n.Height)));
        }
        foreach (var g in groupsToMove)
        {
            var ox = g.X; var oy = g.Y;
            g.X += dx;
            g.Y += dy;
            App.MainVM.UpdateGroupPosition(g);
            if (g.X != ox || g.Y != oy)
                changes.Add((new Services.ItemSnapshot(g, ox, oy, g.Width, g.Height),
                             new Services.ItemSnapshot(g, g.X, g.Y, g.Width, g.Height)));
        }
        if (changes.Count > 0) App.MainVM.RecordTransform(changes);
        e.Handled = true;
        return true;
    }

    private static Models.NoteItem? FindNoteFromVisual(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl dn && dn.DataContext is Models.NoteItem n) return n;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return null;
    }

    private static bool IsInsideListBox(object? source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d is not null)
        {
            if (d is ListBox or ListBoxItem) return true;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return false;
    }

    private (double X, double Y) GetWorldMousePosition()
    {
        var pos = Mouse.GetPosition(CanvasArea);
        return ScreenToWorld(pos);
    }

    private (double X, double Y) ScreenToWorld(Point p)
    {
        var sx = CanvasScale.ScaleX == 0 ? 1 : CanvasScale.ScaleX;
        var sy = CanvasScale.ScaleY == 0 ? 1 : CanvasScale.ScaleY;
        return ((p.X - CanvasTranslate.X) / sx, (p.Y - CanvasTranslate.Y) / sy);
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = _sidebarCollapsed ? new GridLength(0) : new GridLength(SidebarWidth);
        Sidebar.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarReopenButton.Visibility = _sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CanvasArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 좌클릭 빈 영역: 마키 선택 시작 (노트 위는 노트의 PreviewMouseLeftButtonDown이 먼저 처리)
        if (e.ChangedButton == MouseButton.Left && _panButton is null && !_marqueeActive
            && !IsOriginInsideNote(e.OriginalSource))
        {
            _marqueeActive = true;
            _marqueeStart = e.GetPosition(CanvasArea);
            CanvasArea.CaptureMouse();
            // 편집 중이던 TextBox에서 포커스를 캔버스로 옮겨 LostFocus(저장) 트리거
            // → 노트는 표시 모드로 복귀, 키 입력이 더 이상 그 노트로 가지 않음
            FocusCanvas();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
        if (_panButton is not null) return;

        // 우클릭은 항상 팬 후보로 진입(노트/그룹 위에서도). 이동 없이 떼었을 때만 메뉴.
        // 마우스 캡처로 노트의 MouseRightButtonUp이 막혀도, _rightClickOrigin에 저장한
        // 원본 visual로 메뉴 대상(노트/그룹)을 식별한다.
        if (e.ChangedButton == MouseButton.Right)
            _rightClickOrigin = e.OriginalSource as DependencyObject;

        _panButton = e.ChangedButton;
        _panStart = e.GetPosition(CanvasArea);
        _panStartTx = CanvasTranslate.X;
        _panStartTy = CanvasTranslate.Y;
        _panMoved = false;
        CanvasArea.CaptureMouse();

        // 휠 클릭은 즉시 팬 모드 (다른 용도 없음)
        // 우클릭은 임계값 초과 시점부터 팬 시작 → 짧은 클릭은 컨텍스트 메뉴로 유지
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panMoved = true;
            CanvasArea.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void CanvasArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_marqueeActive)
        {
            var p = e.GetPosition(CanvasArea);
            var x = Math.Min(p.X, _marqueeStart.X);
            var y = Math.Min(p.Y, _marqueeStart.Y);
            var w = Math.Abs(p.X - _marqueeStart.X);
            var h = Math.Abs(p.Y - _marqueeStart.Y);
            MarqueeRect.Margin = new Thickness(x, y, 0, 0);
            MarqueeRect.Width = w;
            MarqueeRect.Height = h;
            MarqueeRect.Visibility = Visibility.Visible;
            return;
        }

        if (_panButton is null) return;
        var pos = e.GetPosition(CanvasArea);
        var dx = pos.X - _panStart.X;
        var dy = pos.Y - _panStart.Y;

        if (!_panMoved)
        {
            if (Math.Abs(dx) <= PanDragThreshold && Math.Abs(dy) <= PanDragThreshold) return;
            _panMoved = true;
            CanvasArea.Cursor = Cursors.SizeAll;
        }

        CanvasTranslate.X = _panStartTx + dx;
        CanvasTranslate.Y = _panStartTy + dy;
    }

    private void CanvasArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _marqueeActive)
        {
            EndMarquee(e.GetPosition(CanvasArea));
            e.Handled = true;
            return;
        }

        // 휠 클릭만 여기서 종료. 우클릭은 MouseRightButtonUp에서 처리
        if (_panButton != MouseButton.Middle || e.ChangedButton != MouseButton.Middle) return;
        EndPan();
        e.Handled = true;
    }

    private void EndMarquee(Point endPoint)
    {
        var dx = endPoint.X - _marqueeStart.X;
        var dy = endPoint.Y - _marqueeStart.Y;
        if (Math.Abs(dx) < PanDragThreshold && Math.Abs(dy) < PanDragThreshold)
        {
            // 단순 클릭: 선택 해제
            App.MainVM.ClearSelection();
        }
        else if (App.MainVM.CurrentNotebook is { } nb)
        {
            // world 좌표로 변환 후 인터섹트되는 노트/그룹 모두 선택
            var (wx1, wy1) = ScreenToWorld(_marqueeStart);
            var (wx2, wy2) = ScreenToWorld(endPoint);
            var minX = Math.Min(wx1, wx2);
            var maxX = Math.Max(wx1, wx2);
            var minY = Math.Min(wy1, wy2);
            var maxY = Math.Max(wy1, wy2);
            bool Hit(double x, double y, double w, double h) =>
                x + w >= minX && x <= maxX && y + h >= minY && y <= maxY;
            foreach (var n in nb.Notes) n.IsSelected = Hit(n.X, n.Y, n.Width, n.Height);
            foreach (var g in nb.Groups) g.IsSelected = Hit(g.X, g.Y, g.Width, g.Height);
        }
        MarqueeRect.Visibility = Visibility.Collapsed;
        _marqueeActive = false;
        if (CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
    }

    // 노트뿐 아니라 그룹(헤더/리사이즈 핸들)에 들어간 클릭도 마키 차단 대상.
    // CanvasArea가 generic MouseDown으로 좌클릭을 처리하므로, 그룹의 specific MouseLeftButtonDown
    // 핸들러가 e.Handled를 세팅해도 마키 시작을 막을 수 없어서 여기서 시각 트리 검사로 차단.
    private static bool IsOriginInsideNote(object? originalSource)
    {
        DependencyObject? d = originalSource as DependencyObject;
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl) return true;
            if (d is Controls.GroupBoxControl) return true;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return false;
    }

    private void EndPan()
    {
        _panButton = null;
        _panMoved = false;
        if (CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
        CanvasArea.Cursor = Cursors.Arrow;
    }

    private void CanvasArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // 컨텍스트 메뉴 등 다른 요소가 캡처를 가져가면 안전하게 팬/마키 상태 해제
        if (_panButton is not null) EndPan();
        if (_marqueeActive)
        {
            MarqueeRect.Visibility = Visibility.Collapsed;
            _marqueeActive = false;
        }
    }

    private void CanvasArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var rmb = e.RightButton == MouseButtonState.Pressed;
        if (!ctrl && !rmb) return;

        // 우클릭 누른 채 휠로 줌하면, 우클릭 떼는 순간 컨텍스트 메뉴 뜨지 않도록
        // pan 'moved' 플래그를 set해서 떼기 시점에 메뉴 억제
        if (rmb && _panButton == MouseButton.Right) _panMoved = true;

        var mouse = e.GetPosition(CanvasArea);
        var (worldX, worldY) = ScreenToWorld(mouse);

        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Math.Clamp(CanvasScale.ScaleX * factor, MinZoom, MaxZoom);

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;

        // 마우스 지점 앵커 유지: 같은 world 좌표가 동일 화면 위치에 오도록 평행이동 보정
        CanvasTranslate.X = mouse.X - worldX * newScale;
        CanvasTranslate.Y = mouse.Y - worldY * newScale;

        // 줌 후 팬 앵커 갱신: 줌으로 바뀐 Translate를 기준점으로 재설정해
        // 이어서 드래그할 때 위치가 튀지 않도록 함
        if (_panButton is not null)
        {
            _panStart = mouse;
            _panStartTx = CanvasTranslate.X;
            _panStartTy = CanvasTranslate.Y;
        }

        ZoomLabel.Text = $"{Math.Round(newScale * 100)}%";
        e.Handled = true;
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        CanvasScale.ScaleX = 1;
        CanvasScale.ScaleY = 1;
        CanvasTranslate.X = 0;
        CanvasTranslate.Y = 0;
        ZoomLabel.Text = "100%";
    }

    private void BulkSnap_Click(object sender, RoutedEventArgs e)
    {
        App.MainVM.BulkSnap();
    }

    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 우클릭 팬 종료 처리 (이 이벤트가 MouseUp보다 먼저 올 수 있어서 여기서 직접 처리)
        if (_panButton == MouseButton.Right)
        {
            var wasPan = _panMoved;
            EndPan();
            if (wasPan)
            {
                _rightClickOrigin = null;
                e.Handled = true;
                return;
            }
        }

        if (App.MainVM.CurrentNotebook is null) return;
        if (sender is not IInputElement el) return;

        // 우클릭 대상 식별 — 캔버스 캡처로 e.OriginalSource는 캔버스로 바뀔 수 있어
        // down 시점에 저장한 원본 visual(_rightClickOrigin)을 우선 사용
        var origin = _rightClickOrigin ?? e.OriginalSource as DependencyObject;
        _rightClickOrigin = null;
        var targetNote = FindNoteFromVisual(origin);
        var targetGroup = FindGroupFromVisual(origin);

        var screen = e.GetPosition(el);
        var (worldX, worldY) = ScreenToWorld(screen);

        var menu = new ContextMenu();

        var addText = new MenuItem { Header = "새 텍스트 노트" };
        addText.Click += (_, _) => App.MainVM.AddTextNote(string.Empty, worldX, worldY);
        menu.Items.Add(addText);

        var selectedCount = App.MainVM.SelectedNotes.Count();
        var selectedGroups = App.MainVM.SelectedGroups.ToList();

        if (selectedCount >= 1)
        {
            menu.Items.Add(new Separator());
            var groupItem = new MenuItem { Header = $"그룹 만들기 ({selectedCount}개) — Ctrl+G" };
            groupItem.Click += (_, _) => App.MainVM.CreateGroupFromSelectedNotes();
            menu.Items.Add(groupItem);
        }
        if (selectedGroups.Count >= 1)
        {
            if (selectedCount < 1) menu.Items.Add(new Separator());
            var ungroup = new MenuItem { Header = $"그룹 해제 ({selectedGroups.Count}개) — Ctrl+Shift+G" };
            ungroup.Click += (_, _) => DeleteGroupsWithPrompt(selectedGroups);
            menu.Items.Add(ungroup);
        }

        // 우클릭 대상별 추가 항목
        if (targetNote is not null)
        {
            menu.Items.Add(new Separator());
            var del = new MenuItem { Header = "삭제하기 (Delete)" };
            del.Click += (_, _) => App.MainVM.DeleteNote(targetNote);
            menu.Items.Add(del);
        }
        else if (targetGroup is not null && !selectedGroups.Contains(targetGroup))
        {
            // 위에서 처리되지 않은 그룹을 직접 우클릭한 경우
            menu.Items.Add(new Separator());
            var ungroupOne = new MenuItem { Header = "그룹 해제 (Ctrl+Shift+G)" };
            ungroupOne.Click += (_, _) => DeleteGroupsWithPrompt(new List<Models.NoteGroup> { targetGroup });
            menu.Items.Add(ungroupOne);
        }

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static Models.NoteGroup? FindGroupFromVisual(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Controls.GroupBoxControl gc && gc.DataContext is Models.NoteGroup g) return g;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return null;
    }

    /// <summary>
    /// 그룹 삭제 + 선택 노트 삭제 통합. 그룹에 멤버 노트가 (extraNotes 외에 추가로) 있으면
    /// "일괄 삭제 / 그룹만 삭제 / 취소" 다이얼로그로 확인. 멤버가 없거나 모두 extraNotes에
    /// 포함되면 다이얼로그 없이 즉시 삭제.
    /// </summary>
    /// <param name="groups">삭제할 그룹들</param>
    /// <param name="extraNotes">함께 삭제할 노트들 (이미 명시적으로 선택된 것). 그룹 멤버와 중복은 자동 제거.</param>
    private void DeleteGroupsWithPrompt(IList<Models.NoteGroup> groups, IList<Models.NoteItem>? extraNotes = null)
    {
        extraNotes ??= Array.Empty<Models.NoteItem>();
        if (groups.Count == 0 && extraNotes.Count == 0) return;

        var memberNotes = App.MainVM.GetMemberNotesOf(groups)
            .Except(extraNotes)
            .ToList();

        bool deleteMembers = false;
        if (groups.Count > 0 && memberNotes.Count > 0)
        {
            var dlg = new Dialogs.GroupDeleteDialog(memberNotes.Count) { Owner = this };
            dlg.ShowDialog();
            switch (dlg.Choice)
            {
                case Dialogs.GroupDeleteChoice.Cancel: return;
                case Dialogs.GroupDeleteChoice.DeleteAll: deleteMembers = true; break;
                case Dialogs.GroupDeleteChoice.GroupOnly: deleteMembers = false; break;
            }
        }

        foreach (var n in extraNotes) App.MainVM.DeleteNote(n);
        if (deleteMembers)
            foreach (var n in memberNotes) App.MainVM.DeleteNote(n);
        foreach (var g in groups) App.MainVM.DeleteGroup(g);
    }

    private void FontSettings_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var dlg = new FontSettingsDialog(app.SettingsService.Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            app.SettingsService.Settings.NoteFontFamily = dlg.ResultFontFamily;
            app.SettingsService.Settings.NoteFontSize = dlg.ResultFontSize;
            app.SettingsService.Save();  // SettingsChanged 이벤트 발생 → 모든 노트가 RefreshDocument
        }
    }

    private void NotebookMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not NoteBook nb) return;

        var menu = new ContextMenu();

        // 현재 연동 상태 표시 (비활성 헤더)
        var currentLabel = string.IsNullOrEmpty(nb.ProcessName)
            ? "현재 연동: (없음)"
            : $"현재 연동: {nb.ProcessName}";
        menu.Items.Add(new MenuItem { Header = currentLabel, IsEnabled = false });
        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "이름 변경" };
        rename.Click += (_, _) =>
        {
            var dlg = new RenameDialog(nb.Name) { Owner = this };
            if (dlg.ShowDialog() == true)
                App.MainVM.RenameNotebook(nb, dlg.EnteredName);
        };
        var setProcessHeader = string.IsNullOrEmpty(nb.ProcessName)
            ? "게임 프로세스 연동..."
            : "다른 프로세스로 변경...";
        var setProcess = new MenuItem { Header = setProcessHeader };
        setProcess.Click += (_, _) =>
        {
            var detection = ((App)Application.Current).GameDetectionService;
            var dlg = new GamePickerDialog(detection) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedProcessName is not null)
                App.MainVM.SetNotebookProcess(nb, dlg.SelectedProcessName);
        };
        var clearProcess = new MenuItem
        {
            Header = "프로세스 연동 해제",
            IsEnabled = !string.IsNullOrEmpty(nb.ProcessName)
        };
        clearProcess.Click += (_, _) => App.MainVM.SetNotebookProcess(nb, null);
        var del = new MenuItem { Header = "삭제" };
        del.Click += (_, _) =>
        {
            var ok = MessageBox.Show($"'{nb.Name}'을(를) 삭제할까요?", "확인",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok == MessageBoxResult.OK)
                App.MainVM.DeleteNotebook(nb);
        };
        menu.Items.Add(rename);
        menu.Items.Add(setProcess);
        menu.Items.Add(clearProcess);
        menu.Items.Add(new Separator());
        menu.Items.Add(del);
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _hotkey.Dispose();
    }
}
