using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ffnotev2;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_LAYERED = 0x00080000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public bool IsClickThrough { get; private set; }

    private readonly DispatcherTimer _locationSaveTimer;
    private bool _suppressLocationSave;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = App.OverlayVM;

        _locationSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _locationSaveTimer.Tick += (_, _) =>
        {
            _locationSaveTimer.Stop();
            SaveLocationToSettings();
        };

        Loaded += (_, _) =>
        {
            var settings = ((App)Application.Current).SettingsService.Settings;
            Opacity = Math.Clamp(settings.OverlayOpacity, 0.2, 1.0);

            // 위치 복원: LocationChanged의 즉시 저장이 발생하지 않도록 가드
            _suppressLocationSave = true;
            (Left, Top) = ClampToVisibleScreen(settings.OverlayLeft, settings.OverlayTop);
            _suppressLocationSave = false;
        };

        LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_suppressLocationSave) return;
        // 디바운싱: 드래그 중 픽셀마다 저장하면 IO 폭증 → 500ms 정지 후 저장
        _locationSaveTimer.Stop();
        _locationSaveTimer.Start();
    }

    private void SaveLocationToSettings()
    {
        var app = (App)Application.Current;
        app.SettingsService.Settings.OverlayLeft = Left;
        app.SettingsService.Settings.OverlayTop = Top;
        app.SettingsService.Save();
    }

    /// <summary>저장된 좌표가 모든 모니터 영역 밖이면 기본값(40,40)으로.</summary>
    private (double left, double top) ClampToVisibleScreen(double left, double top)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var visible = screens.Any(s =>
        {
            var b = s.WorkingArea;
            return left + Width > b.Left && left < b.Right
                && top + Height > b.Top && top < b.Bottom;
        });
        return visible ? (left, top) : (40, 40);
    }

    private void SaveOpacityToSettings()
    {
        var app = (App)Application.Current;
        app.SettingsService.Settings.OverlayOpacity = Opacity;
        app.SettingsService.Save();
    }

    private void QuickInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // TextBox는 한 번 늘어나면 자연 축소가 안 됨 → SizeToContent 리셋으로 강제 재측정
        if (sender is TextBox tb)
        {
            tb.Height = double.NaN;
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.Height;
        }
    }

    private void QuickInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Shift+Enter는 줄바꿈 (TextBox 기본 동작)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) return;
            App.OverlayVM.SubmitCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Border_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // TextBox 영역 우클릭은 기본 컨텍스트(복사·붙여넣기) 살리기
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d is not null)
        {
            if (d is TextBox) return;
            d = VisualTreeHelper.GetParent(d);
        }

        ShowOpacityMenu();
        e.Handled = true;
    }

    private void ShowOpacityMenu()
    {
        var menu = new ContextMenu();

        var inc = new MenuItem { Header = $"더 진하게 (+10%) — 현재 {Opacity:P0}" };
        inc.Click += (_, _) => AdjustOpacity(+0.1);
        var dec = new MenuItem { Header = "더 투명하게 (-10%)" };
        dec.Click += (_, _) => AdjustOpacity(-0.1);
        var reset = new MenuItem { Header = "기본 투명도" };
        reset.Click += (_, _) =>
        {
            Opacity = 1.0;
            SaveOpacityToSettings();
        };

        menu.Items.Add(inc);
        menu.Items.Add(dec);
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);

        menu.PlacementTarget = RootBorder;
        menu.IsOpen = true;
    }

    private void AdjustOpacity(double delta)
    {
        Opacity = Math.Clamp(Opacity + delta, 0.2, 1.0);
        SaveOpacityToSettings();
    }

    public void ToggleClickThrough() => SetClickThrough(!IsClickThrough);

    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if (enabled)
        {
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            ex &= ~WS_EX_TRANSPARENT;
        }
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        IsClickThrough = enabled;
        ClickThroughIndicator.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
