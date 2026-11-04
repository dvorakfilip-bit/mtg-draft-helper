namespace MtgDraftHelper.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Klávesová zkratka v textovém tvaru, např. <c>Ctrl+Shift+D</c>. Název klávesy odpovídá
/// názvům WPF <c>Key</c> (D, F9, OemTilde, …); převod na kód klávesy dělá UI.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>Zkratka musí mít aspoň jeden modifikátor (kromě F-kláves), aby nekolidovala s psaním.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = new Hotkey(HotkeyModifiers.None, "");
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null)
                        return false; // dvě ne-modifikační klávesy
                    key = part.Length == 1 ? part.ToUpperInvariant() : part;
                    break;
            }
        }

        if (key is null)
            return false;
        var isFunctionKey = key.Length >= 2 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
            return false;

        hotkey = new Hotkey(modifiers, key);
        return true;
    }
}
