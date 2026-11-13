using System.Buffers.Binary;
using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Where an app's own icon may be read from (ADR-035 rule 2).</summary>
public enum AppIconSourceKind
{
    /// <summary>The <c>DisplayIcon</c> value of the uninstall entry the scan recorded for a classic app.</summary>
    ClassicEntry,
    /// <summary>The logo Windows' package API returns for a Store-type app.</summary>
    StorePackage
}

/// <summary>
/// The one place an app's icon is read from. For a classic app <paramref name="Identity"/> is the name of its uninstall entry,
/// under the current user's hive (<paramref name="CurrentUser"/>) or the machine hive in its 32-bit or 64-bit view
/// (<paramref name="View32"/>). For a Store-type app it is the package family name.
/// </summary>
public sealed record AppIconSource(AppIconSourceKind Kind, string Identity, bool CurrentUser = false, bool View32 = false)
{
    /// <summary>Names this source in the session's in-memory icon cache. Never stored.</summary>
    public string CacheKey => $"{(Kind == AppIconSourceKind.StorePackage ? "store" : CurrentUser ? "user" : View32 ? "machine32" : "machine64")}|{Identity}";
}

/// <summary>
/// ADR-035 (accepted, option A; UI review 2026-11-13 item 20; FR-UI-01, FR-DAT-12): the rules for showing an app's own icon.
/// Portable and deterministic so every refusal is unit-tested. Nothing here opens a file, reads the registry or keeps anything:
/// the desktop app does that, for rows on screen, and falls back to the two-letter tile whenever a rule says no.
/// </summary>
public static class AppIconRules
{
    /// <summary>ADR-035: what About's privacy text says about the icons.</summary>
    public const string PrivacyNote = "The icons in the lists come from each app's own files on this PC, read when a list is shown. They aren't saved and nothing is sent anywhere.";
    /// <summary>The tile's side in layout units; the icon is read at this size times the display's scale.</summary>
    public const double TileSize = 32;
    /// <summary>The only extra registry value read, from the uninstall entry the scan recorded.</summary>
    public const string RegistryValueName = "DisplayIcon";
    public const int MaximumValueLength = 1024;
    /// <summary>The largest single icon image handed to the decoder. A 256 x 256 image with full colour is about 270 KB.</summary>
    public const int MaximumImageBytes = 1024 * 1024;
    /// <summary>Icons kept in memory for the session (rule 6). Past this, further icons are read again when shown.</summary>
    public const int MaximumCachedIcons = 512;
    /// <summary>Icon reads that may run at once, off the UI thread (rule 5).</summary>
    public const int MaximumConcurrentReads = 4;
    /// <summary>A read slower than this is abandoned and the tile stays (rule 5).</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private static readonly string[] AllowedExtensions = [".ico", ".exe", ".dll"];
    private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
    // Files Windows fetches from somewhere else when they are opened (cloud placeholders): opening one could reach the network.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>
    /// The source for an app's icon, or <see langword="null"/> when its records name none: a Store-type app's package family
    /// first, otherwise the uninstall entry of its classic record. Records from WinGet or Chocolatey name no icon.
    /// </summary>
    public static AppIconSource? SourceFor(IEnumerable<InstallationInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        InstallationInstance[] ordered = [.. instances.OrderBy(instance => instance.ProviderIdentity, StringComparer.Ordinal)];
        InstallationInstance? store = ordered.FirstOrDefault(instance => IsProvider(instance, "msix", "appx") && PackageFamilyName.IsValid(instance.ProviderIdentity));
        if (store is not null) return new AppIconSource(AppIconSourceKind.StorePackage, store.ProviderIdentity.Trim());
        InstallationInstance? classic = ordered.FirstOrDefault(instance => IsProvider(instance, "registry") && IsEntryName(instance.ProviderIdentity));
        if (classic is null) return null;
        bool currentUser = string.Equals(classic.Scope, "User", StringComparison.OrdinalIgnoreCase);
        return new AppIconSource(AppIconSourceKind.ClassicEntry, classic.ProviderIdentity, currentUser, !currentUser && string.Equals(classic.Architecture, "x86", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The icon's side in pixels at the display's scale (100% to 400%).</summary>
    public static int PixelSize(double rasterizationScale) =>
        (int)Math.Ceiling(TileSize * (double.IsFinite(rasterizationScale) ? Math.Clamp(rasterizationScale, 1, 4) : 1));

    /// <summary>
    /// An uninstall entry's own name: one registry key name, so it can never address another key. The scan recorded it; it is
    /// still checked because it comes back from the database.
    /// </summary>
    public static bool IsEntryName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && !name.Contains('\\') && !name.Any(char.IsControl);

    /// <summary>
    /// Reads a <c>DisplayIcon</c> value: a file path, optionally in quotes, optionally followed by a comma and an icon number
    /// (<c>"C:\Program Files\App\app.exe",0</c>). A negative number names an icon by its resource ID. Returns false, and the
    /// tile stays, unless the path passes <see cref="IsAllowedPath"/>.
    /// </summary>
    public static bool TryParseDisplayIcon(string? value, out string path, out int index)
    {
        path = string.Empty;
        index = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        if (text.Length > MaximumValueLength || text.Any(char.IsControl)) return false;
        text = Unquote(text);
        int comma = text.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(text.AsSpan(comma + 1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
        {
            if (parsed == int.MinValue) return false;
            index = parsed;
            text = Unquote(text[..comma].TrimEnd());
        }
        if (!IsAllowedPath(text)) { index = 0; return false; }
        path = text;
        return true;
    }

    /// <summary>
    /// ADR-035 rule 3, the part that needs no file system: a full path on a lettered drive to an .ico, .exe or .dll file.
    /// Refused: network paths (<c>\\server\share</c>), device paths (<c>\\?\</c>, <c>\\.\</c>), relative paths and <c>..</c>
    /// segments, a stream name (a second colon), an environment variable (<c>%</c>), forward slashes, wildcards, device names
    /// (<c>NUL</c>, <c>COM1</c>) and segments Windows would rewrite (a trailing dot or space). Whether the drive is a fixed local
    /// drive, and whether the path passes through a link, is checked by the desktop app before the file is opened.
    /// </summary>
    public static bool IsAllowedPath(string? path)
    {
        if (path is null || path.Length < 4 || path.Length > MaximumValueLength) return false;
        if (!char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') return false;
        foreach (char character in path.AsSpan(2))
            if (char.IsControl(character) || character is ':' or '/' or '%' or '<' or '>' or '"' or '|' or '?' or '*') return false;
        string[] segments = path[3..].Split('\\');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment[^1] is ' ' or '.' || segment[0] == ' ') return false;
            int dot = segment.IndexOf('.', StringComparison.Ordinal);
            string stem = (dot < 0 ? segment : segment[..dot]).TrimEnd();
            if (DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return false;
        }
        string file = segments[^1];
        int extension = file.LastIndexOf('.');
        return extension > 0 && AllowedExtensions.Contains(file[extension..], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True for an <c>.ico</c> file; an <c>.exe</c> or <c>.dll</c> holds its icons as resources instead.</summary>
    public static bool IsIconFile(string path) => path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);

    /// <summary>The drive root of an allowed path (<c>C:\</c>), which the desktop app asks Windows the type of.</summary>
    public static string DriveRoot(string allowedPath) => allowedPath[..3];

    /// <summary>ADR-035 rule 3: only a fixed local drive. A mapped network drive, a removable drive and a disc are refused.</summary>
    public static bool IsAllowedDrive(DriveType type) => type == DriveType.Fixed;

    /// <summary>
    /// Each folder on the way to an allowed path and then the path itself, shortest first (<c>C:\A</c>, <c>C:\A\B</c>,
    /// <c>C:\A\B\app.exe</c>). The desktop app checks each one with <see cref="MayPassThrough"/> before it opens the file.
    /// </summary>
    public static IReadOnlyList<string> PathSteps(string allowedPath)
    {
        ArgumentNullException.ThrowIfNull(allowedPath);
        List<string> steps = [];
        for (int separator = allowedPath.IndexOf('\\', 3); separator >= 0; separator = allowedPath.IndexOf('\\', separator + 1)) steps.Add(allowedPath[..separator]);
        steps.Add(allowedPath);
        return steps;
    }

    /// <summary>
    /// ADR-035 rule 3, links: a path that passes through a link of any kind (junction, symbolic link, cloud placeholder) is
    /// refused rather than followed, because a link can lead off this PC. Every step but the last must be a plain folder and the
    /// last a plain file that is on this disk.
    /// </summary>
    public static bool MayPassThrough(FileAttributes attributes, bool isLastStep)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Device | RecallOnOpen | RecallOnDataAccess)) != 0) return false;
        bool isFolder = (attributes & FileAttributes.Directory) != 0;
        return isLastStep ? !isFolder : isFolder;
    }

    private static bool IsProvider(InstallationInstance instance, params string[] providers) =>
        providers.Contains(instance.Provider, StringComparer.OrdinalIgnoreCase);

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1].Trim() : text;
}

/// <summary>
/// ADR-035 rule 4: takes one icon image out of a file's bytes. The file is only ever read as data through the stream it is
/// given: it is never run, never loaded as code and never handed to the Windows shell, so no other program's icon handler is
/// involved. Every offset and count in the file is checked against the file's length and a fixed limit before it is used, at
/// most three levels of the resource table are followed, and the result is a new single-image icon file of at most
/// <see cref="AppIconRules.MaximumImageBytes"/>, which is all the image decoder is given.
/// </summary>
public static class AppIconFile
{
    private const int MaximumDirectoryEntries = 4096;
    private const int MaximumImagesInGroup = 256;
    private const int MaximumSections = 96;
    private const int MaximumCandidatesTried = 4;
    private const uint GroupIconType = 14;
    private const uint IconType = 3;
    private const uint SubdirectoryFlag = 0x80000000;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly record struct Candidate(byte Width, byte Height, byte ColorCount, ushort Planes, ushort BitCount, uint Size, uint Locator)
    {
        public int Pixels => Width == 0 ? 256 : Width;
    }
    private readonly record struct Section(uint VirtualAddress, uint VirtualSize, uint RawSize, uint RawPointer);
    private readonly record struct DirectoryEntry(uint Name, uint Offset);

    /// <summary>
    /// A single-image <c>.ico</c> file holding the image closest to <paramref name="wantedPixels"/>, or <see langword="null"/>
    /// when the file holds no usable icon. <paramref name="index"/> is the icon number from <c>DisplayIcon</c>: zero or more
    /// counts the file's icons in order, a negative number is an icon's resource ID. An <c>.ico</c> file holds one icon, so the
    /// number is not used for it.
    /// </summary>
    public static byte[]? Read(Stream stream, bool isIconFile, int index, int wantedPixels)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || wantedPixels <= 0) return null;
        try { return isIconFile ? ReadIconFile(stream, wantedPixels) : ReadProgramFile(stream, index, wantedPixels); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or OverflowException) { return null; }
    }

    private static byte[]? ReadIconFile(Stream stream, int wantedPixels)
    {
        if (Bytes(stream, 0, 6) is not { } header || U16(header, 0) != 0 || U16(header, 2) != 1) return null;
        int count = U16(header, 4);
        if (count is < 1 or > MaximumImagesInGroup || Bytes(stream, 6, count * 16) is not { } entries) return null;
        List<Candidate> candidates = [];
        for (int entry = 0; entry < count; entry++)
        {
            int at = entry * 16;
            candidates.Add(new Candidate(entries[at], entries[at + 1], entries[at + 2], U16(entries, at + 4), U16(entries, at + 6), U32(entries, at + 8), U32(entries, at + 12)));
        }
        foreach (Candidate candidate in Preferred(candidates, wantedPixels))
            if (Bytes(stream, candidate.Locator, (int)candidate.Size) is { } image && IsImage(image)) return SingleImageIcon(candidate, image);
        return null;
    }

    private static byte[]? ReadProgramFile(Stream stream, int index, int wantedPixels)
    {
        // The headers: "MZ", then the "PE" header it points to, then the table of sections and where the resources are.
        if (Bytes(stream, 0, 64) is not { } dos || dos[0] != (byte)'M' || dos[1] != (byte)'Z') return null;
        long pe = U32(dos, 0x3C);
        if (Bytes(stream, pe, 24) is not { } coff || U32(coff, 0) != 0x00004550) return null;
        int sectionCount = U16(coff, 6);
        int optionalSize = U16(coff, 20);
        if (sectionCount is < 1 or > MaximumSections || Bytes(stream, pe + 24, optionalSize) is not { } optional || optional.Length < 2) return null;
        int directories = U16(optional, 0) switch { 0x10B => 96, 0x20B => 112, _ => -1 };
        if (directories < 0 || optional.Length < directories + 24 || U32(optional, directories - 4) < 3) return null;
        uint resourceAddress = U32(optional, directories + 16);
        if (resourceAddress == 0 || Bytes(stream, pe + 24 + optionalSize, sectionCount * 40) is not { } table) return null;
        Section[] sections = new Section[sectionCount];
        for (int section = 0; section < sectionCount; section++)
        {
            int at = section * 40;
            sections[section] = new Section(U32(table, at + 12), U32(table, at + 8), U32(table, at + 16), U32(table, at + 20));
        }
        if (FileOffset(sections, resourceAddress) is not { } root) return null;

        // Type (icon group) -> the icon the number names -> its first language -> the group's list of images.
        if (Subdirectory(stream, root, Find(stream, root, GroupIconType)) is not { } groups) return null;
        DirectoryEntry? named = index >= 0 ? Nth(stream, groups, index) : Find(stream, groups, (uint)(-(long)index));
        if (Data(stream, root, sections, First(stream, Subdirectory(stream, root, named)), 6 + (MaximumImagesInGroup * 14)) is not { Length: >= 6 } group) return null;
        if (U16(group, 0) != 0 || U16(group, 2) != 1) return null;
        int count = U16(group, 4);
        if (count is < 1 or > MaximumImagesInGroup || group.Length < 6 + (count * 14)) return null;
        List<Candidate> candidates = [];
        for (int entry = 0; entry < count; entry++)
        {
            int at = 6 + (entry * 14);
            candidates.Add(new Candidate(group[at], group[at + 1], group[at + 2], U16(group, at + 4), U16(group, at + 6), U32(group, at + 8), U16(group, at + 12)));
        }

        // Type (icon image) -> the image the group names by ID -> its first language -> the image itself.
        if (Subdirectory(stream, root, Find(stream, root, IconType)) is not { } images) return null;
        foreach (Candidate candidate in Preferred(candidates, wantedPixels))
        {
            byte[]? image = Data(stream, root, sections, First(stream, Subdirectory(stream, root, Find(stream, images, candidate.Locator))), AppIconRules.MaximumImageBytes);
            if (image is not null && IsImage(image)) return SingleImageIcon(candidate with { Size = (uint)image.Length }, image);
        }
        return null;
    }

    /// <summary>The smallest image at least as large as wanted, else the largest; more colours first among equals. A few are tried.</summary>
    private static IEnumerable<Candidate> Preferred(IEnumerable<Candidate> candidates, int wantedPixels) =>
        candidates.Where(candidate => candidate.Size is > 0 and <= AppIconRules.MaximumImageBytes)
            .OrderBy(candidate => candidate.Pixels >= wantedPixels ? 0 : 1)
            .ThenBy(candidate => candidate.Pixels >= wantedPixels ? candidate.Pixels : -candidate.Pixels)
            .ThenByDescending(candidate => candidate.BitCount)
            .Take(MaximumCandidatesTried);

    /// <summary>An icon image is a PNG file or a bitmap that starts with a 40-byte header. Anything else is not handed on.</summary>
    private static bool IsImage(byte[] image) =>
        (image.Length > PngSignature.Length && image.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)) || (image.Length > 40 && U32(image, 0) == 40);

    private static byte[] SingleImageIcon(Candidate candidate, byte[] image)
    {
        byte[] file = new byte[22 + image.Length];
        file[2] = 1;
        file[4] = 1;
        file[6] = candidate.Width;
        file[7] = candidate.Height;
        file[8] = candidate.ColorCount;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10), candidate.Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), candidate.BitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), (uint)image.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 22);
        image.CopyTo(file, 22);
        return file;
    }

    private static DirectoryEntry[]? Entries(Stream stream, long directory, out int namedCount)
    {
        namedCount = 0;
        if (Bytes(stream, directory, 16) is not { } header) return null;
        namedCount = U16(header, 12);
        int total = namedCount + U16(header, 14);
        if (total is < 1 or > MaximumDirectoryEntries || Bytes(stream, directory + 16, total * 8) is not { } raw) return null;
        DirectoryEntry[] entries = new DirectoryEntry[total];
        for (int entry = 0; entry < total; entry++) entries[entry] = new DirectoryEntry(U32(raw, entry * 8), U32(raw, (entry * 8) + 4));
        return entries;
    }

    /// <summary>The entry with this numeric ID. Entries with a text name come first in a directory and are never an ID.</summary>
    private static DirectoryEntry? Find(Stream stream, long directory, uint id)
    {
        if (Entries(stream, directory, out int named) is not { } entries) return null;
        for (int entry = named; entry < entries.Length; entry++)
            if (entries[entry].Name == id) return entries[entry];
        return null;
    }

    private static DirectoryEntry? Nth(Stream stream, long directory, int index) =>
        Entries(stream, directory, out _) is { } entries && index < entries.Length ? entries[index] : null;

    private static DirectoryEntry? First(Stream stream, long? directory) =>
        directory is { } at && Entries(stream, at, out _) is { } entries ? entries[0] : null;

    private static long? Subdirectory(Stream stream, long root, DirectoryEntry? entry)
    {
        if (entry is not { } found || (found.Offset & SubdirectoryFlag) == 0) return null;
        long at = root + (found.Offset & ~SubdirectoryFlag);
        return at < stream.Length ? at : null;
    }

    private static byte[]? Data(Stream stream, long root, Section[] sections, DirectoryEntry? entry, int maximumBytes)
    {
        if (entry is not { } found || (found.Offset & SubdirectoryFlag) != 0 || Bytes(stream, root + found.Offset, 16) is not { } leaf) return null;
        uint size = U32(leaf, 4);
        return size == 0 || size > maximumBytes || FileOffset(sections, U32(leaf, 0)) is not { } at ? null : Bytes(stream, at, (int)size);
    }

    /// <summary>Where an address in the loaded program sits in the file, when it is inside a section's bytes on disk.</summary>
    private static long? FileOffset(Section[] sections, uint address)
    {
        foreach (Section section in sections)
        {
            if (address < section.VirtualAddress) continue;
            uint within = address - section.VirtualAddress;
            if (within < section.RawSize && within < Math.Max(section.VirtualSize, section.RawSize)) return (long)section.RawPointer + within;
        }
        return null;
    }

    private static byte[]? Bytes(Stream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > stream.Length - count) return null;
        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
}
