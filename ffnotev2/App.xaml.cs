using ffnotev2.Services;
using ffnotev2.ViewModels;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace ffnotev2;

public partial class App : Application
{
    public static MainViewModel MainVM { get; private set; } = null!;
    public static OverlayViewModel OverlayVM { get; private set; } = null!;

    private NotifyIcon? _tray;
    private MainWindow? _mainWindow;
    private OverlayWindow? _overlayWindow;
    private GameDetectionService? _gameDetection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var db = new DatabaseService();
        _gameDetection = new GameDetectionService();
        MainVM = new MainViewModel(db, _gameDetection);
        OverlayVM = new OverlayViewModel(MainVM);

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        _overlayWindow = new OverlayWindow();
        _overlayWindow.Show();

        SetupTray();
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ffnote v2",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("노트 열기", null, (_, _) => ShowMain());
        menu.Items.Add("오버레이 토글", null, (_, _) => ToggleOverlay());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Exit());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public void ShowMain()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
        if (_mainWindow != null)
            _mainWindow.WindowState = WindowState.Normal;
    }

    public void ToggleOverlay()
    {
        if (_overlayWindow?.IsVisible == true)
            _overlayWindow.Hide();
        else
            _overlayWindow?.Show();
    }

    private void Exit()
    {
        _tray?.Dispose();
        _gameDetection?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
