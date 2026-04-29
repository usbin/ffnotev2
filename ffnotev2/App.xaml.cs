using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ffnotev2.Dialogs;
using ffnotev2.Services;
using ffnotev2.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ffnotev2;

public partial class App : Application
{
    public static MainViewModel MainVM { get; private set; } = null!;
    public static OverlayViewModel OverlayVM { get; private set; } = null!;

    public GameDetectionService GameDetectionService { get; private set; } = null!;
    public SettingsService SettingsService { get; private set; } = null!;
    public AutoStartService AutoStart { get; private set; } = null!;

    private DatabaseService? _db;
    private MainWindow? _mainWindow;
    private OverlayWindow? _overlayWindow;
    private NotifyIcon? _tray;
    private ToolStripMenuItem? _autoStartMenuItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _db = new DatabaseService();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터베이스 초기화 실패:\n{ex.Message}", "ffnote v2",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        SettingsService = new SettingsService(_db.DataDirectory);
        AutoStart = new AutoStartService();
        SyncAutoStartOnStartup();
        GameDetectionService = new GameDetectionService();
        MainVM = new MainViewModel(_db, GameDetectionService);
        OverlayVM = new OverlayViewModel(MainVM, SettingsService);

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        _overlayWindow = new OverlayWindow();
        _overlayWindow.Show();

        SetupTray();

        GameDetectionService.Start();
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ffnote v2",
            Visible = true
        };

        _autoStartMenuItem = new ToolStripMenuItem("Windows 시작 시 자동 실행")
        {
            Checked = SettingsService.Settings.AutoStartOnLogin,
            CheckOnClick = true
        };
        _autoStartMenuItem.Click += (_, _) => OnAutoStartToggleClicked();

        var menu = new ContextMenuStrip();
        menu.Items.Add("노트 열기", null, (_, _) => ShowMain());
        menu.Items.Add("오버레이 토글", null, (_, _) => ToggleOverlay());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("단축키 설정...", null, (_, _) => ShowHotkeySettings());
        menu.Items.Add(_autoStartMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    /// <summary>설정과 실제 레지스트리 상태를 일치시킨다 (앱 시작 시 1회).</summary>
    private void SyncAutoStartOnStartup()
    {
        var wantEnabled = SettingsService.Settings.AutoStartOnLogin;
        var isEnabled = AutoStart.IsEnabled;
        if (wantEnabled && !isEnabled) AutoStart.Enable();
        else if (!wantEnabled && isEnabled) AutoStart.Disable();
        // 켜져있어야 하면 매번 경로 갱신 (사용자가 .exe를 옮겼을 수 있음)
        else if (wantEnabled && isEnabled) AutoStart.Enable();
    }

    private void OnAutoStartToggleClicked()
    {
        if (_autoStartMenuItem is null) return;
        var enabled = _autoStartMenuItem.Checked;  // CheckOnClick=true → 클릭 시점에 이미 토글된 값
        var ok = AutoStart.SetEnabled(enabled);
        if (!ok)
        {
            // Enable 실패 (보통 ProcessPath가 비어있는 경우는 거의 없음)
            _autoStartMenuItem.Checked = false;
            MessageBox.Show("자동 실행 등록에 실패했습니다.", "ffnote v2",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SettingsService.Settings.AutoStartOnLogin = enabled;
        SettingsService.Save();
    }

    public void ShowMain()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ToggleOverlay()
    {
        if (_overlayWindow is null) return;
        if (_overlayWindow.IsVisible) _overlayWindow.Hide();
        else
        {
            _overlayWindow.Show();
            _overlayWindow.Activate();
        }
    }

    public void ToggleOverlayClickThrough()
    {
        if (_overlayWindow is null) return;
        // 패스스루를 켜려면 창이 보여야 의미가 있음
        if (!_overlayWindow.IsVisible) _overlayWindow.Show();
        _overlayWindow.ToggleClickThrough();
    }

    public void ShowHotkeySettings()
    {
        if (_mainWindow is null) return;
        ShowMain();

        var dlg = new HotkeySettingsDialog(SettingsService.Settings) { Owner = _mainWindow };
        if (dlg.ShowDialog() == true)
        {
            SettingsService.Settings.ShowMain = dlg.Result.ShowMain;
            SettingsService.Settings.ToggleOverlay = dlg.Result.ToggleOverlay;
            SettingsService.Settings.ToggleClickThrough = dlg.Result.ToggleClickThrough;
            SettingsService.Save();

            if (!_mainWindow.ReregisterHotkeys())
            {
                MessageBox.Show(
                    "일부 단축키 등록에 실패했습니다.\n다른 앱이 같은 조합을 점유 중일 수 있습니다.",
                    "ffnote v2", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ExitApp()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        GameDetectionService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        GameDetectionService?.Dispose();
        base.OnExit(e);
    }
}
