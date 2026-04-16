namespace DnRelay.NUnitSmokeTests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
        Assert.Pass();
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ParameterizedSuccess_IsReported(int value)
    {
        Assert.That(value, Is.GreaterThan(0));
    }

    [Test]
    public void IntentionalFailure_IsReported()
    {
        Assert.That(false, Is.True, "intentional nunit smoke failure");
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ParameterizedFailure_IsReported(int value)
    {
        Assert.That(value, Is.LessThan(0), $"value should be negative but was {value}");
    }

    [Test]
    [Ignore("intentional nunit skip")]
    public void SkippedTest_IsReported()
    {
    }
}
