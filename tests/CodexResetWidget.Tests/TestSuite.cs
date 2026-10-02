internal sealed class TestSuite(string name)
{
    private int _passed;
    private int _failed;
    public (int Passed, int Failed) Result => (_passed, _failed);
    public void Check(string description, Action run)
    {
        try { run(); _passed++; Console.WriteLine($"PASS [{name}] {description}"); }
        catch (Exception error) { _failed++; Console.WriteLine($"FAIL [{name}] {description}: {error.Message}"); }
    }
    public async Task CheckAsync(string description, Func<Task> run)
    {
        try { await run(); _passed++; Console.WriteLine($"PASS [{name}] {description}"); }
        catch (Exception error) { _failed++; Console.WriteLine($"FAIL [{name}] {description}: {error.Message}"); }
    }
}
internal static class TestAssert
{
    public static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    public static void True(bool value) => Assert(value);
    public static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
}
