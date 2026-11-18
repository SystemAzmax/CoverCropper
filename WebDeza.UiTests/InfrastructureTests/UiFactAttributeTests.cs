using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>環境変数 WEBDEZA_UITEST=1 が無いと、UI テストがスキップされる（画面を動かさないので、普段のテストで実行する）</summary>
public class UiFactAttributeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1")]
    public void SkipReason_WithoutOne_Skips(string? value)
    {
        var reason = UiTestEnvironment.SkipReason(value);

        Assert.NotNull(reason);
        Assert.Contains("WEBDEZA_UITEST=1", reason);
    }

    [Fact]
    public void SkipReason_One_Runs()
    {
        Assert.Null(UiTestEnvironment.SkipReason("1"));
    }

    [Fact]
    public void UiFact_FollowsEnvironmentVariable()
    {
        string? value = Environment.GetEnvironmentVariable(UiTestEnvironment.VariableName);

        var attribute = new UiFactAttribute();

        Assert.Equal(UiTestEnvironment.SkipReason(value), attribute.Skip);
        Assert.Equal(value == "1", UiTestEnvironment.IsEnabled);
    }

    [Fact]
    public void Exes_AreInTheSameConfiguration()
    {
        Assert.True(File.Exists(Path.Combine(UiTestEnvironment.RepositoryRoot, "CoverCropper.slnx")), UiTestEnvironment.RepositoryRoot);
        Assert.Contains(Path.Combine("bin", UiTestEnvironment.Configuration, "net10.0-windows"), UiTestEnvironment.WebDezaExe);
        Assert.EndsWith("webDeza.exe", UiTestEnvironment.WebDezaExe);
        Assert.EndsWith("CoverCropper.exe", UiTestEnvironment.CoverCropperExe);
    }
}
