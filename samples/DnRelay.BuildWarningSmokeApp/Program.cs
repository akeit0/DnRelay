var sample = new WarningSample();
Console.WriteLine(sample.Name.Length);

sealed class WarningSample
{
    public string Name { get; set; }
}
