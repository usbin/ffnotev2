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
}
