using ffnotev2.Services;

namespace ffnotev2.Models;

public class AppSettings
{
    public HotkeyBinding ShowMain { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x4E /* N */);

    public HotkeyBinding ToggleOverlay { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x4D /* M */);

    public HotkeyBinding ToggleClickThrough { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
        0x5A /* Z */);

    public double OverlayOpacity { get; set; } = 1.0;

    public bool AutoStartOnLogin { get; set; }

    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 40;
}
