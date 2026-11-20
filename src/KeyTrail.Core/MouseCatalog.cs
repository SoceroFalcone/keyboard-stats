namespace KeyTrail.Core;

public static class MouseCatalog
{
    public static IReadOnlyList<KeyDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new KeyDefinition("MouseLeft", "左键", "鼠标左键", "鼠标按键"),
        new KeyDefinition("MouseRight", "右键", "鼠标右键", "鼠标按键"),
        new KeyDefinition("MouseMiddle", "中键", "鼠标中键", "鼠标按键"),
        new KeyDefinition("MouseX1", "侧键 1", "鼠标侧键 1", "鼠标按键"),
        new KeyDefinition("MouseX2", "侧键 2", "鼠标侧键 2", "鼠标按键")
    });

    public static bool IsMouse(string id) => id is "MouseLeft" or "MouseRight" or "MouseMiddle" or "MouseX1" or "MouseX2";
    public static bool IsButtonMessage(int message) => message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C;
    public static string? Resolve(int message, uint mouseData, out bool down)
    {
        down = message is 0x201 or 0x204 or 0x207 or 0x20B;
        return message switch
        {
            0x201 or 0x202 => "MouseLeft",
            0x204 or 0x205 => "MouseRight",
            0x207 or 0x208 => "MouseMiddle",
            0x20B or 0x20C => (mouseData >> 16) switch { 1 => "MouseX1", 2 => "MouseX2", _ => null },
            _ => null
        };
    }
}
