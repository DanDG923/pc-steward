using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// ADR-035 rule 4 (UI review 2026-11-13, item 20): one icon image is taken out of a file's bytes, with every offset and count
/// checked. The files here are built in memory: an .ico file, and a program file (32-bit and 64-bit layout) with a resource table.
/// </summary>
[TestClass]
public sealed class AppIconFileTests
{
    private const uint ResourceAddress = 0x2000;
    private const int ResourceFileOffset = 0x200;

    private sealed record Image(int Pixels, ushort Bits, byte[] Data, ushort Id = 0);
    private sealed record Group(uint Id, bool Named, Image[] Images);

    private static byte[] Png(int length, byte fill)
    {
        byte[] data = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        Array.Fill(data, fill, 8, length - 8);
        return data;
    }

    private static byte[] Bitmap(int length, byte fill)
    {
        byte[] data = new byte[length];
        Array.Fill(data, fill);
        BinaryPrimitives.WriteUInt32LittleEndian(data, 40);
        return data;
    }

    private static byte[] IconFile(params Image[] images)
    {
        using MemoryStream file = new();
        using BinaryWriter writer = new(file);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Length);
        int offset = 6 + (images.Length * 16);
        foreach (Image image in images)
        {
            writer.Write((byte)(image.Pixels == 256 ? 0 : image.Pixels)); writer.Write((byte)(image.Pixels == 256 ? 0 : image.Pixels)); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write(image.Bits); writer.Write((uint)image.Data.Length); writer.Write((uint)offset);
            offset += image.Data.Length;
        }
        foreach (Image image in images) writer.Write(image.Data);
        return file.ToArray();
    }

    private static byte[] Single(Image image)
    {
        byte[] file = IconFile(image);
        Assert.AreEqual(22 + image.Data.Length, file.Length);
        return file;
    }

    /// <summary>
    /// A resource section: the root lists type 3 (icon images) and type 14 (icon groups); each has one directory per name and one
    /// language under it. A named group's entry has the high bit set in its name, as a real one does.
    /// </summary>
    private static byte[] Resources(Group[] groups)
    {
        Image[] images = [.. groups.SelectMany(group => group.Images).OrderBy(image => image.Id)];
        Group[] ordered = [.. groups.OrderBy(group => group.Named ? 0 : 1).ThenBy(group => group.Id)];
        // Layout: root, the two type directories, one language directory per image and group, then the data entries, then the data.
        int root = 0, imageDirectory = 16 + (2 * 8), groupDirectory = imageDirectory + 16 + (images.Length * 8), languages = groupDirectory + 16 + (ordered.Length * 8);
        int leaves = languages + ((images.Length + ordered.Length) * 24);
        int data = leaves + ((images.Length + ordered.Length) * 16);
        byte[][] blobs = [.. images.Select(image => image.Data), .. ordered.Select(GroupDirectory)];
        byte[] section = new byte[data + blobs.Sum(blob => blob.Length)];

        Directory(section, root, 0, 2);
        Entry(section, root + 16, 3, 0x80000000 | (uint)imageDirectory);
        Entry(section, root + 24, 14, 0x80000000 | (uint)groupDirectory);
        Directory(section, imageDirectory, 0, images.Length);
        Directory(section, groupDirectory, ordered.Count(group => group.Named), ordered.Count(group => !group.Named));
        int blobOffset = data;
        for (int item = 0; item < blobs.Length; item++)
        {
            bool isImage = item < images.Length;
            int language = languages + (item * 24), leaf = leaves + (item * 16);
            if (isImage) Entry(section, imageDirectory + 16 + (item * 8), images[item].Id, 0x80000000 | (uint)language);
            else
            {
                Group group = ordered[item - images.Length];
                Entry(section, groupDirectory + 16 + ((item - images.Length) * 8), group.Named ? 0x80000000 | group.Id : group.Id, 0x80000000 | (uint)language);
            }
            Directory(section, language, 0, 1);
            Entry(section, language + 16, 1033, (uint)leaf);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(leaf), ResourceAddress + (uint)blobOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(leaf + 4), (uint)blobs[item].Length);
            blobs[item].CopyTo(section, blobOffset);
            blobOffset += blobs[item].Length;
        }
        return section;
    }

    private static byte[] GroupDirectory(Group group)
    {
        byte[] directory = new byte[6 + (group.Images.Length * 14)];
        directory[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(4), (ushort)group.Images.Length);
        for (int item = 0; item < group.Images.Length; item++)
        {
            Image image = group.Images[item];
            int at = 6 + (item * 14);
            directory[at] = directory[at + 1] = (byte)(image.Pixels == 256 ? 0 : image.Pixels);
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(at + 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(at + 6), image.Bits);
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(at + 8), (uint)image.Data.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(at + 12), image.Id);
        }
        return directory;
    }

    private static void Directory(byte[] section, int at, int named, int ids)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(at + 12), (ushort)named);
        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(at + 14), (ushort)ids);
    }

    private static void Entry(byte[] section, int at, uint name, uint offset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(at), name);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(at + 4), offset);
    }

    /// <summary>A program file with one section that holds <paramref name="resources"/>, in the 32-bit or 64-bit header layout.</summary>
    private static byte[] ProgramFile(byte[] resources, bool is64Bit)
    {
        int optionalSize = (is64Bit ? 112 : 96) + (16 * 8);
        byte[] file = new byte[ResourceFileOffset + resources.Length];
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x3C), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(64), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(64 + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(64 + 20), (ushort)optionalSize);
        int optional = 64 + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optional), (ushort)(is64Bit ? 0x20B : 0x10B));
        int directories = optional + (is64Bit ? 112 : 96);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directories - 4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directories + 16), ResourceAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directories + 20), (uint)resources.Length);
        int section = optional + optionalSize;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(section + 8), (uint)resources.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(section + 12), ResourceAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(section + 16), (uint)resources.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(section + 20), ResourceFileOffset);
        resources.CopyTo(file, ResourceFileOffset);
        return file;
    }

    private static byte[]? Read(byte[] file, bool isIconFile, int index = 0, int wanted = 32)
    {
        using MemoryStream stream = new(file, writable: false);
        return AppIconFile.Read(stream, isIconFile, index, wanted);
    }

    private static readonly Image Small = new(16, 32, Bitmap(1128, 0x10), 1);
    private static readonly Image Medium = new(32, 32, Bitmap(4264, 0x20), 2);
    private static readonly Image Large = new(48, 32, Bitmap(9640, 0x30), 3);
    private static readonly Image Huge = new(256, 32, Png(20000, 0x40), 4);
    private static readonly Image FewColours = new(32, 8, Bitmap(2216, 0x50), 5);
    private static readonly Image Other = new(32, 32, Bitmap(4264, 0x60), 6);

    [TestMethod]
    [Description("ADR-035 rules 4 and 5: from an .ico file, the smallest image at least as large as wanted, else the largest; more colours first among equals; the result is a new single-image icon file.")]
    public void AnIconFileGivesOneImageNearestTheWantedSize()
    {
        byte[] file = IconFile(FewColours, Small, Huge, Medium, Large);
        CollectionAssert.AreEqual(Single(Medium), Read(file, isIconFile: true, wanted: 32), "32 wanted: the 32 image with full colour, not the 8-bit one");
        CollectionAssert.AreEqual(Single(Large), Read(file, isIconFile: true, wanted: 40), "40 wanted (125%): the 48 image");
        CollectionAssert.AreEqual(Single(Large), Read(file, isIconFile: true, wanted: 48));
        CollectionAssert.AreEqual(Single(Huge), Read(file, isIconFile: true, wanted: 64), "64 wanted (200%): the 256 image is the only one large enough");
        CollectionAssert.AreEqual(Single(Small), Read(IconFile(Small), isIconFile: true, wanted: 64), "nothing large enough: the largest there is");
        CollectionAssert.AreEqual(Single(Medium), Read(IconFile(Small, Medium), isIconFile: true, index: 7, wanted: 128), "an .ico file is one icon, whatever number follows it");

        byte[] single = Read(file, isIconFile: true, wanted: 64)!;
        Assert.AreEqual(0, single[6], "256 is written as 0, as in any icon file");
        Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(single.AsSpan(4)), "one image");
        Assert.AreEqual(22u, BinaryPrimitives.ReadUInt32LittleEndian(single.AsSpan(18)), "the image follows the 22-byte header");
    }

    [TestMethod]
    [Description("ADR-035 rule 4: from an .exe or .dll, the icon the number names: zero or more counts the icon groups in the file's order (named groups first), a negative number is a group's resource ID.")]
    public void AProgramFileGivesTheIconItsNumberNames()
    {
        Group[] groups = [new(101, false, [Small, Medium, Huge]), new(0x100, true, [Other]), new(205, false, [Large, FewColours])];
        foreach (bool is64Bit in new[] { false, true })
        {
            byte[] file = ProgramFile(Resources(groups), is64Bit);
            string layout = is64Bit ? "64-bit" : "32-bit";
            CollectionAssert.AreEqual(Single(Other), Read(file, false, index: 0), $"{layout}: number 0 is the first group, and named groups come first");
            CollectionAssert.AreEqual(Single(Medium), Read(file, false, index: 1), $"{layout}: number 1");
            CollectionAssert.AreEqual(Single(FewColours), Read(file, false, index: 2), $"{layout}: number 2, whose only 32 image has few colours");
            CollectionAssert.AreEqual(Single(Large), Read(file, false, index: 2, wanted: 48), $"{layout}: number 2 at 150%");
            CollectionAssert.AreEqual(Single(Medium), Read(file, false, index: -101), $"{layout}: resource ID 101");
            CollectionAssert.AreEqual(Single(Huge), Read(file, false, index: -101, wanted: 64), $"{layout}: resource ID 101 at 200%");
            CollectionAssert.AreEqual(Single(FewColours), Read(file, false, index: -205, wanted: 32), $"{layout}: resource ID 205");
            Assert.IsNull(Read(file, false, index: 3), $"{layout}: no fourth icon");
            Assert.IsNull(Read(file, false, index: -999), $"{layout}: no such resource ID");
            Assert.IsNull(Read(file, false, index: -0x100), $"{layout}: a named group is not a resource ID");
        }
    }

    [TestMethod]
    [Description("ADR-035 rule 4 and risk 3: a file that is not what it claims, is cut short, points outside itself or holds something that is not an icon image gives nothing, and never throws.")]
    public void ADamagedOrHostileFileGivesNothing()
    {
        byte[] icon = IconFile(Small, Medium);
        byte[] program = ProgramFile(Resources([new(1, false, [Small, Medium])]), is64Bit: true);
        Assert.IsNotNull(Read(icon, true));
        Assert.IsNotNull(Read(program, false));

        Assert.IsNull(Read([], true));
        Assert.IsNull(Read([], false));
        Assert.IsNull(Read(program, isIconFile: true), "a program is not an icon file");
        Assert.IsNull(Read(icon, isIconFile: false), "an icon file is not a program");
        Assert.IsNull(Read(icon, true, wanted: 0));

        // Cut short at every length: nothing, or a whole image that was entirely inside what is left.
        foreach (byte[] whole in new[] { icon, program })
            for (int length = 0; length < whole.Length; length += 7)
            {
                byte[]? result = Read(whole[..length], ReferenceEquals(whole, icon));
                if (result is not null) Assert.IsTrue(result.Length <= length + 22, $"cut at {length}");
            }

        // Every 4-byte field overwritten with a huge value, one at a time: nothing is read outside the file and nothing throws.
        foreach (byte[] whole in new[] { icon, program })
        {
            int end = ReferenceEquals(whole, icon) ? 6 + (2 * 16) : whole.Length - Small.Data.Length - Medium.Data.Length;
            for (int at = 0; at + 4 <= end; at += 2)
                foreach (uint value in new[] { 0xFFFFFFFFu, 0x7FFFFFFFu, 0x80000000u, (uint)whole.Length, 0u })
                {
                    byte[] damaged = (byte[])whole.Clone();
                    BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan(at), value);
                    byte[]? result = Read(damaged, ReferenceEquals(whole, icon));
                    if (result is not null) Assert.IsTrue(result.Length <= 22 + AppIconRules.MaximumImageBytes, $"field at {at}");
                }
        }

        Image notAnImage = new(32, 32, new byte[4264], 9);
        Assert.IsNull(Read(IconFile(notAnImage), true), "neither a PNG nor a bitmap header");
        Assert.IsNull(Read(ProgramFile(Resources([new(1, false, [notAnImage])]), false), false));
        CollectionAssert.AreEqual(Single(Small), Read(IconFile(notAnImage, Small), true), "the next best image is used");

        Image tooLarge = new(256, 32, Png(AppIconRules.MaximumImageBytes + 1, 0x70), 10);
        Assert.IsNull(Read(IconFile(tooLarge), true), "larger than the limit");
        Assert.IsNull(Read(ProgramFile(Resources([new(1, false, [tooLarge])]), true), false));
        Image atLimit = new(256, 32, Png(AppIconRules.MaximumImageBytes, 0x71), 11);
        Assert.AreEqual(22 + AppIconRules.MaximumImageBytes, Read(IconFile(atLimit), true)!.Length);

        byte[] tooMany = IconFile(Small);
        BinaryPrimitives.WriteUInt16LittleEndian(tooMany.AsSpan(4), 257);
        Assert.IsNull(Read(tooMany, true), "more images than any icon holds");
    }

    [TestMethod]
    [Description("ADR-035 rule 4: the file is only read through a stream that can seek; it is never asked for by path here.")]
    public void OnlyASeekableReadableStreamIsRead()
    {
        using MemoryStream closed = new(IconFile(Small));
        closed.Dispose();
        Assert.IsNull(AppIconFile.Read(closed, true, 0, 32));
        Assert.ThrowsExactly<ArgumentNullException>(() => AppIconFile.Read(null!, true, 0, 32));
    }
}
