using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ffnotev2.Dialogs;
using ffnotev2.Models;
using ffnotev2.Services;

namespace ffnotev2;

public partial class MainWindow : Window
{
    private readonly HotkeyService _hotkey = new();

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

            // 윈도우 기준 마우스 좌표 → 캔버스 좌표 (사이드바 220 + 패딩 보정)
            var pos = Mouse.GetPosition(this);
            var x = Math.Max(20, pos.X - 220);
            var y = Math.Max(20, pos.Y);
            App.MainVM.PasteFromClipboard(x, y);
            e.Handled = true;
        }
    }

    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (App.MainVM.CurrentNotebook is null) return;

        // 노트 위에서 우클릭한 경우 무시 (빈 캔버스 영역만 처리)
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d is not null)
        {
            if (d is Controls.DraggableNoteControl) return;
            d = VisualTreeHelper.GetParent(d);
        }

        if (sender is not IInputElement el) return;
        var pos = e.GetPosition(el);

        var menu = new ContextMenu();
        var addText = new MenuItem { Header = "새 텍스트 노트" };
        addText.Click += (_, _) => App.MainVM.AddTextNote(string.Empty, pos.X, pos.Y);
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
