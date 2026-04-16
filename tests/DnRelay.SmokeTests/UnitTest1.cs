namespace DnRelay.SmokeTests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {

    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ParameterizedSuccess_IsReported(int value)
    {
        Assert.True(value > 0);
    }

    [Fact(Skip = "intentional xunit skip")]
    public void SkippedTest_IsReported()
    {
    }

    [Fact]
    public void EnvironmentVariable_IsInjected()
    {
        Assert.Equal("expected-from-harness", Environment.GetEnvironmentVariable("DOTNET_HARNESS_SMOKE"));
    }
}
