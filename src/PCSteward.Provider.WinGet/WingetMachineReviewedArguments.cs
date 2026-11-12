using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Phase D stage D4b design §4 (owner answers 2026-11-12): the fixed command lines of the machine WinGet route, built only from a
/// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation and its <c>wgm1</c> evidence. Formatting only: the elevated
/// helper still checks the trusted client, source, settings, registration and manifest before it runs anything. Never adds
/// <c>--force</c>, <c>--ignore-security-hash</c>, <c>--accept-package-agreements</c>, <c>--include-unknown</c>, <c>--location</c>,
/// <c>--override</c>, <c>--custom</c>, <c>--allow-reboot</c> or <c>--skip-dependencies</c>.
/// </summary>
internal static class WingetMachineReviewedArguments
{
    /// <summary>
    /// <c>upgrade --id &lt;ID&gt; --version &lt;V&gt; --source winget --exact --scope machine --architecture &lt;a&gt;
    /// --installer-type &lt;t&gt; --disable-interactivity --silent</c>.
    /// </summary>
    internal static ReadOnlyCollection<string> BuildUpgrade(ReviewedUpdateOperation operation)
    {
        (ReviewedUpdateSelection selection, string architecture, string installerType) = Admit(operation);
        return Array.AsReadOnly(new[]
        {
            "upgrade", "--id", selection.PackageId, "--version", selection.CandidateVersion, "--source", "winget", "--exact",
            "--scope", "machine", "--architecture", architecture, "--installer-type", installerType, "--disable-interactivity", "--silent"
        });
    }

    /// <summary>
    /// D4b design §5 H7b / §7: <c>download</c> of the same reviewed installer into <paramref name="downloadDirectory"/> (a fresh
    /// administrator-only folder the helper created), so its exported manifest can be compared with the review.
    /// </summary>
    internal static ReadOnlyCollection<string> BuildDownload(ReviewedUpdateOperation operation, string downloadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        if (!Path.IsPathFullyQualified(downloadDirectory))
            throw new ArgumentException("The download folder must be a fully qualified path.", nameof(downloadDirectory));
        (ReviewedUpdateSelection selection, string architecture, string installerType) = Admit(operation);
        return Array.AsReadOnly(new[]
        {
            "download", "--id", selection.PackageId, "--version", selection.CandidateVersion, "--source", "winget", "--exact",
            "--scope", "machine", "--architecture", architecture, "--installer-type", installerType, "--download-directory",
            downloadDirectory, "--disable-interactivity"
        });
    }

    /// <summary>
    /// D4b stage 2b, design §7: the review's own <c>download</c> of a whole-PC selection into <paramref name="downloadDirectory"/> (a
    /// fresh folder the desktop created): the §4 line without <c>--installer-type</c>, so WinGet chooses the installer and the review
    /// records the type it chose. Built only for a selection with the route's shape; throws for anything else.
    /// </summary>
    internal static ReadOnlyCollection<string> BuildReviewDownload(UpdateJobSelection selection, string downloadDirectory)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        if (!Path.IsPathFullyQualified(downloadDirectory))
            throw new ArgumentException("The download folder must be a fully qualified path.", nameof(downloadDirectory));
        if (!WingetMachineReviewedRoute.TryGetSelectionArchitecture(selection, out string architecture))
            throw new ArgumentException("The selection is outside the machine WinGet route.", nameof(selection));
        return Array.AsReadOnly(new[]
        {
            "download", "--id", selection.PackageId, "--version", selection.CandidateVersion, "--source", "winget", "--exact",
            "--scope", "machine", "--architecture", architecture, "--download-directory", downloadDirectory, "--disable-interactivity"
        });
    }

    /// <summary>The shape every machine command line requires (<see cref="WingetMachineReviewedRoute.TryGetShape"/>); throws for anything else.</summary>
    internal static (ReviewedUpdateSelection Selection, string Architecture, string InstallerType) Admit(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!WingetMachineReviewedRoute.TryGetShape(operation, out _, out string architecture, out string installerType))
            throw new ArgumentException("The reviewed binding is unavailable for the machine WinGet mapping.", nameof(operation));
        return (operation.Selection, architecture, installerType);
    }
}

/// <summary>
/// D4b design §5 H7 and §6 S2 (owner accepted the residual race 2026-11-12): WinGet reads the owner's own <c>settings.json</c> even
/// when it runs elevated. The helper refuses the run unless every setting present is on a small allow-list that cannot change what
/// is installed, from where, or how: <c>$schema</c>, <c>visual</c>, <c>telemetry</c>, <c>logging</c>, <c>output</c>,
/// <c>interactivity</c> (the command line disables it anyway) and <c>source.autoUpdateIntervalInMinutes</c>. A missing file is
/// the default settings and passes. The file is read bounded; comments and trailing commas are accepted because WinGet accepts
/// them. Anything unreadable, oversized, malformed or outside the list refuses.
/// </summary>
internal static class WingetUserSettingsPolicy
{
    /// <summary>Settings files larger than this are refused.</summary>
    internal const int MaximumBytes = 64 * 1024;

    private static readonly string[] AllowedTopLevel = ["$schema", "visual", "telemetry", "logging", "output", "interactivity", "source"];
    private static readonly string[] AllowedSource = ["autoUpdateIntervalInMinutes"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>True when the bytes hold only allow-listed settings. An empty file (WinGet treats it as defaults) passes.</summary>
    internal static bool Allows(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaximumBytes) return false;
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (content.StartsWith(bom)) content = content[bom.Length..];
        try
        {
            string text = StrictUtf8.GetString(content);
            if (string.IsNullOrWhiteSpace(text)) return true;
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 8
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !AllowedTopLevel.Contains(property.Name, StringComparer.Ordinal)) return false;
                if (property.Name == "source" && !SourceAllowed(property.Value)) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool SourceAllowed(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in source.EnumerateObject())
            if (!seen.Add(property.Name) || !AllowedSource.Contains(property.Name, StringComparer.Ordinal)) return false;
        return true;
    }
}
