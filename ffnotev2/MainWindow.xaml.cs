using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // 편집 가능한 텍스트박스 안에서의 Ctrl+V는 기본 붙여넣기 살리기
            if (e.OriginalSource is TextBox tb && !tb.IsReadOnly) return;

            var (x, y) = GetWorldMousePosition();
            App.MainVM.PasteFromClipboard(x, y);
            e.Handled = true;
        }
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
        // 휠 클릭만 여기서 종료. 우클릭은 MouseRightButtonUp에서 처리
        if (_panButton != MouseButton.Middle || e.ChangedButton != MouseButton.Middle) return;
        EndPan();
        e.Handled = true;
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
        // 컨텍스트 메뉴 등 다른 요소가 캡처를 가져가면 안전하게 팬 상태 해제
        if (_panButton is not null) EndPan();
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
