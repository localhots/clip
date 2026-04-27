namespace Clip.Fuzz;

internal static class FuzzConfig
{
    public static long Iter { get; } = Parse(Environment.GetEnvironmentVariable("FUZZ_ITER"));

    private static long Parse(string? s) =>
        long.TryParse(s, out var n) && n > 0 ? n : 1000;
}
