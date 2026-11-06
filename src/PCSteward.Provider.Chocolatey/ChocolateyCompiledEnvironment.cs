using System.Collections.ObjectModel;
using System.Diagnostics;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Passive native-path formatting inputs. Native provenance, protection and continued identity must be verified separately.</summary>
internal sealed record ChocolateyLaunchPaths(string WindowsRoot, string ProgramFiles, string ProgramFilesX86,
    string UserProfile, string RoamingAppData, string LocalAppData, string ChocolateyRoot, string ProtectedCache);

/// <summary>Compiled bootstrap environment only; does not resolve paths, qualify a client, grant approval or start a process.</summary>
internal static class ChocolateyCompiledEnvironment
{
    internal static IReadOnlyDictionary<string, string> Create(ChocolateyLaunchPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string windows = ValidatePath(paths.WindowsRoot);
        string programFiles = ValidatePath(paths.ProgramFiles);
        string programFilesX86 = ValidatePath(paths.ProgramFilesX86);
        string profile = ValidatePath(paths.UserProfile);
        string roaming = ValidatePath(paths.RoamingAppData);
        string local = ValidatePath(paths.LocalAppData);
        string root = ValidatePath(paths.ChocolateyRoot);
        string cache = ValidatePath(paths.ProtectedCache);
        string system = Path.Combine(windows, "System32");
        string powerShell = Path.Combine(system, "WindowsPowerShell", "v1.0");
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows, ["WINDIR"] = windows, ["SystemDrive"] = Path.GetPathRoot(windows)!.TrimEnd('\\'),
            ["ComSpec"] = Path.Combine(system, "cmd.exe"),
            ["PATH"] = string.Join(';', system, windows, Path.Combine(system, "Wbem"), powerShell),
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
            ["ProgramFiles"] = programFiles, ["ProgramW6432"] = programFiles, ["ProgramFiles(x86)"] = programFilesX86,
            ["USERPROFILE"] = profile, ["APPDATA"] = roaming, ["LOCALAPPDATA"] = local,
            ["ChocolateyInstall"] = root, ["TEMP"] = cache, ["TMP"] = cache,
            ["PSModulePath"] = string.Join(';', Path.Combine(programFiles, "WindowsPowerShell", "Modules"), Path.Combine(powerShell, "Modules")),
            ["PROCESSOR_ARCHITECTURE"] = "AMD64"
        };
        return new ReadOnlyDictionary<string, string>(environment);
    }

    internal static void ApplyTo(ProcessStartInfo start, ChocolateyLaunchPaths paths)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute) throw new ArgumentException("The fixed bootstrap environment requires direct process creation.", nameof(start));
        IReadOnlyDictionary<string, string> compiled = Create(paths);
        start.Environment.Clear();
        foreach ((string key, string value) in compiled) start.Environment.Add(key, value);
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length is < 3 or > 4096 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path[2] != '\\' || path.Any(character => char.IsControl(character) || ";\"%<>|?*/".Contains(character)) ||
            path.AsSpan(2).Contains(':') || path.Split('\\').Any(part => part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("The native path is not a supported unambiguous local path.", nameof(path));
        string normalized = Path.GetFullPath(path).TrimEnd('\\');
        if (!string.Equals(path.TrimEnd('\\'), normalized, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The native path must already be canonical.", nameof(path));
        return normalized.Length == 2 ? normalized + '\\' : normalized;
    }
}
