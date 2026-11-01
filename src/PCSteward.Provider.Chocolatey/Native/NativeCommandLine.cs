using System.Text;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// Pure, portable formatting for the N0 launcher (Task 50 design rev 5 §4.1): MSVCRT command-line quoting, the explicit
/// Unicode environment block and the strict local-path checks used by <see cref="NativeLaunchSpec"/>. It reads no process
/// state and starts nothing.
/// </summary>
internal static class NativeCommandLine
{
    /// <summary>CreateProcessW accepts at most 32,767 characters including the terminating NUL.</summary>
    internal const int MaximumCommandLineLength = 32_766;

    /// <summary>Upper bound for the explicit environment block, including every separator and the final NUL.</summary>
    internal const int MaximumEnvironmentBlockLength = 32_767;

    private const int MaximumPathLength = 32_767;

    /// <summary>
    /// Builds <c>"image" arg1 arg2 …</c>. argv[0] is always quoted (it may not contain a quote); every further argument
    /// uses the MSVCRT/CommandLineToArgvW rules so the child parses exactly <paramref name="arguments"/>.
    /// </summary>
    internal static string Build(string imagePath, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        ArgumentNullException.ThrowIfNull(arguments);
        if (imagePath.Length == 0 || imagePath.Contains('"') || imagePath.Contains('\0'))
            throw new ArgumentException("The image path cannot be quoted unambiguously.", nameof(imagePath));
        StringBuilder builder = new(imagePath.Length + 2);
        builder.Append('"').Append(imagePath).Append('"');
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(arguments));
            if (argument.Contains('\0')) throw new ArgumentException("An argument contains NUL.", nameof(arguments));
            builder.Append(' ');
            AppendArgument(builder, argument);
        }
        if (builder.Length > MaximumCommandLineLength)
            throw new ArgumentException("The command line exceeds the CreateProcessW limit.", nameof(arguments));
        return builder.ToString();
    }

    /// <summary>Quotes one argument (not argv[0]) with the MSVCRT rules.</summary>
    internal static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        StringBuilder builder = new(argument.Length + 2);
        AppendArgument(builder, argument);
        return builder.ToString();
    }

    /// <summary>
    /// Builds <c>NAME=VALUE\0…\0\0</c>, sorted by upper-invariant name as Windows expects, from the given pairs only.
    /// Nothing is read from or merged with the current process environment.
    /// </summary>
    internal static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        KeyValuePair<string, string>[] pairs = [.. environment];
        foreach ((string name, string value) in pairs) ValidateVariable(name, value);
        if (pairs.Select(pair => pair.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pairs.Length)
            throw new ArgumentException("Environment names must be unique ignoring case.", nameof(environment));
        Array.Sort(pairs, static (left, right) =>
        {
            int compared = string.CompareOrdinal(left.Key.ToUpperInvariant(), right.Key.ToUpperInvariant());
            return compared != 0 ? compared : string.CompareOrdinal(left.Key, right.Key);
        });
        StringBuilder builder = new();
        foreach ((string name, string value) in pairs) builder.Append(name).Append('=').Append(value).Append('\0');
        if (pairs.Length == 0) builder.Append('\0');
        builder.Append('\0');
        if (builder.Length > MaximumEnvironmentBlockLength)
            throw new ArgumentException("The environment block exceeds the supported size.", nameof(environment));
        char[] block = new char[builder.Length];
        builder.CopyTo(0, block, 0, builder.Length);
        return block;
    }

    /// <summary>
    /// Accepts only a drive-absolute local path (<c>X:\…</c>) without quotes, wildcards, control characters, alternate
    /// streams, forward slashes or relative segments. UNC, device and <c>\\?\</c> forms are refused.
    /// </summary>
    internal static string ValidateLocalPath(string path, string parameterName)
    {
        if (path is null || path.Length is < 3 or > MaximumPathLength || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] != '\\' || path.Any(static character => char.IsControl(character) || "\"*?<>|/".Contains(character)) ||
            path.AsSpan(2).Contains(':'))
            throw new ArgumentException("The path is not a supported drive-absolute local path.", parameterName);
        string[] segments = path[3..].Split('\\');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            bool last = index == segments.Length - 1;
            // Only a single trailing separator may produce an empty segment; Windows would silently trim '.' or ' '.
            if ((segment.Length == 0 && !last) || segment.EndsWith('.') || segment.EndsWith(' '))
                throw new ArgumentException("The path is not a supported drive-absolute local path.", parameterName);
        }
        return path;
    }

    /// <summary>
    /// The image must be a local path (above) naming a <c>.exe</c> file, with no trailing separator. A <c>.bat</c>/<c>.cmd</c>
    /// image would make CreateProcessW run cmd.exe over the MSVCRT-quoted line, where cmd metacharacters apply (review L2).
    /// </summary>
    internal static string ValidateImagePath(string path, string parameterName)
    {
        ValidateLocalPath(path, parameterName);
        if (path.EndsWith('\\') || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The image must be a local .exe file path.", parameterName);
        return path;
    }

    internal static void ValidateVariable(string name, string value)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Any(char.IsControl))
            throw new ArgumentException("An environment name is empty or contains '=' or a control character.", nameof(name));
        if (value is null || value.Contains('\0'))
            throw new ArgumentException("An environment value is missing or contains NUL.", nameof(value));
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length != 0 && !argument.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        int index = 0;
        while (index < argument.Length)
        {
            char character = argument[index++];
            if (character == '\\')
            {
                int backslashes = 1;
                while (index < argument.Length && argument[index] == '\\')
                {
                    index++;
                    backslashes++;
                }
                if (index == argument.Length)
                {
                    // Trailing backslashes precede the closing quote, so each is doubled.
                    builder.Append('\\', backslashes * 2);
                }
                else if (argument[index] == '"')
                {
                    // Backslashes before a literal quote are doubled, then the quote is escaped.
                    builder.Append('\\', (backslashes * 2) + 1).Append('"');
                    index++;
                }
                else
                {
                    builder.Append('\\', backslashes);
                }
                continue;
            }
            if (character == '"')
            {
                builder.Append('\\').Append('"');
                continue;
            }
            builder.Append(character);
        }
        builder.Append('"');
    }
}
