using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using ffnotev2.Models;
using ffnotev2.Services;

namespace ffnotev2.Dialogs;

public partial class HotkeySettingsDialog : Window
{
    private readonly AppSettings _working;

    public ObservableCollection<NotebookHotkeyEntry> NotebookEntries { get; } = new();

    public HotkeySettingsDialog(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        // 작업용 사본 (취소 시 원본 보존)
        _working = new AppSettings
        {
            ShowMain = current.ShowMain.Clone(),
            ToggleOverlay = current.ToggleOverlay.Clone(),
            ToggleClickThrough = current.ToggleClickThrough.Clone(),
            NotebookSwitches = current.NotebookSwitches.Select(b => b.Clone()).ToArray()
        };
        BuildNotebookEntries();
        NotebookSwitchesList.ItemsSource = NotebookEntries;
        RefreshAllBoxes();
    }

    public AppSettings Result => _working;

    private void BuildNotebookEntries()
    {
        NotebookEntries.Clear();
        for (int i = 0; i < _working.NotebookSwitches.Length; i++)
        {
            var idx = i;
            NotebookEntries.Add(new NotebookHotkeyEntry
            {
                Index = idx,
                Label = $"노트북 {idx + 1}",
                Binding = _working.NotebookSwitches[idx]
            });
        }
    }

    private void RefreshAllBoxes()
    {
        ShowMainBox.Text = _working.ShowMain.DisplayString;
        ToggleOverlayBox.Text = _working.ToggleOverlay.DisplayString;
        ToggleClickThroughBox.Text = _working.ToggleClickThrough.DisplayString;
        foreach (var entry in NotebookEntries) entry.Refresh();
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Background = System.Windows.Media.Brushes.LightYellow;
    }

    /// <summary>글로벌 단축키 캡처 — 모디파이어 1개 이상 필수 (Win32 RegisterHotKey 권장).</summary>
    private void GlobalHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!TryCaptureKey(e, out var key, out var modifiers, requireModifier: true)) return;
        if (sender is not TextBox tb || tb.Tag is not string targetKey) return;

        var binding = new HotkeyBinding(modifiers | HotkeyModifiers.NoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
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

    /// <summary>로컬(노트북 전환) 단축키 캡처 — 모디파이어 없어도 허용.</summary>
    private void LocalHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!TryCaptureKey(e, out var key, out var modifiers, requireModifier: false)) return;
        if (sender is not TextBox tb || tb.Tag is not NotebookHotkeyEntry entry) return;

        var binding = new HotkeyBinding(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        entry.Binding = binding;
        _working.NotebookSwitches[entry.Index] = binding;
        entry.Refresh();
        tb.Background = System.Windows.Media.Brushes.White;
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private bool TryCaptureKey(KeyEventArgs e, out Key key, out HotkeyModifiers modifiers, bool requireModifier)
    {
        key = e.Key == Key.System ? e.SystemKey : e.Key;
        modifiers = HotkeyModifiers.None;

        // 단독 modifier 키는 캡처 안 함 (사용자가 modifier 만 누른 상태)
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return false;
        }

        // ESC는 캡처 취소
        if (key == Key.Escape)
        {
            if (e.OriginalSource is TextBox tbCancel) tbCancel.Background = System.Windows.Media.Brushes.White;
            Keyboard.ClearFocus();
            e.Handled = true;
            return false;
        }

        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        if (requireModifier && modifiers == HotkeyModifiers.None)
        {
            // 글로벌 핫키는 modifier 1개 이상 권장 — 무효화
            e.Handled = true;
            return false;
        }
        return true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        _working.ShowMain = def.ShowMain;
        _working.ToggleOverlay = def.ToggleOverlay;
        _working.ToggleClickThrough = def.ToggleClickThrough;
        _working.NotebookSwitches = AppSettings.DefaultNotebookSwitches();
        BuildNotebookEntries();
        NotebookSwitchesList.ItemsSource = null;
        NotebookSwitchesList.ItemsSource = NotebookEntries;
        RefreshAllBoxes();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public partial class NotebookHotkeyEntry : ObservableObject
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private HotkeyBinding binding = new HotkeyBinding();

    public string Display => Binding.DisplayString;

    /// <summary>Binding이 직접 교체된 후 Display 갱신 알림 (ObservableProperty 자동 발생 외 수동 호출용).</summary>
    public void Refresh() => OnPropertyChanged(nameof(Display));

    partial void OnBindingChanged(HotkeyBinding value) => OnPropertyChanged(nameof(Display));
}
