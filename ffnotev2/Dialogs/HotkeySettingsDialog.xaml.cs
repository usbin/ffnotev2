using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ffnotev2.Models;
using ffnotev2.Services;

namespace ffnotev2.Dialogs;

public partial class HotkeySettingsDialog : Window
{
    private readonly AppSettings _working;

    public HotkeySettingsDialog(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        // 작업용 사본 (취소 시 원본 보존)
        _working = new AppSettings
        {
            ShowMain = current.ShowMain.Clone(),
            ToggleOverlay = current.ToggleOverlay.Clone(),
            ToggleClickThrough = current.ToggleClickThrough.Clone()
        };
        RefreshAllBoxes();
    }

    public AppSettings Result => _working;

    private void RefreshAllBoxes()
    {
        ShowMainBox.Text = _working.ShowMain.DisplayString;
        ToggleOverlayBox.Text = _working.ToggleOverlay.DisplayString;
        ToggleClickThroughBox.Text = _working.ToggleClickThrough.DisplayString;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Background = System.Windows.Media.Brushes.LightYellow;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not string targetKey) return;

        // System 키(Alt 단독 등)는 SystemKey로 옴
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 단독 modifier 키는 캡처하지 않고 무시 (Ctrl만 누른 상태 등)
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        // ESC는 캡처 취소
        if (key == Key.Escape)
        {
            tb.Background = System.Windows.Media.Brushes.White;
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        var modifiers = HotkeyModifiers.NoRepeat;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        // modifier 없으면 무효 (글로벌 핫키는 modifier 1개 이상 권장)
        if ((modifiers & ~HotkeyModifiers.NoRepeat) == 0)
        {
            e.Handled = true;
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var binding = new HotkeyBinding(modifiers, vk);

        switch (targetKey)
        {
            case "ShowMain": _working.ShowMain = binding; break;
            case "ToggleOverlay": _working.ToggleOverlay = binding; break;
            case "ToggleClickThrough": _working.ToggleClickThrough = binding; break;
        }

        tb.Text = binding.DisplayString;
        tb.Background = System.Windows.Media.Brushes.White;
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        _working.ShowMain = def.ShowMain;
        _working.ToggleOverlay = def.ToggleOverlay;
        _working.ToggleClickThrough = def.ToggleClickThrough;
        RefreshAllBoxes();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
