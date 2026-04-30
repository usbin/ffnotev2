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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainVM;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey.Initialize(this);
        ReregisterHotkeys();
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

        // Ctrl+G: 선택 노트로 그룹 만들기. Ctrl+Shift+G: 선택된 그룹 해제
        if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && e.OriginalSource is not TextBox)
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (shift)
            {
                foreach (var g in App.MainVM.SelectedGroups.ToList())
                    App.MainVM.DeleteGroup(g);
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

        var sel = App.MainVM.SelectedNotes.ToList();
        if (sel.Count == 0) return false;

        var step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 1;
        var dx = dxUnit * step;
        var dy = dyUnit * step;
        foreach (var n in sel)
        {
            n.X += dx;
            n.Y += dy;
            App.MainVM.UpdateNotePosition(n);
        }
        e.Handled = true;
        return true;
    }

    private static Models.NoteItem? FindNoteFromVisual(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl dn && dn.DataContext is Models.NoteItem n) return n;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static bool IsInsideListBox(object? source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d is not null)
        {
            if (d is ListBox or ListBoxItem) return true;
            d = VisualTreeHelper.GetParent(d);
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
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
        if (_panButton is not null) return;

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

    private static bool IsOriginInsideNote(object? originalSource)
    {
        DependencyObject? d = originalSource as DependencyObject;
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl) return true;
            d = VisualTreeHelper.GetParent(d);
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
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

        var mouse = e.GetPosition(CanvasArea);
        var (worldX, worldY) = ScreenToWorld(mouse);

        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Math.Clamp(CanvasScale.ScaleX * factor, MinZoom, MaxZoom);

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;

        // 마우스 지점 앵커 유지: 같은 world 좌표가 동일 화면 위치에 오도록 평행이동 보정
        CanvasTranslate.X = mouse.X - worldX * newScale;
        CanvasTranslate.Y = mouse.Y - worldY * newScale;

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
                e.Handled = true;
                return;
            }
        }

        if (App.MainVM.CurrentNotebook is null) return;

        // 노트 위에서 우클릭한 경우 무시 (빈 캔버스 영역만 처리)
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl) return;
            d = VisualTreeHelper.GetParent(d);
        }

        if (sender is not IInputElement el) return;
        var screen = e.GetPosition(el);
        var (worldX, worldY) = ScreenToWorld(screen);

        var menu = new ContextMenu();
        var addText = new MenuItem { Header = "새 텍스트 노트" };
        addText.Click += (_, _) => App.MainVM.AddTextNote(string.Empty, worldX, worldY);
        menu.Items.Add(addText);

        var selectedCount = App.MainVM.SelectedNotes.Count();
        if (selectedCount >= 1)
        {
            var groupItem = new MenuItem { Header = $"그룹 만들기 ({selectedCount}개) — Ctrl+G" };
            groupItem.Click += (_, _) => App.MainVM.CreateGroupFromSelectedNotes();
            menu.Items.Add(new Separator());
            menu.Items.Add(groupItem);
        }
        var selectedGroups = App.MainVM.SelectedGroups.ToList();
        if (selectedGroups.Count >= 1)
        {
            var ungroup = new MenuItem { Header = $"그룹 해제 ({selectedGroups.Count}개) — Ctrl+Shift+G" };
            ungroup.Click += (_, _) =>
            {
                foreach (var g in selectedGroups) App.MainVM.DeleteGroup(g);
            };
            if (selectedCount < 1) menu.Items.Add(new Separator());
            menu.Items.Add(ungroup);
        }

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
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
