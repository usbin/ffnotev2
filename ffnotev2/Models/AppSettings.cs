using ffnotev2.Services;

namespace ffnotev2.Models;

public class AppSettings
{
    public HotkeyBinding ShowMain { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
        0x4E /* N */);

    public HotkeyBinding ToggleOverlay { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
        0x4D /* M */);

    public HotkeyBinding ToggleClickThrough { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
        0x5A /* Z */);

    /// <summary>노트북 1~10 빠른 전환. 기본 Ctrl+1, Ctrl+2, ..., Ctrl+9, Ctrl+0.
    /// 인덱스 N의 단축키가 눌리면 Notebooks[N]으로 CurrentNotebook 변경.</summary>
    public HotkeyBinding[] NotebookSwitches { get; set; } = DefaultNotebookSwitches();

    public static HotkeyBinding[] DefaultNotebookSwitches()
    {
        // VK_1 ~ VK_9 (0x31~0x39), 마지막은 VK_0 (0x30)
        uint[] vk = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 };
        return vk.Select(k => new HotkeyBinding(HotkeyModifiers.Control, k)).ToArray();
    }

    public double OverlayOpacity { get; set; } = 1.0;

    public bool AutoStartOnLogin { get; set; }

    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 40;
}
