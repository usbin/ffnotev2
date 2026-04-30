using System.Text.Json.Serialization;
using System.Windows.Input;
using ffnotev2.Services;

namespace ffnotev2.Models;

public class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    [JsonIgnore]
    public HotkeyModifiers ModifiersFlags => (HotkeyModifiers)Modifiers;

    public HotkeyBinding() { }

    public HotkeyBinding(HotkeyModifiers modifiers, uint vk)
    {
        Modifiers = (uint)modifiers;
        VirtualKey = vk;
    }

    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            var m = ModifiersFlags;
            if (m.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (m.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");

            var key = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }
    }

    public HotkeyBinding Clone() => new HotkeyBinding
    {
        Modifiers = Modifiers,
        VirtualKey = VirtualKey
    };

    /// <summary>로컬(WPF) 키 이벤트와 이 바인딩이 일치하는지. NoRepeat 같은 글로벌 전용 플래그는 무시.</summary>
    public bool MatchesLocal(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk != VirtualKey) return false;

        var m = ModifiersFlags;
        var mods = Keyboard.Modifiers;
        bool wantCtrl = m.HasFlag(HotkeyModifiers.Control);
        bool wantAlt = m.HasFlag(HotkeyModifiers.Alt);
        bool wantShift = m.HasFlag(HotkeyModifiers.Shift);
        bool wantWin = m.HasFlag(HotkeyModifiers.Win);
        bool hasCtrl = mods.HasFlag(ModifierKeys.Control);
        bool hasAlt = mods.HasFlag(ModifierKeys.Alt);
        bool hasShift = mods.HasFlag(ModifierKeys.Shift);
        bool hasWin = mods.HasFlag(ModifierKeys.Windows);
        return wantCtrl == hasCtrl && wantAlt == hasAlt && wantShift == hasShift && wantWin == hasWin;
    }
}
