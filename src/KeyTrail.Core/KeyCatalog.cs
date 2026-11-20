namespace KeyTrail.Core;

public sealed record KeyDefinition(string Id, string Label, string Name, string Category);

public static class KeyCatalog
{
    private static readonly Dictionary<int, string> VirtualKeys = new();
    private static readonly Dictionary<string, KeyDefinition> Definitions = new(StringComparer.Ordinal);
    public static IReadOnlyCollection<KeyDefinition> All => Definitions.Values;

    static KeyCatalog()
    {
        for (var i = 0; i < 26; i++) Add(0x41 + i, ((char)('A' + i)).ToString(), ((char)('A' + i)).ToString(), "字母键");
        for (var i = 0; i <= 9; i++) Add(0x30 + i, $"D{i}", i.ToString(), "数字键");
        for (var i = 1; i <= 24; i++) Add(0x6f + i, $"F{i}", $"F{i}", "功能键");
        for (var i = 0; i <= 9; i++) Add(0x60 + i, $"Num{i}", i.ToString(), "数字小键盘", $"小键盘 {i}");
        Add(0x08, "Backspace", "Backspace", "编辑键", "退格");
        Add(0x09, "Tab", "Tab", "编辑键", "制表");
        Add(0x0D, "Enter", "Enter", "编辑键", "回车");
        Add(0x14, "CapsLock", "Caps Lock", "修饰键", "大写锁定");
        Add(0x1B, "Escape", "Esc", "功能键", "Esc");
        Add(0x20, "Space", "Space", "编辑键", "空格");
        Add(0xA0, "LShift", "Shift", "修饰键", "左 Shift");
        Add(0xA1, "RShift", "Shift", "修饰键", "右 Shift");
        Add(0xA2, "LControl", "Ctrl", "修饰键", "左 Ctrl");
        Add(0xA3, "RControl", "Ctrl", "修饰键", "右 Ctrl");
        Add(0xA4, "LAlt", "Alt", "修饰键", "左 Alt");
        Add(0xA5, "RAlt", "Alt", "修饰键", "右 Alt");
        Add(0x5B, "LWin", "Win", "修饰键", "左 Windows");
        Add(0x5C, "RWin", "Win", "修饰键", "右 Windows");
        Add(0x5D, "Menu", "Menu", "功能键", "菜单");
        Add(0x21, "PageUp", "PgUp", "导航键", "Page Up");
        Add(0x22, "PageDown", "PgDn", "导航键", "Page Down");
        Add(0x23, "End", "End", "导航键");
        Add(0x24, "Home", "Home", "导航键");
        Add(0x25, "Left", "←", "导航键", "左方向键");
        Add(0x26, "Up", "↑", "导航键", "上方向键");
        Add(0x27, "Right", "→", "导航键", "右方向键");
        Add(0x28, "Down", "↓", "导航键", "下方向键");
        Add(0x2D, "Insert", "Ins", "导航键", "Insert");
        Add(0x2E, "Delete", "Del", "编辑键", "Delete");
        Add(0x2C, "PrintScreen", "PrtSc", "功能键", "Print Screen");
        Add(0x91, "ScrollLock", "ScrLk", "功能键", "Scroll Lock");
        Add(0x13, "Pause", "Pause", "功能键");
        Add(0x90, "NumLock", "Num", "数字小键盘", "Num Lock");
        Add(0x6A, "NumMultiply", "×", "数字小键盘", "小键盘 乘号");
        Add(0x6B, "NumAdd", "+", "数字小键盘", "小键盘 加号");
        Add(0x6D, "NumSubtract", "−", "数字小键盘", "小键盘 减号");
        Add(0x6E, "NumDecimal", ".", "数字小键盘", "小键盘 小数点");
        Add(0x6F, "NumDivide", "/", "数字小键盘", "小键盘 除号");
        Definitions["NumEnter"] = new("NumEnter", "Enter", "小键盘 回车", "数字小键盘");
        Add(0xBA, "Semicolon", ";", "符号键");
        Add(0xBB, "Equal", "=", "符号键");
        Add(0xBC, "Comma", ",", "符号键");
        Add(0xBD, "Minus", "−", "符号键");
        Add(0xBE, "Period", ".", "符号键");
        Add(0xBF, "Slash", "/", "符号键");
        Add(0xC0, "Backquote", "`", "符号键");
        Add(0xDB, "BracketLeft", "[", "符号键");
        Add(0xDC, "Backslash", "\\", "符号键");
        Add(0xDD, "BracketRight", "]", "符号键");
        Add(0xDE, "Quote", "'", "符号键");
        Add(0xE2, "IntlBackslash", "ISO", "符号键", "国际键盘附加键");
        Add(0xAD, "VolumeMute", "静音", "媒体键");
        Add(0xAE, "VolumeDown", "音量−", "媒体键");
        Add(0xAF, "VolumeUp", "音量+", "媒体键");
        Add(0xB0, "MediaNext", "下一首", "媒体键");
        Add(0xB1, "MediaPrevious", "上一首", "媒体键");
        Add(0xB2, "MediaStop", "停止", "媒体键");
        Add(0xB3, "MediaPlay", "播放", "媒体键");
        foreach (var button in MouseCatalog.All) Definitions[button.Id] = button;
    }

    private static void Add(int vk, string id, string label, string category, string? name = null)
    {
        VirtualKeys[vk] = id;
        Definitions[id] = new(id, label, name ?? label, category);
    }

    public static KeyDefinition Get(string id) => Definitions.TryGetValue(id, out var key)
        ? key : new(id, id, id, "其他键");

    public static string? Resolve(int vk, int scanCode, bool extended)
    {
        if (vk is <= 0 or >= 255) return null;
        if (vk == 0x10) return scanCode == 0x36 ? "RShift" : "LShift";
        if (vk == 0x11) return extended ? "RControl" : "LControl";
        if (vk == 0x12) return extended ? "RAlt" : "LAlt";
        if (vk == 0x0D && extended) return "NumEnter";
        // Num Lock changes virtual key codes, but these remain the same physical keypad keys.
        if (!extended && vk is >= 0x21 and <= 0x2E)
        {
            var num = scanCode switch
            {
                0x52 => "Num0", 0x4F => "Num1", 0x50 => "Num2", 0x51 => "Num3",
                0x4B => "Num4", 0x4C => "Num5", 0x4D => "Num6", 0x47 => "Num7",
                0x48 => "Num8", 0x49 => "Num9", 0x53 => "NumDecimal", _ => null
            };
            if (num is not null) return num;
        }
        if (vk == 0x0C && scanCode == 0x4C) return "Num5";
        return VirtualKeys.TryGetValue(vk, out var id) ? id : $"VK_{vk:X2}";
    }
}
