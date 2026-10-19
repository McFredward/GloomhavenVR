using System;

// Test-only adversary for the real GH.Runtime global extension scope. The game
// overload is more specific than Enumerable.Skip<char> and starts at count-1.
// Never include this fixture in the production plugin or change its semantics
// to match the framework: its disagreement is exactly the regression witness.
internal static class StringExtensions
{
    internal static string Skip(this string value, int count)
        => value.Substring(Math.Min(count, value.Length) - 1);
}
