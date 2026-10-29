using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PCSteward.ProcessFixture;

public static class ProcessFixtureMarker { }

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 ||
            !args[1].StartsWith("pcsteward-task49-", StringComparison.Ordinal) || args[1].Length > 100 ||
            args[1].Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) return 20;
        if (args[0] == "parent") return StartHolder(args[1]);
        if (args[0] != "holder") return 21;

        using CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
        using NamedPipeClientStream control = new(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await control.ConnectAsync(lifetime.Token).ConfigureAwait(false);
            byte[] command = new byte[1];
            while (await control.ReadAsync(command, lifetime.Token).ConfigureAwait(false) == 1)
            {
                if (command[0] == (byte)'q') return 0;
                if (command[0] == (byte)'w') { Console.Out.Write('x'); Console.Out.Flush(); }
                else if (command[0] == (byte)'e') { Console.Error.Write('x'); Console.Error.Flush(); }
                else return 22;
                await control.WriteAsync(command, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (IOException) { }
        return 0;
    }

    private static int StartHolder(string pipeName)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing fixture executable.");
        if (!string.Equals(Path.GetFileName(executable), "PCSteward.ProcessFixture.exe", StringComparison.OrdinalIgnoreCase)) return 23;
        StartupInfo startup = new()
        {
            Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x00000100,
            Input = GetStdHandle(-10), Output = GetStdHandle(-11), Error = GetStdHandle(-12)
        };
        char[] command = ($"\"{executable}\" holder {pipeName}\0").ToCharArray();
        if (!CreateProcessW(executable, command, nint.Zero, nint.Zero, true, 0x08000000,
            nint.Zero, null, ref startup, out ProcessInformation child))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(child.Thread);
        CloseHandle(child.Process);
        Console.WriteLine(child.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedBytes;
        public nint ReservedPointer, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, [In, Out] char[] command, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment,
        string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int kind);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
