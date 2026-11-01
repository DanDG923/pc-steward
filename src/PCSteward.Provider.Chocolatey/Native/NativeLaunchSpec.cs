using System.Collections.ObjectModel;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// Immutable, validated input to the N0 launcher (Task 50 design rev 5 §4.1, rev-4 M1). Construction is private: in N0
/// the only factory is the test-only factory below, which an architecture test bans from every <c>src</c> caller. The
/// typed N3 wrapper builds specs from a held client binding and a sealed compiled command. The environment is the
/// complete child environment; it is never merged with the current process environment.
/// </summary>
internal sealed class NativeLaunchSpec
{
    private NativeLaunchSpec(string imagePath, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ImagePath = NativeCommandLine.ValidateImagePath(imagePath, nameof(imagePath));
        WorkingDirectory = NativeCommandLine.ValidateLocalPath(workingDirectory, nameof(workingDirectory));
        string[] copiedArguments = [.. arguments];
        if (copiedArguments.Any(static argument => argument is null || argument.Any(char.IsControl)))
            throw new ArgumentException("Arguments must be non-null and free of control characters.", nameof(arguments));
        Dictionary<string, string> copiedEnvironment = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in environment)
        {
            NativeCommandLine.ValidateVariable(name, value);
            if (!copiedEnvironment.TryAdd(name, value))
                throw new ArgumentException("Environment names must be unique ignoring case.", nameof(environment));
        }
        Arguments = new ReadOnlyCollection<string>(copiedArguments);
        Environment = new ReadOnlyDictionary<string, string>(copiedEnvironment);
        // Both native forms are checked once here so the launcher cannot meet an unformattable spec.
        _ = NativeCommandLine.Build(ImagePath, Arguments);
        _ = NativeCommandLine.BuildEnvironmentBlock(Environment);
    }

    internal string ImagePath { get; }
    internal IReadOnlyList<string> Arguments { get; }
    internal IReadOnlyDictionary<string, string> Environment { get; }
    internal string WorkingDirectory { get; }

    /// <summary>Test-only factory (rev-4 M1). Any <c>src</c> caller fails the architecture tests.</summary>
    internal static NativeLaunchSpec ForTests(string imagePath, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory) =>
        new(imagePath, arguments, environment, workingDirectory);
}
