using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using ffnotev2.Models;
using ffnotev2.Services;
using FontFamily = System.Windows.Media.FontFamily;
using Fonts = System.Windows.Media.Fonts;

namespace ffnotev2.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _working;

    public ObservableCollection<NotebookHotkeyEntry> NotebookEntries { get; } = new();

    public SettingsDialog(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        // 취소 시 원본 보존을 위한 작업 사본
        _working = new AppSettings
        {
            ShowMain = current.ShowMain.Clone(),
            ToggleOverlay = current.ToggleOverlay.Clone(),
            ToggleClickThrough = current.ToggleClickThrough.Clone(),
            NotebookSwitches = current.NotebookSwitches.Select(b => b.Clone()).ToArray(),
            NoteFontFamily = current.NoteFontFamily,
            NoteFontSize = current.NoteFontSize,
            ShowLineNumbers = current.ShowLineNumbers,
            EditorMonospace = current.EditorMonospace,
            AutoStartOnLogin = current.AutoStartOnLogin,
            EnableAutoPanForDrag = current.EnableAutoPanForDrag,
        };

        // 폰트 콤보
        var families = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontCombo.ItemsSource = families;
        FontCombo.SelectedItem = families.Contains(_working.NoteFontFamily)
            ? _working.NoteFontFamily : "Segoe UI";
        SizeSlider.Value = _working.NoteFontSize;

        // 노트북 단축키
        BuildNotebookEntries();
        NotebookSwitchesList.ItemsSource = NotebookEntries;

        // 체크박스
        ShowLineNumbersCheck.IsChecked = _working.ShowLineNumbers;
        EditorMonospaceCheck.IsChecked = _working.EditorMonospace;
        AutoStartCheck.IsChecked = _working.AutoStartOnLogin;
        EnableAutoPanCheck.IsChecked = _working.EnableAutoPanForDrag;

        RefreshAllBoxes();
        UpdatePreview();
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

    private void OnFontChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText is null) return;
        if (FontCombo.SelectedItem is string family)
            PreviewText.FontFamily = new FontFamily(family);
        PreviewText.FontSize = SizeSlider.Value;
        if (SizeLabel is not null) SizeLabel.Text = ((int)SizeSlider.Value).ToString();
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Background = System.Windows.Media.Brushes.LightYellow;
    }

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

        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return false;
        }

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
        _working.NoteFontFamily = def.NoteFontFamily;
        _working.NoteFontSize = def.NoteFontSize;
        _working.ShowLineNumbers = def.ShowLineNumbers;
        _working.EditorMonospace = def.EditorMonospace;
        _working.EnableAutoPanForDrag = def.EnableAutoPanForDrag;
        // AutoStart는 시스템 상태 — 기본값으로 강제 변경하지 않음 (사용자가 직접 토글)

        BuildNotebookEntries();
        NotebookSwitchesList.ItemsSource = null;
        NotebookSwitchesList.ItemsSource = NotebookEntries;
        FontCombo.SelectedItem = _working.NoteFontFamily;
        SizeSlider.Value = _working.NoteFontSize;
        ShowLineNumbersCheck.IsChecked = _working.ShowLineNumbers;
        EditorMonospaceCheck.IsChecked = _working.EditorMonospace;
        EnableAutoPanCheck.IsChecked = _working.EnableAutoPanForDrag;
        RefreshAllBoxes();
        UpdatePreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _working.NoteFontFamily = (FontCombo.SelectedItem as string) ?? "Segoe UI";
        _working.NoteFontSize = Math.Clamp(SizeSlider.Value, 9, 28);
        _working.ShowLineNumbers = ShowLineNumbersCheck.IsChecked == true;
        _working.EditorMonospace = EditorMonospaceCheck.IsChecked == true;
        _working.AutoStartOnLogin = AutoStartCheck.IsChecked == true;
        _working.EnableAutoPanForDrag = EnableAutoPanCheck.IsChecked == true;
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

    public void Refresh() => OnPropertyChanged(nameof(Display));

    partial void OnBindingChanged(HotkeyBinding value) => OnPropertyChanged(nameof(Display));
}
