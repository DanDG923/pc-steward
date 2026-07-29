using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: PCSteward.Desktop.exe <signed-helper-path>");
    return 2;
}

string helperPath = Path.GetFullPath(args[0]);
string pipeName = $"pcsteward-elevated-{Guid.NewGuid():N}";
string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
using NamedPipeServerStream pipe = new(
    pipeName,
    PipeDirection.InOut,
    1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

Process? helper;
try
{
    ProcessStartInfo start = new()
    {
        FileName = helperPath,
        UseShellExecute = true,
        Verb = "runas"
    };
    start.ArgumentList.Add("--protocol");
    start.ArgumentList.Add("1");
    start.ArgumentList.Add("--desktop-pid");
    start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    start.ArgumentList.Add("--pipe");
    start.ArgumentList.Add(pipeName);
    start.ArgumentList.Add("--nonce");
    start.ArgumentList.Add(nonce);
    helper = Process.Start(start);
}
catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
{
    Console.WriteLine("UAC-DENIED: elevation was declined; no helper process or provider job was started.");
    return 10;
}

if (helper is null)
{
    Console.Error.WriteLine("FAIL: no helper process was returned.");
    return 11;
}

int helperExitCode;
using (helper)
using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(35)))
{
    try
    {
        await pipe.WaitForConnectionAsync(timeout.Token);
        byte[] invalidRequest = "{}"u8.ToArray();
        byte[] length = BitConverter.GetBytes(invalidRequest.Length);
        await pipe.WriteAsync(length, timeout.Token);
        await pipe.WriteAsync(invalidRequest, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        await helper.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        if (!helper.HasExited)
        {
            helper.Kill(entireProcessTree: true);
        }
        Console.Error.WriteLine("FAIL: helper did not connect and terminate within 35 seconds.");
        return 12;
    }

    helperExitCode = helper.ExitCode;
}

if (helperExitCode != 24)
{
    Console.Error.WriteLine($"FAIL: helper exit {helperExitCode}; expected 24 (invalid request after authenticated connection).");
    return 13;
}

Console.WriteLine("PASS: UAC accepted; signed peer identity and one-shot pipe passed; malformed request exited 24 before any provider invocation.");
return 0;
