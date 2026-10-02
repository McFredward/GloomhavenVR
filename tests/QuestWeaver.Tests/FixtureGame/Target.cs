namespace FixtureGame;

public sealed class Target
{
    private int counter = 10;
    public static readonly List<string> Trace = new();
    public int Counter => counter;
    public int Calculate(ref int value)
    {
        Trace.Add("original"); counter++;
        if (value > 900) throw new InvalidOperationException("original-failure");
        if (value < -100) throw new ArgumentException("preserved-failure");
        value++;
        return counter + value;
    }
    public int Dynamic(int value) => value * 2;
    public int Direct(ref int value) { value++; return value; }
}
