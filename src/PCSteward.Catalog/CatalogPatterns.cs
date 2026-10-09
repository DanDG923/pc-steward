using System.Text.RegularExpressions;

namespace PCSteward.Catalog;

/// <summary>
/// Detection-pattern rules (ADR-023, amended by Task 45). Patterns use the interpreted engine with a
/// per-match timeout. A restricted grammar removes the constructs that can backtrack catastrophically
/// or do more than plain matching. NonBacktracking was measured at ~5.8 ms construction per pattern.
/// </summary>
public static class CatalogPatterns
{
    public const int MaximumPatternLength = 128;
    public const int MaximumInputLength = 512;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>True when the pattern is anchored, bounded and uses only the allowed grammar.</summary>
    public static bool IsAllowed(string? pattern) =>
        pattern is { Length: > 2 and <= MaximumPatternLength } &&
        pattern[0] == '^' &&
        pattern[^1] == '$' &&
        !pattern.Any(char.IsControl) &&
        UsesAllowedGrammar(pattern);

    /// <summary>Compiles an allowed pattern. Throws <see cref="ArgumentException"/> for anything else.</summary>
    public static Regex Compile(string pattern)
    {
        if (!IsAllowed(pattern)) throw new ArgumentException("The detection pattern is outside the allowed grammar.", nameof(pattern));
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
    }

    /// <summary>Matches bounded input. A timeout or oversize input is treated as no match.</summary>
    public static bool IsMatch(Regex pattern, string? input)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (input is null || input.Length > MaximumInputLength) return false;
        try { return pattern.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    // A small scanner (escape- and character-class-aware) refuses: a quantified group (an unescaped
    // ')' followed by '*', '+' or '{'; only '?' may follow a group), backreferences ('\1'-'\9', '\k'),
    // and every '(?' construct (lookarounds, inline options, named, atomic and non-capturing groups,
    // conditionals). Unbalanced brackets are refused too.
    private static bool UsesAllowedGrammar(string pattern)
    {
        bool inClass = false;
        int depth = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            if (current == '\\')
            {
                if (index + 1 >= pattern.Length) return false;
                char escaped = pattern[++index];
                if (!inClass && (escaped is >= '1' and <= '9' || escaped == 'k')) return false;
                continue;
            }

            if (inClass)
            {
                if (current == ']') inClass = false;
                continue;
            }

            switch (current)
            {
                case '[':
                    inClass = true;
                    if (index + 1 < pattern.Length && pattern[index + 1] == ']') index++;
                    break;
                case '(':
                    if (index + 1 < pattern.Length && pattern[index + 1] == '?') return false;
                    depth++;
                    break;
                case ')':
                    if (--depth < 0) return false;
                    if (index + 1 < pattern.Length && pattern[index + 1] is '*' or '+' or '{') return false;
                    break;
            }
        }

        return !inClass && depth == 0;
    }
}
