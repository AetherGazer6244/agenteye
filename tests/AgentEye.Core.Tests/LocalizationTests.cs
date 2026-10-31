using AgentEye.Core.Localization;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// L 查表契约:zh 默认、en 映射、缺键回退键名。
/// L.Lang 是进程级静态态:en 用例必须在 finally 里还原 zh,
/// 免得污染同进程的其它用例(现有断言全按 zh 文案写)。
/// </summary>
public class LocalizationTests
{
    [Fact]
    public void Init_Defaults_To_Zh()
    {
        L.Init("zh-Hans");
        Assert.Equal("zh-Hans", L.Lang);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    public void Init_Maps_English_Variants(string raw)
    {
        try
        {
            L.Init(raw);
            Assert.Equal("en", L.Lang);
        }
        finally
        {
            L.Init("zh-Hans");
        }
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    public void Init_Non_English_Falls_Back_To_Zh(string? raw)
    {
        L.Init(raw!);
        Assert.Equal("zh-Hans", L.Lang);
    }

    [Fact]
    public void T_Zh_Returns_Chinese()
    {
        L.Init("zh-Hans");
        Assert.Equal("语言", L.T("lang.title"));
        Assert.Equal("跟随系统", L.T("theme.auto"));
    }

    [Fact]
    public void T_En_Returns_English()
    {
        try
        {
            L.Init("en");
            Assert.Equal("Language", L.T("lang.title"));
            Assert.Equal("Follow system", L.T("theme.auto"));
        }
        finally
        {
            L.Init("zh-Hans");
        }
    }

    [Fact]
    public void F_Formats_By_Language()
    {
        try
        {
            L.Init("en");
            Assert.Equal("3 providers", L.F("ov.count", 3));
            L.Init("zh-Hans");
            Assert.Equal("3 个 provider", L.F("ov.count", 3));
        }
        finally
        {
            L.Init("zh-Hans");
        }
    }

    [Fact]
    public void T_Unknown_Key_Returns_Key_Name()
    {
        Assert.Equal("no.such.key", L.T("no.such.key"));
    }

    [Fact]
    public void Zh_And_En_Dictionaries_Stay_In_Sync()
    {
        var zhKeys = GetKeys("zh");
        var enKeys = GetKeys("en");
        Assert.NotEmpty(zhKeys);
        Assert.Equal(zhKeys, enKeys);
    }

    private static IEnumerable<string> GetKeys(string lang)
    {
        var dict = (Dictionary<string, string>?)typeof(L)
            .GetField(lang == "zh" ? "Zh" : "En",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null);
        Assert.NotNull(dict);
        return dict!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
