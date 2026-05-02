using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ffnotev2.Models;
using FontFamily = System.Windows.Media.FontFamily;
using Fonts = System.Windows.Media.Fonts;

namespace ffnotev2.Dialogs;

public partial class FontSettingsDialog : Window
{
    public string ResultFontFamily { get; private set; } = "Segoe UI";
    public double ResultFontSize { get; private set; } = 13;

    public FontSettingsDialog(AppSettings settings)
    {
        InitializeComponent();

        var families = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontCombo.ItemsSource = families;

        FontCombo.SelectedItem = families.Contains(settings.NoteFontFamily)
            ? settings.NoteFontFamily
            : "Segoe UI";
        SizeSlider.Value = settings.NoteFontSize;

        UpdatePreview();
    }

    private void OnAnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText is null) return;
        if (FontCombo.SelectedItem is string family)
            PreviewText.FontFamily = new FontFamily(family);
        PreviewText.FontSize = SizeSlider.Value;
        if (SizeLabel is not null) SizeLabel.Text = ((int)SizeSlider.Value).ToString();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        FontCombo.SelectedItem = "Segoe UI";
        SizeSlider.Value = 13;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultFontFamily = (FontCombo.SelectedItem as string) ?? "Segoe UI";
        ResultFontSize = Math.Clamp(SizeSlider.Value, 9, 28);
        DialogResult = true;
    }
}
