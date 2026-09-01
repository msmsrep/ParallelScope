using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class AppThemeTests
{
    [Theory]
    [InlineData("System", AppThemeSetting.System)]
    [InlineData("Light", AppThemeSetting.Light)]
    [InlineData("Dark", AppThemeSetting.Dark)]
    [InlineData("dark", AppThemeSetting.Dark)]
    public void Parse_AcceptsSettingNamesCaseInsensitively(string value, AppThemeSetting expected)
    {
        Assert.Equal(expected, AppTheme.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sepia")]
    public void Parse_FallsBackToSystemForMissingOrUnknownValues(string? value)
    {
        // 設定ファイルが古い/壊れている場合でも起動できるよう、不正値はOS追従へ丸める
        Assert.Equal(AppThemeSetting.System, AppTheme.Parse(value));
    }
}
