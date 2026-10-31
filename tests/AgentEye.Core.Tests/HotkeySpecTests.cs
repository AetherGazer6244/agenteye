using AgentEye.Core.Hotkeys;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// HotkeySpec 契约测试:文本 ⇄ (修饰键, 键码) 的解析、校验与往返。
/// 修饰键常量与 Win32 MOD_* 同值,壳层直接喂 RegisterHotKey。
/// </summary>
public sealed class HotkeySpecTests
{
    [Fact]
    public void Parse_DefaultText_YieldsCtrlShiftA()
    {
        Assert.True(HotkeySpec.TryParse(HotkeySpec.DefaultText, out var spec, out var err));
        Assert.Null(err);
        Assert.NotNull(spec);
        Assert.Equal(HotkeySpec.ModControl | HotkeySpec.ModShift, spec!.Modifiers);
        Assert.Equal(0x41, spec.VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", HotkeySpec.ModControl | HotkeySpec.ModAlt, 0x41)]
    [InlineData("ctrl+shift+a", HotkeySpec.ModControl | HotkeySpec.ModShift, 0x41)]
    [InlineData("Alt+F4", HotkeySpec.ModAlt, 0x73)]
    [InlineData("Ctrl+Shift+F12", HotkeySpec.ModControl | HotkeySpec.ModShift, 0x7B)]
    [InlineData("Win+Space", HotkeySpec.ModWin, 0x20)]
    [InlineData("Ctrl+Alt+Shift+Win+K", HotkeySpec.ModControl | HotkeySpec.ModAlt | HotkeySpec.ModShift | HotkeySpec.ModWin, 0x4B)]
    [InlineData("Ctrl+0", HotkeySpec.ModControl, 0x30)]
    public void Parse_ValidCombinations(string text, int mods, int vk)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec, out var err));
        Assert.Null(err);
        Assert.Equal(mods, spec!.Modifiers);
        Assert.Equal(vk, spec.VirtualKey);
    }

    [Fact]
    public void Parse_ModifierOrderIsIrrelevant()
    {
        Assert.True(HotkeySpec.TryParse("Shift+Ctrl+A", out var a, out _));
        Assert.True(HotkeySpec.TryParse("Ctrl+Shift+A", out var b, out _));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Parse_WithoutModifier_IsRejected()
    {
        Assert.False(HotkeySpec.TryParse("A", out var spec, out var err));
        Assert.Null(spec);
        Assert.Contains("修饰键", err);
    }

    [Fact]
    public void Parse_UnknownKey_IsRejected()
    {
        Assert.False(HotkeySpec.TryParse("Ctrl+NotAKey", out _, out var err));
        Assert.Contains("无法识别", err);
    }

    [Fact]
    public void Parse_MultipleMainKeys_IsRejected()
    {
        Assert.False(HotkeySpec.TryParse("Ctrl+A+B", out _, out var err));
        Assert.Contains("主键", err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    public void Parse_Incomplete_IsRejected(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Theory]
    [InlineData("Ctrl+Shift+A")]
    [InlineData("Ctrl+Alt+F2")]
    [InlineData("Win+Space")]
    [InlineData("Ctrl+Alt+Shift+Win+M")]
    public void RoundTrip_TextToSpecToText(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec, out _));
        Assert.Equal(text, spec!.ToText());
    }

    [Fact]
    public void TryFromVirtualKey_ModifierAlone_IsRejected()
    {
        // VK_CONTROL 自身不能当主键
        Assert.False(HotkeySpec.TryFromVirtualKey(0x11, HotkeySpec.ModControl, out _, out var err));
        Assert.Contains("修饰键", err);
    }

    [Fact]
    public void TryFromVirtualKey_WithoutModifier_IsRejected()
    {
        Assert.False(HotkeySpec.TryFromVirtualKey(0x41, 0, out _, out var err));
        Assert.Contains("修饰键", err);
    }

    [Fact]
    public void TryFromVirtualKey_SupportedKey_BuildsSpec()
    {
        Assert.True(HotkeySpec.TryFromVirtualKey(0x41, HotkeySpec.ModControl | HotkeySpec.ModShift, out var spec, out var err));
        Assert.Null(err);
        Assert.Equal("Ctrl+Shift+A", spec!.ToText());
    }

    [Fact]
    public void TryFromVirtualKey_UnsupportedKey_IsRejected()
    {
        Assert.False(HotkeySpec.TryFromVirtualKey(0x07, HotkeySpec.ModControl, out _, out var err));
        Assert.Contains("不支持", err);
    }
}
