namespace AgentEye.Core.Hotkeys;

/// <summary>
/// 全局快捷键描述(纯逻辑,Core 不引 Win32):
/// 把 "Ctrl+Shift+A" 这样的文本解析成 (修饰键掩码, 虚拟键码),
/// 壳层直接把这对值喂给 RegisterHotKey。
///
/// 约定:
/// - 修饰键常量与 Win32 MOD_* 同值,键码与 Win32 VK 同值(0x41='A' 等)。
/// - 至少要有一个修饰键:裸键会劫持日常打字,一律拒绝。
/// - 修饰键顺序任意、大小写不敏感;主键只能有一个。
/// </summary>
public sealed record HotkeySpec(int Modifiers, int VirtualKey)
{
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;

    /// <summary>默认组合:与 A 标同字母。</summary>
    public const string DefaultText = "Ctrl+Shift+A";

    private const int VkA = 0x41;

    public static HotkeySpec Default { get; } = new(ModControl | ModShift, VkA);

    /// <summary>文本形式的组合,如 "Ctrl+Shift+A"。</summary>
    public string ToText()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    public override string ToString() => ToText();

    /// <summary>解析文本组合;失败时给出人话原因(直接展示给用户)。</summary>
    public static bool TryParse(string? text, out HotkeySpec? spec, out string? error)
    {
        spec = null;
        var raw = (text ?? "").Trim();
        if (raw.Length == 0)
        {
            error = "快捷键为空";
            return false;
        }

        var parts = raw.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mods = 0;
        int? key = null;
        foreach (var part in parts)
        {
            if (TryModifier(part, out var m))
            {
                mods |= m;
                continue;
            }
            if (!KeyNames.TryGetValue(part, out var vk))
            {
                error = $"无法识别的按键:{part}";
                return false;
            }
            if (key is not null)
            {
                error = "只能有一个主键(如 A / F2 / Space)";
                return false;
            }
            key = vk;
        }

        if (key is null)
        {
            error = "缺少主键(如 A / F2 / Space)";
            return false;
        }
        if (mods == 0)
        {
            error = "至少需要一个修饰键(Ctrl / Alt / Shift / Win)";
            return false;
        }

        spec = new HotkeySpec(mods, key.Value);
        error = null;
        return true;
    }

    /// <summary>按虚拟键码 + 修饰键位构造(设置页录制时用)。</summary>
    public static bool TryFromVirtualKey(int vk, int modifiers, out HotkeySpec? spec, out string? error)
    {
        spec = null;
        if (vk <= 0)
        {
            error = "无效按键";
            return false;
        }
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        {
            error = "请按字母/数字/功能键,不能只按修饰键";
            return false;
        }
        if (!KeyCodes.ContainsKey(vk))
        {
            error = $"暂不支持的按键(0x{vk:X2})";
            return false;
        }
        if (modifiers == 0)
        {
            error = "至少需要一个修饰键(Ctrl / Alt / Shift / Win)";
            return false;
        }
        spec = new HotkeySpec(modifiers, vk);
        error = null;
        return true;
    }

    private static bool TryModifier(string part, out int mod)
    {
        switch (part.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
            case "ctl":
                mod = ModControl;
                return true;
            case "alt":
                mod = ModAlt;
                return true;
            case "shift":
                mod = ModShift;
                return true;
            case "win":
            case "windows":
            case "meta":
                mod = ModWin;
                return true;
            default:
                mod = 0;
                return false;
        }
    }

    /// <summary>键码 → 显示名(取常用叫法)。</summary>
    public static string KeyName(int vk) =>
        KeyCodes.TryGetValue(vk, out var name) ? name : $"Key{vk:X2}";

    // 名称 → 键码(大小写不敏感)
    private static readonly Dictionary<string, int> KeyNames = BuildKeyNames();

    // 键码 → 名称(反向表,显示用)
    private static readonly Dictionary<int, string> KeyCodes = BuildKeyCodes();

    private static Dictionary<string, int> BuildKeyNames()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 26; i++) d[((char)('A' + i)).ToString()] = 0x41 + i;
        for (var i = 0; i < 10; i++) d[i.ToString()] = 0x30 + i;
        for (var i = 1; i <= 24; i++) d[$"F{i}"] = 0x70 + i - 1;
        void Add(string name, int vk) => d[name] = vk;

        Add("Space", 0x20);
        Add("Enter", 0x0D);
        Add("Return", 0x0D);
        Add("Esc", 0x1B);
        Add("Escape", 0x1B);
        Add("Tab", 0x09);
        Add("Backspace", 0x08);
        Add("BkSp", 0x08);
        Add("Pause", 0x13);
        Add("CapsLock", 0x14);
        Add("PrintScreen", 0x2C);
        Add("ScrollLock", 0x91);
        Add("Insert", 0x2D);
        Add("Ins", 0x2D);
        Add("Delete", 0x2E);
        Add("Del", 0x2E);
        Add("Home", 0x24);
        Add("End", 0x23);
        Add("PageUp", 0x21);
        Add("PgUp", 0x21);
        Add("PageDown", 0x22);
        Add("PgDn", 0x22);
        Add("Left", 0x25);
        Add("Right", 0x27);
        Add("Up", 0x26);
        Add("Down", 0x28);
        Add("Semicolon", 0xBA);
        Add("Plus", 0xBB);
        Add("Equals", 0xBB);
        Add("Comma", 0xBC);
        Add("Minus", 0xBD);
        Add("Period", 0xBE);
        Add("Slash", 0xBF);
        Add("Tilde", 0xC0);
        Add("Backslash", 0xDC);
        Add("BracketLeft", 0xDB);
        Add("BracketRight", 0xDD);
        Add("Quote", 0xDE);
        return d;
    }

    private static Dictionary<int, string> BuildKeyCodes()
    {
        var d = new Dictionary<int, string>();
        for (var i = 0; i < 26; i++) d[0x41 + i] = ((char)('A' + i)).ToString();
        for (var i = 0; i < 10; i++) d[0x30 + i] = i.ToString();
        for (var i = 1; i <= 24; i++) d[0x70 + i - 1] = $"F{i}";
        d[0x20] = "Space";
        d[0x0D] = "Enter";
        d[0x1B] = "Esc";
        d[0x09] = "Tab";
        d[0x08] = "Backspace";
        d[0x13] = "Pause";
        d[0x14] = "CapsLock";
        d[0x2C] = "PrintScreen";
        d[0x91] = "ScrollLock";
        d[0x2D] = "Insert";
        d[0x2E] = "Delete";
        d[0x24] = "Home";
        d[0x23] = "End";
        d[0x21] = "PageUp";
        d[0x22] = "PageDown";
        d[0x25] = "Left";
        d[0x27] = "Right";
        d[0x26] = "Up";
        d[0x28] = "Down";
        d[0xBA] = "Semicolon";
        d[0xBB] = "Plus";
        d[0xBC] = "Comma";
        d[0xBD] = "Minus";
        d[0xBE] = "Period";
        d[0xBF] = "Slash";
        d[0xC0] = "Tilde";
        d[0xDC] = "Backslash";
        d[0xDB] = "BracketLeft";
        d[0xDD] = "BracketRight";
        d[0xDE] = "Quote";
        return d;
    }
}
