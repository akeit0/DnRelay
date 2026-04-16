namespace DnRelay.FailureSmokeTests;

public class UnitTest1
{
    [Fact]
    public void IntentionalFailure_IsReported()
    {
        Assert.Fail("intentional smoke failure");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ParameterizedFailure_IsReported(int value)
    {
        Assert.True(value < 0, $"value should be negative but was {value}");
    }
}
