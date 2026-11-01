#Requires -Version 5.1
<#
  Task 50 host-evidence probe for O5, A-Q1 and A2-L3 -- revision 2.
  Plan: docs/review/TASK_50_HOST_EVIDENCE_PLAN.md (revision 2). Approval binds the CRLF file SHA-256 recorded in plan section 4.
  Run and Cleanup write only inside %TEMP%\pcsteward-evidence-task50. SelfCheck writes only compiler output under -ScratchDirectory.
  Never runs choco.exe or any PC Steward binary. No registry writes, ACL changes, installs, network calls or elevation
  (except the separately approved Uac case). Pure ASCII.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Run', 'Cleanup', 'SelfCheck')][string]$Mode,
    [ValidateSet('Default', 'Uac', 'PcStewardHolds')][string]$Cases = 'Default',
    [string]$ScratchDirectory   # SelfCheck only: caller-supplied, existing, empty directory for compiler output
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Process scope only: use Windows PowerShell's own modules even when launched from PowerShell 7, which passes a module
# path where 5.1 cannot auto-load Get-FileHash, ConvertTo-Json and similar (same guard as the approved redeploy script).
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')

function Stop-Probe([string]$Reason) { throw ('STOP: ' + $Reason) }

$deadlineSeconds = @{ Run = @{ Default = 240; Uac = 300; PcStewardHolds = 60 }[$Cases]; Cleanup = 60; SelfCheck = 150 }[$Mode]
$clock = [Diagnostics.Stopwatch]::StartNew()
function Assert-Deadline { if ($clock.Elapsed.TotalSeconds -gt $deadlineSeconds) { Stop-Probe 'overall-deadline' } }

$nativeSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

public sealed class T50Open
{
    public SafeFileHandle Handle;
    public int Error;
    public bool Ok { get { return Handle != null && !Handle.IsInvalid; } }
}

public sealed class T50File
{
    public bool Ok;
    public string Failure;
    public int Error;
    public long Length;
    public string Sha256;
    public string FileId;
    public bool Reparse;
}

public sealed class T50Child
{
    public IntPtr Process;
    public IntPtr Thread;
    public int Error;
    public bool Ok { get { return Process != IntPtr.Zero; } }
}

public static class T50
{
    public const uint GenericRead = 0x80000000;
    public const uint FileReadAttributes = 0x00000080;
    public const uint ShareRead = 1, ShareAll = 7;
    public const uint FlagBackup = 0x02000000, FlagOpenReparse = 0x00200000;
    public const uint FileWriteData = 0x2, FileAppendData = 0x4, FileWriteEa = 0x10, FileDeleteChild = 0x40;
    public const uint FileWriteAttributes = 0x100, Delete = 0x10000, WriteDac = 0x40000, WriteOwner = 0x80000;
    // Exact copies of the ProtectedLaunchLayout lists in WindowsElevatedHelperTransport.cs (pinned in the plan).
    public static readonly uint[] FileModificationRights = { FileWriteData, FileAppendData, FileWriteEa, FileWriteAttributes, Delete, WriteDac, WriteOwner };
    public static readonly uint[] DirectoryModificationRights = { FileWriteData, FileAppendData, FileDeleteChild, Delete, WriteDac, WriteOwner };
    public static readonly uint[] AncestorReplacementRights = { FileDeleteChild, Delete, WriteDac, WriteOwner };

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo { public ulong Volume; public ulong IdLow; public ulong IdHigh; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars;
        public int dwFillAttribute; public int dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public string lpVerb; public string lpFile;
        public string lpParameters; public string lpDirectory; public int nShow; public IntPtr hInstApp; public IntPtr lpIDList;
        public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIconOrMonitor; public IntPtr hProcess;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int infoClass, out FileIdInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle h, StringBuilder volumeName, uint volumeNameSize, IntPtr serial, IntPtr maxComponent, IntPtr flags, StringBuilder fsName, uint fsNameSize);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo si, out ProcessInformation pi);
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr p, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr p, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value, int length, out int returned);

    public static T50Open Open(string path, uint access, uint share, uint flags)
    {
        SafeFileHandle h = CreateFileW(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        T50Open r = new T50Open();
        if (h.IsInvalid) { r.Error = Marshal.GetLastWin32Error(); h.Dispose(); } else { r.Handle = h; }
        return r;
    }

    // Opens and immediately closes the handle. Never reads, writes, renames or deletes content.
    public static string ProbeOpen(string path, uint access, bool directory)
    {
        T50Open o = Open(path, access, ShareAll, FlagOpenReparse | (directory ? FlagBackup : 0u));
        if (o.Ok) { o.Handle.Dispose(); return "handle"; }
        return o.Error.ToString();
    }

    // Mirrors ProtectedLaunchLayout.TryProbeRights (share RWD, OPEN_EXISTING, OPEN_REPARSE_POINT, BACKUP for
    // directories). The verdict follows the first decisive right exactly as TryProbeRights; every right is recorded.
    public static string[] ProbeRights(string path, bool directory, uint[] rights, out string verdict)
    {
        List<string> outcomes = new List<string>();
        verdict = null;
        foreach (uint right in rights)
        {
            string outcome = ProbeOpen(path, right, directory);
            outcomes.Add(right.ToString("X8") + "=" + outcome);
            if (verdict == null && outcome == "handle") verdict = "can-modify";
            else if (verdict == null && outcome != "5") verdict = "probe-failed-closed:" + outcome;
        }
        if (verdict == null) verdict = "cannot-modify";
        return outcomes.ToArray();
    }

    public static string FileIdOf(SafeFileHandle h, out int error)
    {
        FileIdInfo info;
        if (!GetFileInformationByHandleEx(h, 18, out info, (uint)Marshal.SizeOf(typeof(FileIdInfo))))
        { error = Marshal.GetLastWin32Error(); return null; }
        error = 0;
        byte[] id = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(info.IdLow), 0, id, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(info.IdHigh), 0, id, 8, 8);
        return info.Volume.ToString("X16") + ":" + BitConverter.ToString(id).Replace("-", "");
    }

    public static string FileSystemOf(SafeFileHandle h)
    {
        StringBuilder fs = new StringBuilder(261);
        return GetVolumeInformationByHandleW(h, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fs, 261) ? fs.ToString() : null;
    }

    // GENERIC_READ + FILE_SHARE_READ (the desktop read and HeldHelperBinding shape). Records identity and either a
    // full SHA-256 or a bounded tail read. Content is never retained or written anywhere.
    public static T50File ReadFile(string path, long maxBytes, bool fullHash, int tailBytes)
    {
        T50File r = new T50File();
        try { r.Reparse = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) { r.Failure = "attributes"; r.Error = ex.HResult & 0xFFFF; return r; }
        T50Open o = Open(path, GenericRead, ShareRead, FlagOpenReparse);
        if (!o.Ok) { r.Failure = "open"; r.Error = o.Error; return r; }
        try
        {
            int e;
            r.FileId = FileIdOf(o.Handle, out e);
            if (r.FileId == null) { r.Failure = "file-id"; r.Error = e; return r; }
            using (FileStream fs = new FileStream(o.Handle, FileAccess.Read, 65536))
            {
                r.Length = fs.Length;
                if (r.Length > maxBytes) { r.Failure = "over-bound"; return r; }
                if (fullHash)
                {
                    using (SHA256 sha = SHA256.Create()) { r.Sha256 = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", ""); }
                }
                else if (r.Length > 0)
                {
                    int n = (int)Math.Min((long)tailBytes, r.Length);
                    fs.Seek(-n, SeekOrigin.End);
                    byte[] buffer = new byte[n];
                    int total = 0;
                    while (total < n) { int k = fs.Read(buffer, total, n - total); if (k == 0) break; total += k; }
                    if (total != n) { r.Failure = "short-read"; return r; }
                }
            }
            r.Ok = true;
            return r;
        }
        catch (Exception ex) { r.Failure = "read"; r.Error = ex.HResult & 0xFFFF; return r; }
        finally { o.Handle.Dispose(); }
    }

    // Direct process creation of a fixed synthetic image; the command line is built only from compiled constants.
    public static T50Child Start(string exe, string arguments, string directory, bool suspended)
    {
        StartupInfo si = new StartupInfo();
        si.cb = Marshal.SizeOf(typeof(StartupInfo));
        ProcessInformation pi;
        StringBuilder cmd = new StringBuilder("\"" + exe + "\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
        uint flags = 0x08000000u | (suspended ? 0x4u : 0u); // CREATE_NO_WINDOW | CREATE_SUSPENDED
        T50Child c = new T50Child();
        if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, directory, ref si, out pi))
        { c.Error = Marshal.GetLastWin32Error(); return c; }
        c.Process = pi.hProcess; c.Thread = pi.hThread;
        return c;
    }

    // Used only by the separately approved Uac case.
    public static T50Child RunAs(string exe, string arguments, string directory)
    {
        ShellExecuteInfo info = new ShellExecuteInfo();
        info.cbSize = Marshal.SizeOf(typeof(ShellExecuteInfo));
        info.fMask = 0x00000040u | 0x00000100u; // SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC
        info.lpVerb = "runas"; info.lpFile = exe; info.lpParameters = arguments; info.lpDirectory = directory; info.nShow = 0;
        T50Child c = new T50Child();
        if (!ShellExecuteExW(ref info)) { c.Error = Marshal.GetLastWin32Error(); return c; }
        c.Process = info.hProcess;
        if (c.Process == IntPtr.Zero) c.Error = -1;
        return c;
    }

    public static int Resume(T50Child c) { return ResumeThread(c.Thread) == 0xFFFFFFFFu ? Marshal.GetLastWin32Error() : 0; }

    public static string WaitExit(T50Child c, uint milliseconds)
    {
        if (WaitForSingleObject(c.Process, milliseconds) != 0) return "timeout";
        uint code;
        return GetExitCodeProcess(c.Process, out code) ? code.ToString() : "exit-code-unavailable";
    }

    // Only for this probe's own non-elevated synthetic child after its bounded wait expired.
    public static bool TerminateOwnSyntheticChild(T50Child c) { return TerminateProcess(c.Process, 0xDEAD); }

    public static void Release(T50Child c)
    {
        if (c.Thread != IntPtr.Zero) { CloseHandle(c.Thread); c.Thread = IntPtr.Zero; }
        if (c.Process != IntPtr.Zero) { CloseHandle(c.Process); c.Process = IntPtr.Zero; }
    }

    // Adapter-gates A.4 step 3.4: image path from the process, opened with no write or delete share, FileIdInfo read.
    public static string PeerImageFileId(T50Child c, out string imagePath, out int error)
    {
        StringBuilder buffer = new StringBuilder(32768);
        uint size = (uint)buffer.Capacity;
        imagePath = null;
        if (!QueryFullProcessImageNameW(c.Process, 0, buffer, ref size)) { error = Marshal.GetLastWin32Error(); return null; }
        imagePath = buffer.ToString(0, (int)size);
        T50Open o = Open(imagePath, GenericRead, ShareRead, 0);
        if (!o.Ok) { error = o.Error; return null; }
        try { return FileIdOf(o.Handle, out error); } finally { o.Handle.Dispose(); }
    }

    // 18 = TokenElevationType (1 default, 2 full, 3 limited); 20 = TokenElevation (0/1).
    public static int TokenInt(int infoClass)
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token)) return -1;
        try { int value, returned; return GetTokenInformation(token, infoClass, out value, 4, out returned) ? value : -1; }
        finally { CloseHandle(token); }
    }
}
'@

$lazySource = @'
public static class Aq1Lazy { public static int Value() { return 7; } }
'@

$managedSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
public static class Aq1Program
{
    public static int Main(string[] args)
    {
        Thread.Sleep(1500);
        int result = 42;
        try
        {
            string directory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            Assembly lazy = Assembly.LoadFrom(Path.Combine(directory, "aq1-lazy.dll"));
            object value = lazy.GetType("Aq1Lazy", true).GetMethod("Value").Invoke(null, null);
            if (!(value is int) || (int)value != 7) result = 43;
        }
        catch (Exception) { result = 44; }
        Thread.Sleep(1500);
        return result;
    }
}
'@

$runtimeConfigText = '{"runtimeOptions":{"tfm":"net10.0","rollForward":"LatestPatch","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'

# Pinned 56-entry release list generated from docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json (path|length|sha256).
$releaseText = @'
choco.exe|12022880|4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0
choco.exe.manifest|2997|D715D6071E5CDD6447D46ED8E903B9B3AD5952ACC7394EE17593D87A546C17FC
helpers/Chocolatey.PowerShell.dll|40544|B36F60CB783329F341ED0CD0075D9A0BA2863F835FE46A944CCE9F693E9C2480
helpers/Chocolatey.PowerShell.dll-help.xml|59470|A0BCB2523147215E66A126F8B69E578043C14FEE3D72DF085F54C53E0D3CFBD9
helpers/chocolateyInstaller.psm1|22385|4AF7D8DCDBA7335F96D4D7F9B7AB75B29A890380D8C7C35C59F60739DB8A604E
helpers/chocolateyProfile.psm1|15622|ED69CDE7544EFE46ECBC66B10EDC55140E49CD2FA17F5CCF0E214D769E3CAD2B
helpers/chocolateyScriptRunner.ps1|17777|859CFA5D9DC747A5BC5651331977BEEF2177CF8335A24A8F0A26D7965FD66944
helpers/ChocolateyTabExpansion.ps1|37972|1A9137DE5F6B86E6BF4E478E7ACDF95433107E010FED460FAAD88D94EABB50E6
helpers/functions/Format-FileSize.ps1|16054|D47A44752FD6A983F9AB0E48AA8B12A2B0BC772EA0BB380C64723BB8E0B2CCBB
helpers/functions/Get-CheckSumValid.ps1|26167|5F88F0BEAA6CCE518833111F16B532E33F80BA3EA5B878C2690B4FC1218CF3B2
helpers/functions/Get-ChocolateyConfigValue.ps1|16318|83EB927EFCD495E15FD4FF5D043E1F0CF4B2DCEDED9AEB5A4AF3DB0CDE2BFD8B
helpers/functions/Get-ChocolateyPath.ps1|17022|18F1E1DC7EA4C3DAAE3FC51FD1373330C0132270180ED93BCAC7A1D2843353F5
helpers/functions/Get-ChocolateyUnzip.ps1|25780|BB77491D99FA16F09048E81A2CEDC29F3E6397D0D166BA2F72317ACA04347C25
helpers/functions/Get-ChocolateyWebFile.ps1|31820|5A763E6F6895FB36C99C942C56B2E5860E316978CE61FFB6D5A4599B357EAE4E
helpers/functions/Get-FtpFile.ps1|23075|A0D1FCEC293A9D8B1340BBF54194884EF1C7495C3CBE9D4D5673EDF2E5CCFB43
helpers/functions/Get-OSArchitectureWidth.ps1|17145|30F831B5189132D642EDFD7CC9E4F44B11AE357652E1748073D94206544D4B77
helpers/functions/Get-PackageParameters.ps1|21718|D537580623CA8088692AD463E8913A83EDB50963BD4B3B2B7B579E4E2B3B71F9
helpers/functions/Get-ToolsLocation.ps1|17518|10EF1BD8A810EE08F601A207AC83A4C7D9EBAD1A4777378CF3749E3C56B98C48
helpers/functions/Get-UACEnabled.ps1|16240|3F0C1C8C91696C6AE9C0E41589319D200D2C4BD16CABF4E2F1A11FC947A72F91
helpers/functions/Get-UninstallRegistryKey.ps1|20286|573FD644BEE61BF85053989C7111BE4A33223CE9BFD0AE5F95E05382FA08A1F5
helpers/functions/Get-VirusCheckValid.ps1|16118|73D85AA2297033F106A0C8C3138EFB9AD36F97ED108E040F12348FAE94C56F74
helpers/functions/Get-WebFile.ps1|28730|2DC9AE2D1DE175E6B867FF89F84BA25D08DD5F41B84E2818318CA23F3EB5797E
helpers/functions/Get-WebFileName.ps1|24253|DB062D9D09D7DAE751E626BF97138EAE6E9350112E2738CB3BE9EF78DBDACE1A
helpers/functions/Get-WebHeaders.ps1|20634|1D15D738C319132C792AC6F8820F50CCB0FC32597E9C886746BCC31FCCE2C683
helpers/functions/Install-BinFile.ps1|20895|767FD247F1F93CAC6188BA1A0C3398B87CF3178E25DED4A16CED7E9BB3CD27F6
helpers/functions/Install-ChocolateyEnvironmentVariable.ps1|18759|223F17F84D214C9FA9478817EFF65A2681D505DFBFB6B81A2121E446E9614000
helpers/functions/Install-ChocolateyExplorerMenuItem.ps1|19037|1E2FB44E0BE817B5E16A03A30502C65F61DDDC551BD3923EA571E3F83980E049
helpers/functions/Install-ChocolateyFileAssociation.ps1|17879|9C7C53D9EF8B902F60D5448ADBE96FC1EDA319C1DD6E6E7C8A4FFB3EB46C28DB
helpers/functions/Install-ChocolateyInstallPackage.ps1|29245|09E1973A0286C5912C7F233FCE89B2EFD9347EFDD085869437D9FCBE69A5C5D7
helpers/functions/Install-ChocolateyPackage.ps1|31292|16B5DF14198360715D06A5F12F2B1976D38E729BBE37748E0CBB17F57C4F367E
helpers/functions/Install-ChocolateyPinnedTaskBarItem.ps1|17373|DBA9B9E9329FA5D918B1E941DBFED9363A616033CDFCAD4A0C60AF9C41C4C4AC
helpers/functions/Install-ChocolateyPowershellCommand.ps1|23698|AEEE93F35724D656A73D1572522FE9B985FA1CAE6978B0405398EF9327A1580D
helpers/functions/Install-ChocolateyShortcut.ps1|22580|3410AEB9BC5106B29F2C4CBC74C9FEBDC229C569153DDB1E41188A7396079A3B
helpers/functions/Install-ChocolateyVsixPackage.ps1|23306|4E982CE84C225C6870CC78120E5F85FB622756FEFF4C7E8EB7088473A2538620
helpers/functions/Install-ChocolateyZipPackage.ps1|24593|B0211091D5FE20E5E917855550EEFB2961BE6556A6F4F8A9C98619723A062CB7
helpers/functions/Install-Vsix.ps1|16518|DAD0EF31EB232C6C189E0AD947E62E71C5239BF2DAD8F9D72A06CF3544A427A4
helpers/functions/Set-PowerShellExitCode.ps1|16048|FFFB05319B00EFB87D2705760EF351C11AD2B1913469635B980D386310BF0E1F
helpers/functions/Start-ChocolateyProcessAsAdmin.ps1|32129|694C4DAED9ADD47D4ECE4BD07568AA57DBC1F3316426F78CE5FD1EF2F2CE2625
helpers/functions/Uninstall-BinFile.ps1|18078|F706530F0CDABB2F02C9D5B70D7DE77D1F02FC4F6730C815FF8410DCF208B9FC
helpers/functions/Uninstall-ChocolateyEnvironmentVariable.ps1|17361|C20B736BCE859734C4497C6D5AAEC13BFA3C201461CC02F48A7539FEA54BE59E
helpers/functions/Uninstall-ChocolateyPackage.ps1|20524|07CED451A144A7F6E3FD24D19BFCB2E2A5EA49A969A036754CB833DC2D2986CB
helpers/functions/UnInstall-ChocolateyZipPackage.ps1|17275|599EA45533DC1AB68A9646C6A88B71F4FC11A8669FA3EE8F41360435CA8816DC
helpers/functions/Write-FunctionCallLogMessage.ps1|16311|117E7BBFD11DA2F5BD00F66AA004837DD774485E96334FB42B8AC537F4FB012B
tools/7z.dll|1319424|1C57100CC237DB626259F68FE91C5A632A5128E0E01BAB1452BAA1104FF84311
tools/7z.dll.manifest|513|6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033
tools/7z.exe|358912|C3598543C41918B5C94DAFB9DD724C16C9A217D36B5EE530E3BC10C9F12ACA6C
tools/7z.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/7z.exe.manifest|513|6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033
tools/7zip.license.txt|6031|519AC0A4BDED9C18EA02E0AFB71F663D8C47373BD9FACD3AC96A79F51D77765D
tools/checksum.exe|39008|04DDF72E7D930D3001D8111E1C5EA0A408A56D5BF4FFCF9C6680427375E31E1D
tools/checksum.exe.config|150|9B250C32CBEC90D2A61CB90055AC825D7A5F9A5923209CFD0625FCA09A908D0A
tools/checksum.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/checksum.license.txt|95|29472B6BE2F4E7134F09CC2FADF088CB87089853B383CA4AF29C19CC8DFC1A62
tools/shimgen.exe|568928|9033F75F25BAE75853BCA02AAB9BBF1EA73D52CD256822F744820052B1CB70AF
tools/shimgen.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/shimgen.license.txt|3758|D4D2EF2C520EC3E4ECFF52C867EBD28E357900E0328BB4173CB46996DED353F4
'@
$release = @(foreach ($line in ($releaseText -split "`r?`n")) {
    if ($line.Length -gt 0) { $f = $line.Split('|'); [pscustomobject]@{ Path = $f[0]; Length = [long]$f[1]; Sha256 = $f[2] } }
})
if ($release.Count -ne 56) { Stop-Probe 'embedded-release-list-count' }
$engineSha256 = '4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0'
$assetSha256 = 'F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839'

# ---- Shared by Run and SelfCheck: the one Add-Type call and the bounded synthetic build -----------
function Import-T50 {
    Assert-Deadline
    Add-Type -TypeDefinition $nativeSource -Language CSharp   # the same call in Run and SelfCheck
    Assert-Deadline
}

# CodeDom (csc.exe) build in a separate runspace so that the wait is bounded. Outputs are never loaded into this process.
# On timeout the probe stops; an orphaned csc.exe can only finish writing into the given scratch/output directory.
$compileScript = @'
param($Source, $Output, $Executable, $Scratch)
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
$parameters.GenerateExecutable = $Executable
$parameters.GenerateInMemory = $false
$parameters.OutputAssembly = $Output
$parameters.TempFiles = New-Object System.CodeDom.Compiler.TempFileCollection($Scratch, $false)
$result = $provider.CompileAssemblyFromSource($parameters, $Source)
-not $result.Errors.HasErrors
'@
function Build-Synthetic([string]$Source, [string]$Output, [bool]$Executable, [string]$Scratch) {
    Assert-Deadline
    $shell = [PowerShell]::Create()
    $async = $null
    try {
        [void]$shell.AddScript($compileScript).AddArgument($Source).AddArgument($Output).AddArgument($Executable).AddArgument($Scratch)
        $async = $shell.BeginInvoke()
        $remaining = [int][Math]::Max(1000, [Math]::Min(60000, ($deadlineSeconds - $clock.Elapsed.TotalSeconds) * 1000))
        if (-not $async.AsyncWaitHandle.WaitOne($remaining)) { Stop-Probe 'synthetic-build-deadline' }
        $ok = @($shell.EndInvoke($async))
        if ($shell.HadErrors -or $ok.Count -ne 1 -or -not $ok[0]) { Stop-Probe 'synthetic-build-failed' }
    } finally {
        if ($null -ne $async -and $async.IsCompleted) { $shell.Dispose() }
    }
}

# ---- SelfCheck (CI rehearsal): host version, the Run-mode Add-Type compile and the synthetic compiles only -------
# No reads outside this script's own directory (other than .NET/compiler binaries), no launches, no probes; writes only
# compiler output and scratch under -ScratchDirectory.
if ($Mode -eq 'SelfCheck') {
    'SELFCHECK-HOST: ' + $PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
        'SELFCHECK-FAIL: windows-powershell-5.1-required'; exit 2
    }
    if ([string]::IsNullOrEmpty($ScratchDirectory) -or -not [IO.Path]::IsPathRooted($ScratchDirectory)) { 'SELFCHECK-FAIL: absolute-scratch-directory-required'; exit 2 }
    $scratchInfo = New-Object IO.DirectoryInfo $ScratchDirectory
    if (-not $scratchInfo.Exists -or ($scratchInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or @($scratchInfo.GetFileSystemInfos()).Count -ne 0) {
        'SELFCHECK-FAIL: scratch-directory-must-exist-empty-and-not-be-a-reparse-point'; exit 2
    }
    $env:TEMP = $scratchInfo.FullName   # process scope only
    $env:TMP = $scratchInfo.FullName
    try {
        'SELFCHECK-SHA256: ' + (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        'SELFCHECK-RELEASE-ENTRIES: ' + $release.Count
        Import-T50
        if ([T50]::FileModificationRights.Length -ne 7 -or [T50]::DirectoryModificationRights.Length -ne 6 -or [T50]::AncestorReplacementRights.Length -ne 4) { Stop-Probe 'rights-lists' }
        Build-Synthetic $lazySource (Join-Path $scratchInfo.FullName 'aq1-lazy.dll') $false $scratchInfo.FullName
        Build-Synthetic $managedSource (Join-Path $scratchInfo.FullName 'aq1-managed.exe') $true $scratchInfo.FullName
    } catch {
        $message = [string]$_.Exception.Message
        if ($message.StartsWith('STOP: ')) { 'SELFCHECK-FAIL: ' + $message.Substring(6) } else { 'SELFCHECK-FAIL: ' + $_.Exception.GetType().Name }
        exit 1
    }
    'SELFCHECK-OK'
    exit 0
}

# ---- S0: interpreter, platform, fixed location and token preconditions ----------------
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) { Stop-Probe 'windows-powershell-5.1-required' }
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { Stop-Probe 'x64-process-required' }
if ([Environment]::OSVersion.Version.Major -ne 10 -or [Environment]::OSVersion.Version.Build -lt 22000) { Stop-Probe 'windows-11-required' }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$evidenceDir = Join-Path $tempRoot 'pcsteward-evidence-task50'
$scriptPath = [IO.Path]::GetFullPath($PSCommandPath)
if (-not [string]::Equals([IO.Path]::GetDirectoryName($scriptPath), $evidenceDir, [StringComparison]::OrdinalIgnoreCase)) { Stop-Probe 'script-not-in-dedicated-evidence-directory' }
$cursor = New-Object IO.DirectoryInfo $evidenceDir
while ($null -ne $cursor) {
    if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { Stop-Probe 'reparse-point-in-evidence-ancestry' }
    $cursor = $cursor.Parent
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ((New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Stop-Probe 'elevated-or-full-administrator-token' }

# ---- Cleanup mode: fixed-depth manual walk (evidence dir -> run-* -> tmp), reparse refused at every level ----
if ($Mode -eq 'Cleanup') {
    $runPattern = '^run-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}$'
    $runFiles = @('aq1-native.exe', 'aq1-managed.exe', 'aq1-lazy.dll', 'aq1-managed.runtimeconfig.json', 'a2l3-own.bin', 'evidence.json')
    function Get-Level([string]$Directory, [int]$Limit) {
        $items = @(Get-ChildItem -LiteralPath $Directory -Force)   # one level only; never -Recurse
        if ($items.Count -gt $Limit) { Stop-Probe 'cleanup-unexpected-entry-count' }
        foreach ($item in $items) { if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { Stop-Probe 'cleanup-reparse-point' } }
        return $items
    }
    foreach ($top in (Get-Level $evidenceDir 16)) {
        if (-not $top.PSIsContainer) { if ($top.Name -ne 'Task50-HostEvidence.ps1') { Stop-Probe 'cleanup-unexpected-file' }; continue }
        if ($top.Name -notmatch $runPattern) { Stop-Probe 'cleanup-unexpected-directory' }
        foreach ($inRun in (Get-Level $top.FullName 16)) {
            if (-not $inRun.PSIsContainer) { if ($runFiles -notcontains $inRun.Name) { Stop-Probe 'cleanup-unexpected-file' }; continue }
            if ($inRun.Name -ne 'tmp') { Stop-Probe 'cleanup-unexpected-directory' }
            foreach ($inScratch in (Get-Level $inRun.FullName 256)) { if ($inScratch.PSIsContainer) { Stop-Probe 'cleanup-unexpected-directory' } }
        }
    }
    Remove-Item -LiteralPath $evidenceDir -Recurse -Force   # reached only when every level was checked and no reparse point exists
    'CLEANUP-COMPLETE: <TEMP>\pcsteward-evidence-task50 removed'
    return
}

# ---- Run mode setup -------------------------------------------------------------------
$scriptSha256 = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
$runName = 'run-' + [DateTime]::UtcNow.ToString('yyyyMMdd\THHmmss\Z') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $evidenceDir $runName
$scratch = Join-Path $run 'tmp'
if (Test-Path -LiteralPath $run) { Stop-Probe 'run-directory-exists' }
[void][IO.Directory]::CreateDirectory($scratch)
$env:TEMP = $scratch   # process scope only: compiler scratch files stay inside the run directory
$env:TMP = $scratch
Import-T50
# ---- Sanitizing helpers -----------------------------------------------------------------
$programFiles = [IO.Path]::GetFullPath([Environment]::GetFolderPath('ProgramFiles')).TrimEnd('\')
$programData = [IO.Path]::GetFullPath([Environment]::GetFolderPath('CommonApplicationData')).TrimEnd('\')
$system32 = [IO.Path]::GetFullPath([Environment]::SystemDirectory).TrimEnd('\')
$chocoRoot = Join-Path $programData 'chocolatey'
$roots = New-Object 'System.Collections.Generic.List[object]'
foreach ($pair in @(@('<RUN>', $run), @('<EVIDENCE>', $evidenceDir), @('<CHOCO>', $chocoRoot), @('<SYSTEM32>', $system32), @('<PF>', $programFiles), @('<PROGRAMDATA>', $programData))) { $roots.Add($pair) }
function San([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $Path }
    foreach ($pair in $roots) { if ($Path.StartsWith($pair[1], [StringComparison]::OrdinalIgnoreCase)) { return $pair[0] + $Path.Substring($pair[1].Length) } }
    return '<OTHER>'
}
function Get-FailureToken($ErrorRecord) {
    $message = [string]$ErrorRecord.Exception.Message
    if ($message.StartsWith('STOP: ')) { return $message.Substring(6) }
    return $ErrorRecord.Exception.GetBaseException().GetType().Name + ':0x' + ('{0:X8}' -f $ErrorRecord.Exception.GetBaseException().HResult)
}
$redactions = 0
function Redact([string]$Text) {
    foreach ($secret in @($env:USERNAME, $env:COMPUTERNAME, $env:USERDOMAIN, $identity.User.Value)) {
        if (-not [string]::IsNullOrEmpty($secret) -and $secret.Length -ge 3) {
            $pattern = '(?<![A-Za-z0-9])' + [regex]::Escape($secret) + '(?![A-Za-z0-9])'
            $script:redactions += ([regex]::Matches($Text, $pattern, 'IgnoreCase')).Count
            $Text = [regex]::Replace($Text, $pattern, '<REDACTED>', 'IgnoreCase')
        }
    }
    return $Text
}
function Get-PathFileId([string]$Path, [bool]$Directory) {
    $flags = if ($Directory) { [T50]::FlagBackup -bor [T50]::FlagOpenReparse } else { [T50]::FlagOpenReparse }
    $o = [T50]::Open($Path, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]$flags)
    if (-not $o.Ok) { return 'error:' + $o.Error }
    try { $e = 0; $id = [T50]::FileIdOf($o.Handle, [ref]$e); if ($null -eq $id) { 'error:' + $e } else { $id } } finally { $o.Handle.Dispose() }
}
function Get-Replacement([string[]]$Paths) {
    @(foreach ($p in $Paths) { (Split-Path -Leaf $p) + ':DELETE=' + [T50]::ProbeOpen($p, [T50]::Delete, $false) + ',WRITE_DATA=' + [T50]::ProbeOpen($p, [T50]::FileWriteData, $false) })
}

# ---- O5: non-elevated readability of every file-first post-update check input ------------
function Read-Tree([string]$TopName, $KnownSet, [bool]$RecordNames, $Problems) {
    $r = [ordered]@{ tree = $TopName; present = $false; listError = $null; directories = 0; files = 0; unlistedFiles = 0; readable = 0; unreadable = 0; reparse = 0; overBound = $false; failures = @() }
    $top = Join-Path $chocoRoot $TopName
    $probe = [T50]::Open($top, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]([T50]::FlagBackup -bor [T50]::FlagOpenReparse))
    if (-not $probe.Ok) {
        if ($probe.Error -eq 2 -or $probe.Error -eq 3) { return $r }
        $r.present = $true; $r.listError = $probe.Error; [void]$Problems.Add($TopName + ':open:' + $probe.Error); return $r
    }
    $probe.Handle.Dispose(); $r.present = $true
    $failures = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $stack.Push([object[]]@($top, 0))
    $count = 0
    while ($stack.Count -gt 0 -and -not $r.overBound) {
        Assert-Deadline
        $item = $stack.Pop(); $dir = [string]$item[0]; $depth = [int]$item[1]
        $info = New-Object IO.DirectoryInfo $dir
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $r.reparse++; continue }
        try { $children = @($info.EnumerateFileSystemInfos()) }
        catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$failures.Add('list:' + $code); [void]$Problems.Add($TopName + ':list:' + $code); continue }
        foreach ($child in $children) {
            Assert-Deadline
            $count++
            if ($count -gt 32768) { $r.overBound = $true; [void]$Problems.Add($TopName + ':over-bound'); break }
            $relative = $child.FullName.Substring($chocoRoot.Length + 1).Replace('\', '/')
            $label = if ($RecordNames) { $relative } else { $TopName + '/*' }
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $r.reparse++; [void]$failures.Add($label + ':reparse'); continue }
            if ($child -is [IO.DirectoryInfo]) {
                $r.directories++
                if ($depth + 1 -gt 32) { $r.overBound = $true; [void]$Problems.Add($TopName + ':depth') } else { $stack.Push([object[]]@($child.FullName, $depth + 1)) }
                continue
            }
            $r.files++
            if ($null -ne $KnownSet -and $KnownSet.Contains($relative.ToUpperInvariant())) { continue }
            $r.unlistedFiles++
            $f = [T50]::ReadFile($child.FullName, 64MB, $true, 0)
            if ($f.Ok) { $r.readable++ } else { $r.unreadable++; [void]$failures.Add($label + ':' + $f.Failure + ':' + $f.Error); [void]$Problems.Add($TopName + ':' + $f.Failure + ':' + $f.Error) }
        }
    }
    $r.failures = @($failures | Select-Object -First 50)
    return $r
}

function Format-File($File, [string]$Label, [string]$ExpectedSha256) {
    $o = [ordered]@{ path = $Label; ok = $File.Ok; failure = $File.Failure; error = $File.Error; length = $File.Length; fileIdRead = ($null -ne $File.FileId); reparse = $File.Reparse }
    if ($File.Sha256) { $o.sha256 = $File.Sha256 }
    if ($ExpectedSha256) { $o.matchesPin = ($File.Sha256 -eq $ExpectedSha256) }
    return $o
}

function Invoke-O5 {
    $started = [Diagnostics.Stopwatch]::StartNew()
    $problems = New-Object System.Collections.ArrayList
    $findings = New-Object System.Collections.ArrayList
    $r = [ordered]@{ case = 'O5'; status = 'not-run'; fixedRoot = '<PROGRAMDATA>\chocolatey' }
    $r.environmentVariableSet = -not [string]::IsNullOrEmpty($env:ChocolateyInstall)
    $r.environmentVariableEqualsFixedRoot = $r.environmentVariableSet -and [string]::Equals(([IO.Path]::GetFullPath($env:ChocolateyInstall)).TrimEnd('\'), $chocoRoot, [StringComparison]::OrdinalIgnoreCase)
    $engine = Join-Path $chocoRoot 'choco.exe'
    $presence = [T50]::Open($engine, [T50]::FileReadAttributes, [T50]::ShareAll, [T50]::FlagOpenReparse)
    if ($presence.Ok) { $presence.Handle.Dispose() }
    elseif ($presence.Error -eq 2 -or $presence.Error -eq 3) {
        $r.status = 'not-installed'; $r.rootDirectoryPresent = [IO.Directory]::Exists($chocoRoot); return $r
    }
    if (@(Get-Process -Name 'choco' -ErrorAction SilentlyContinue).Count -gt 0) { $r.status = 'skipped-choco-running'; return $r }
    $r.status = 'run'
    $rootId = Get-PathFileId $chocoRoot $true   # used only as a readability check; never recorded
    $r.rootIdentityRead = -not $rootId.StartsWith('error:')
    if (-not $r.rootIdentityRead) { [void]$problems.Add('root:file-id:' + $rootId.Substring(6)) }
    try { if (([IO.File]::GetAttributes($chocoRoot) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { [void]$findings.Add('root:reparse') } }
    catch { [void]$problems.Add('root:attributes:' + ($_.Exception.GetBaseException().HResult -band 0xFFFF)) }

    # Engine and the pinned 56-file code list (engine is entry 1; read once per entry).
    $known = New-Object 'System.Collections.Generic.HashSet[string]'
    $codeFailures = New-Object System.Collections.ArrayList
    $readable = 0; $matched = 0; $bytes = 0L
    foreach ($entry in $release) {
        Assert-Deadline
        [void]$known.Add($entry.Path.ToUpperInvariant())
        $f = [T50]::ReadFile((Join-Path $chocoRoot $entry.Path.Replace('/', '\')), 64MB, $true, 0)
        if ($entry.Path -eq 'choco.exe') { $r.engine = Format-File $f 'choco.exe' $engineSha256 }
        if ($f.Ok) {
            $readable++; $bytes += $f.Length
            if ($f.Length -eq $entry.Length -and $f.Sha256 -eq $entry.Sha256) { $matched++ } else { [void]$codeFailures.Add($entry.Path + ':mismatch') }
        } else {
            [void]$codeFailures.Add($entry.Path + ':' + $f.Failure + ':' + $f.Error); [void]$problems.Add('code:' + $f.Failure + ':' + $f.Error)
        }
        if ($f.Reparse) { [void]$findings.Add('code:reparse:' + $entry.Path) }
    }
    $r.codeList = [ordered]@{ expected = 56; readable = $readable; matchedPin = $matched; bytesRead = $bytes; failures = @($codeFailures) }

    # Unlisted helpers/tools files (would make the code list differ) and optional extension/hook code trees.
    $r.helpersTree = Read-Tree 'helpers' $known $true $problems
    $r.toolsTree = Read-Tree 'tools' $known $true $problems
    $r.extensionsTree = Read-Tree 'extensions' $null $false $problems
    $r.hooksTree = Read-Tree 'hooks' $null $false $problems

    # Registration: complete lib discovery set (lib root and immediate directories, top-level *.nupkg only),
    # full reads of the chocolatey and chocolatey.extension registrations, names of other packages never recorded.
    $lib = Join-Path $chocoRoot 'lib'
    $reg = [ordered]@{ libListed = $false; libListError = $null; directories = 0; rootNupkg = 0; nupkgFiles = 0; nupkgReadable = 0; nupkgUnreadable = 0; reparse = 0; overBound = $false; errorCodes = @(); chocolatey = 'absent'; chocolateyFiles = @(); extension = 'absent'; extensionFiles = @() }
    $codes = New-Object System.Collections.ArrayList
    try {
        $directories = @([IO.Directory]::EnumerateDirectories($lib) | Select-Object -First 1025)
        $rootPackages = @([IO.Directory]::EnumerateFiles($lib, '*.nupkg') | Where-Object { $_.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 2049)
        $reg.libListed = $true
    } catch { $reg.libListError = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$problems.Add('lib:list:' + $reg.libListError); $directories = @(); $rootPackages = @() }
    if ($directories.Count -gt 1024 -or $rootPackages.Count -gt 2048) { $reg.overBound = $true; [void]$problems.Add('lib:over-bound') }
    $reg.directories = $directories.Count; $reg.rootNupkg = $rootPackages.Count
    $packageFiles = New-Object System.Collections.ArrayList
    foreach ($p in $rootPackages) { [void]$packageFiles.Add(@($p, '')) }
    foreach ($d in $directories) {
        Assert-Deadline
        $name = (Split-Path -Leaf $d).ToLowerInvariant()
        try { $isReparse = ([IO.File]::GetAttributes($d) -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
        catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$codes.Add($code); [void]$problems.Add('lib-dir:attributes:' + $code); continue }
        if ($isReparse) { $reg.reparse++; [void]$findings.Add('lib:reparse'); continue }
        $client = if ($name -eq 'chocolatey' -or $name -eq 'chocolatey.extension') { $name } else { '' }
        if ($client -eq 'chocolatey') { $reg.chocolatey = 'present' } elseif ($client -eq 'chocolatey.extension') { $reg.extension = 'present' }
        try {
            $pattern = if ($client) { '*' } else { '*.nupkg' }
            foreach ($file in @([IO.Directory]::EnumerateFiles($d, $pattern) | Select-Object -First 65)) {
                if ($client -or $file.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase)) { [void]$packageFiles.Add(@($file, $client)) }
            }
        } catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$codes.Add($code); [void]$problems.Add('lib-dir:list:' + $code) }
    }
    $chocolateyFiles = New-Object System.Collections.ArrayList; $extensionFiles = New-Object System.Collections.ArrayList
    foreach ($pair in $packageFiles) {
        Assert-Deadline
        $file = [string]$pair[0]; $client = [string]$pair[1]
        $isPackage = $file.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase)
        if ($isPackage) { $reg.nupkgFiles++ }
        if ($reg.nupkgFiles -gt 2048) { $reg.overBound = $true; [void]$problems.Add('lib:over-bound'); break }
        $f = if ($client) { [T50]::ReadFile($file, 64MB, $true, 0) } else { [T50]::ReadFile($file, 64MB, $false, 65536) }
        if ($isPackage) { if ($f.Ok) { $reg.nupkgReadable++ } else { $reg.nupkgUnreadable++ } }
        if (-not $f.Ok) { [void]$codes.Add($f.Error); [void]$problems.Add('lib-file:' + $f.Failure + ':' + $f.Error) }
        if ($f.Reparse) { $reg.reparse++; [void]$findings.Add('lib:reparse') }
        if ($client) {
            $leaf = (Split-Path -Leaf $file).ToLowerInvariant()
            $expected = if ($client -eq 'chocolatey' -and $leaf -eq 'chocolatey.nupkg') { $assetSha256 } else { $null }
            $formatted = Format-File $f ($client + '/' + $leaf) $expected
            if ($client -eq 'chocolatey') { [void]$chocolateyFiles.Add($formatted) } else { [void]$extensionFiles.Add($formatted) }
        }
    }
    $reg.chocolateyFiles = @($chocolateyFiles); $reg.extensionFiles = @($extensionFiles)
    $reg.errorCodes = @($codes | Sort-Object -Unique)
    if ($reg.libListed -and $reg.chocolatey -eq 'absent') { [void]$findings.Add('lib:chocolatey-registration-absent') }
    $r.registration = $reg

    # Store residue for the two client identities: directory listing plus bounded 4 KiB tail reads.
    $store = Join-Path $chocoRoot '.chocolatey'
    $st = [ordered]@{ present = $false; listError = $null; directories = 0; clientDirectories = 0; markerFiles = 0; markerReadable = 0; errorCodes = @() }
    $stCodes = New-Object System.Collections.ArrayList
    if ([IO.Directory]::Exists($store)) {
        $st.present = $true
        try {
            $storeDirs = @([IO.Directory]::EnumerateDirectories($store) | Select-Object -First 4097)
            $st.directories = $storeDirs.Count
            if ($storeDirs.Count -gt 4096) { [void]$problems.Add('store:over-bound') }
            foreach ($d in $storeDirs) {
                if ((Split-Path -Leaf $d) -notmatch '^(?i)chocolatey(\.extension)?\.[0-9]') { continue }
                $st.clientDirectories++
                foreach ($file in @([IO.Directory]::EnumerateFiles($d) | Select-Object -First 64)) {
                    $st.markerFiles++
                    $f = [T50]::ReadFile($file, 1MB, $false, 4096)
                    if ($f.Ok) { $st.markerReadable++ } else { [void]$stCodes.Add($f.Error); [void]$problems.Add('store-file:' + $f.Failure + ':' + $f.Error) }
                }
            }
        } catch { $st.listError = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$problems.Add('store:list:' + $st.listError) }
    }
    $st.errorCodes = @($stCodes | Sort-Object -Unique)
    $r.store = $st

    $r.problems = @($problems | Select-Object -First 100)
    $r.routeFindings = @($findings | Sort-Object -Unique | Select-Object -First 100)
    $r.matchesPinnedRelease = ($matched -eq 56) -and ($r.helpersTree.unlistedFiles -eq 0) -and ($r.toolsTree.unlistedFiles -eq 0)
    $r.elapsedMs = $started.ElapsedMilliseconds
    $denied = @($problems | Where-Object { $_ -match ':5$' }).Count
    $r.verdict = if ($denied -gt 0) { 'FAIL-UNREADABLE' } elseif ($problems.Count -gt 0) { 'INCONCLUSIVE' } else { 'PASS-READABLE' }
    return $r
}

# ---- A-Q1: launch of a synthetic image while read-share holds are held ------------------------
function Invoke-Aq1Run([string]$Launcher, [string]$Image, [string]$Arguments, [string[]]$HoldPaths, [bool]$CheckPeer) {
    $r = [ordered]@{ launcher = $Launcher; held = ($HoldPaths.Count -gt 0) }
    $heldId = $null
    $holds = New-Object System.Collections.ArrayList
    try {
        foreach ($p in $HoldPaths) {
            $o = [T50]::Open($p, [T50]::GenericRead, [T50]::ShareRead, 0)   # FileShare.Read only, as HeldHelperBinding
            if (-not $o.Ok) { $r.holdError = $o.Error; return $r }
            [void]$holds.Add($o)
        }
        if ($holds.Count -gt 0) {
            $e = 0
            $heldId = [T50]::FileIdOf($holds[0].Handle, [ref]$e)   # volume serial + file id; kept in memory only
            $r.heldFileIdRead = ($null -ne $heldId)
            if ($null -ne $heldId) { $r.heldFileId128 = $heldId.Split(':')[1] }
            $r.freshFileIdBeforeEqualsHeld = ($null -ne $heldId) -and ((Get-PathFileId $HoldPaths[0] $false) -eq $heldId)
            $r.replacementWhileHeldBeforeLaunch = Get-Replacement $HoldPaths
        }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $child = switch ($Launcher) {
            'CreateProcessSuspended' { [T50]::Start($Image, $Arguments, $run, $true) }
            'CreateProcess' { [T50]::Start($Image, $Arguments, $run, $false) }
            'RunAs' { [T50]::RunAs($Image, $Arguments, $run) }
        }
        $r.launchMs = $watch.ElapsedMilliseconds
        if (-not $child.Ok) { $r.launchError = $child.Error; return $r }
        try {
            if ($Launcher -ne 'CreateProcessSuspended') { Start-Sleep -Milliseconds 500 }
            if ($CheckPeer) {
                $path = $null; $e = 0
                $peer = [T50]::PeerImageFileId($child, [ref]$path, [ref]$e)
                $r.peerImagePath = San $path
                $r.peerFileIdRead = ($null -ne $peer)
                if ($null -eq $peer) { $r.peerError = $e }
                if ($holds.Count -gt 0) { $r.peerEqualsHeld = ($null -ne $peer) -and ($null -ne $heldId) -and ($peer -eq $heldId) }
            }
            if ($holds.Count -gt 0) { $r.replacementWhileRunning = Get-Replacement $HoldPaths }
            if ($Launcher -eq 'CreateProcessSuspended') { $r.resumeError = [T50]::Resume($child) }
            $r.exit = [T50]::WaitExit($child, 15000)
            if ($r.exit -eq 'timeout' -and $Launcher -ne 'RunAs') { $r.terminatedOwnSyntheticChild = [T50]::TerminateOwnSyntheticChild($child); [void][T50]::WaitExit($child, 5000) }
        } finally { [T50]::Release($child) }
        if ($holds.Count -gt 0) {
            $r.freshFileIdAfterEqualsHeld = ($null -ne $heldId) -and ((Get-PathFileId $HoldPaths[0] $false) -eq $heldId)
            $r.replacementWhileHeldAfterExit = Get-Replacement $HoldPaths
        }
    } finally { foreach ($h in $holds) { $h.Handle.Dispose() } }
    if ($HoldPaths.Count -gt 0) {
        # Bounded re-probe (at most 3 attempts, 500 ms apart) absorbs a transient scanner open; attempts are recorded.
        $r.controlAfterRelease = @(foreach ($p in $HoldPaths) {
            $outcome = $null
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                $outcome = [T50]::ProbeOpen($p, [T50]::Delete, $false)
                if ($outcome -ne '32') { break }
                if ($attempt -lt 3) { Start-Sleep -Milliseconds 500 }
            }
            (Split-Path -Leaf $p) + ':DELETE=' + $outcome + ';attempts=' + [Math]::Min($attempt, 3)
        })
    }
    return $r
}

function Test-HeldRun($Baseline, $Held, [bool]$PeerRequired) {
    if (-not $Baseline.Contains('exit') -or $Baseline.exit -eq 'timeout') { return 'INCONCLUSIVE-BASELINE' }
    if ($Held.Contains('holdError')) { return 'INCONCLUSIVE-HOLD' }
    if ($Held.Contains('launchError')) { return 'FAIL-LAUNCH:' + $Held.launchError }
    if ($Held.exit -ne $Baseline.exit) { return 'FAIL-EXIT:' + $Held.exit }
    $all = @($Held.replacementWhileHeldBeforeLaunch) + @($Held.replacementWhileHeldAfterExit)
    if (@($all | Where-Object { $_ -match '=handle' }).Count -gt 0) { return 'FAIL-HOLD-NOT-EXCLUSIVE' }
    if (@($all | Where-Object { $_ -notmatch 'DELETE=32,WRITE_DATA=32$' }).Count -gt 0) { return 'INCONCLUSIVE-REPLACEMENT-PROBE' }
    if (@($Held.controlAfterRelease | Where-Object { $_ -notmatch 'DELETE=handle;attempts=[123]$' }).Count -gt 0) { return 'INCONCLUSIVE-CONTROL' }
    if (-not ($Held.freshFileIdBeforeEqualsHeld -and $Held.freshFileIdAfterEqualsHeld)) { return 'FAIL-IDENTITY-UNSTABLE' }
    if ($PeerRequired -and -not ($Held.Contains('peerEqualsHeld') -and $Held.peerEqualsHeld)) { return 'FAIL-PEER-IDENTITY' }
    return 'PASS'
}

function Assert-ImageDirectory {
    $allowed = @('aq1-native.exe', 'aq1-managed.exe', 'aq1-lazy.dll', 'aq1-managed.runtimeconfig.json', 'a2l3-own.bin')
    foreach ($item in @(Get-ChildItem -LiteralPath $run -Force)) {
        if ($item.PSIsContainer) { if ($item.Name -ne 'tmp') { Stop-Probe 'unexpected-directory-in-run' } }
        elseif ($allowed -notcontains $item.Name) { Stop-Probe 'unexpected-file-in-image-directory' }
    }
}

# ---- A2-L3: TryProbeRights results while read-share holds are held ------------------------------
function Get-Probe([string]$Path, [bool]$Directory, [uint32[]]$Rights) {
    $v = $null
    $outcomes = [T50]::ProbeRights($Path, $Directory, $Rights, [ref]$v)
    return [ordered]@{ path = (San $Path); outcomes = @($outcomes); verdict = $v }
}

function Get-A2L3Verdict($Unheld, $Held) {
    if (@($Unheld | Where-Object { $_.verdict -ne 'cannot-modify' }).Count -gt 0) { return 'INCONCLUSIVE-ANALOGUE-NOT-PROTECTED' }
    $outcomes = @($Held | ForEach-Object { $_.outcomes })
    if (@($outcomes | Where-Object { $_ -match '=handle$' }).Count -gt 0) { return 'FAIL-UNPROTECTED' }
    if (@($outcomes | Where-Object { $_ -match '=32$' }).Count -gt 0) { return 'FAIL-SHARING-VIOLATION' }
    if (@($Held | Where-Object { $_.verdict -ne 'cannot-modify' }).Count -gt 0) { return 'INCONCLUSIVE-OTHER-ERROR' }
    return 'PASS'
}

function Invoke-A2L3Synthetic {
    $r = [ordered]@{ case = 'A2L3-S' }
    $target = Join-Path $system32 'whoami.exe'
    $unheld = @((Get-Probe $target $false ([T50]::FileModificationRights)), (Get-Probe $system32 $true ([T50]::AncestorReplacementRights)))
    $hold = [T50]::Open($target, [T50]::GenericRead, [T50]::ShareRead, 0)
    if (-not $hold.Ok) { $r.holdError = $hold.Error; $r.verdict = 'INCONCLUSIVE-HOLD'; return $r }
    $holdWatch = [Diagnostics.Stopwatch]::StartNew()
    try { $held = @((Get-Probe $target $false ([T50]::FileModificationRights)), (Get-Probe $system32 $true ([T50]::AncestorReplacementRights))) }
    finally { $hold.Handle.Dispose(); $r.systemFileHoldMs = $holdWatch.ElapsedMilliseconds }
    $r.unheld = $unheld; $r.held = $held
    $own = Join-Path $run 'a2l3-own.bin'
    [IO.File]::WriteAllBytes($own, [byte[]](1..16))
    $r.ownUnheld = Get-Probe $own $false ([T50]::FileModificationRights)
    $ownHold = [T50]::Open($own, [T50]::GenericRead, [T50]::ShareRead, 0)
    try { $r.ownHeld = Get-Probe $own $false ([T50]::FileModificationRights) } finally { if ($ownHold.Ok) { $ownHold.Handle.Dispose() } }
    $r.positiveControl = if ($r.ownUnheld.verdict -eq 'can-modify' -and $r.ownHeld.verdict -eq 'probe-failed-closed:32') { 'as-expected' } else { 'unexpected' }
    $r.verdict = Get-A2L3Verdict $unheld $held
    if ($r.verdict -eq 'PASS' -and $r.positiveControl -ne 'as-expected') { $r.verdict = 'INCONCLUSIVE-CONTROL' }
    return $r
}

function Invoke-A2L3PcSteward {
    $r = [ordered]@{ case = 'A2L3-P' }
    if (@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count -gt 0) { $r.verdict = 'NOT-RUN-PCSTEWARD-RUNNING'; return $r }
    $install = Join-Path $programFiles 'PC Steward'
    if (-not [IO.Directory]::Exists($install)) { $r.verdict = 'NOT-RUN-NOT-INSTALLED'; return $r }
    $names = @('PCSteward.Desktop.exe', 'PCSteward.Desktop.dll', 'PCSteward.ElevatedHelper.exe', 'PCSteward.ElevatedHelper.dll',
        'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json', 'PCSteward.Execution.dll',
        'PCSteward.Application.dll', 'PCSteward.Domain.dll', 'PCSteward.Provider.Chocolatey.dll', 'PCSteward.Provider.WinGet.dll',
        'PCSteward.DeploymentCapability.txt')
    $present = @($names | Where-Object { [IO.File]::Exists((Join-Path $install $_)) })
    $r.bindingFilesPresent = $present
    $ancestors = New-Object System.Collections.ArrayList
    $cursorDir = New-Object IO.DirectoryInfo $install
    while ($null -ne $cursorDir) { [void]$ancestors.Insert(0, $cursorDir.FullName); $cursorDir = $cursorDir.Parent }
    $probeAll = {
        $list = New-Object System.Collections.ArrayList
        foreach ($a in $ancestors) {
            $rights = if ([string]::Equals($a, $install, [StringComparison]::OrdinalIgnoreCase)) { [T50]::DirectoryModificationRights } else { [T50]::AncestorReplacementRights }
            [void]$list.Add((Get-Probe $a $true $rights))
        }
        foreach ($n in $present) { [void]$list.Add((Get-Probe (Join-Path $install $n) $false ([T50]::FileModificationRights))) }
        return , $list.ToArray()
    }
    $unheld = & $probeAll
    $holds = New-Object System.Collections.ArrayList
    $holdWatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        foreach ($n in $present) {
            $o = [T50]::Open((Join-Path $install $n), [T50]::GenericRead, [T50]::ShareRead, 0)
            if (-not $o.Ok) { $r.holdError = $n + ':' + $o.Error; break }
            [void]$holds.Add($o)
        }
        if (-not $r.Contains('holdError')) { $held = & $probeAll }
    } finally { foreach ($h in $holds) { $h.Handle.Dispose() }; $r.holdMs = $holdWatch.ElapsedMilliseconds }
    $r.unheld = $unheld
    if ($r.Contains('holdError')) { $r.verdict = 'INCONCLUSIVE-HOLD'; return $r }
    $r.held = $held
    if ($r.holdMs -gt 10000) { $r.holdBoundExceeded = $true }
    $r.verdict = Get-A2L3Verdict $unheld $held
    return $r
}

# ---- Main ----------------------------------------------------------------------------------
$runVolumeId = Get-PathFileId $run $true
$programFilesVolumeId = Get-PathFileId $programFiles $true
$volumeEquality = if ($runVolumeId.StartsWith('error:') -or $programFilesVolumeId.StartsWith('error:')) { 'unknown' } else { $runVolumeId.Split(':')[0] -eq $programFilesVolumeId.Split(':')[0] }
$evidence = [ordered]@{
    schema = 'pc-steward-task50-host-evidence:1'
    cases = $Cases
    scriptSha256 = $scriptSha256
    runDirectory = '<EVIDENCE>\' + $runName
    startedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    hostFacts = [ordered]@{
        osVersion = [Environment]::OSVersion.Version.ToString()
        powerShell = $PSVersionTable.PSVersion.ToString()
        clr = [Environment]::Version.ToString()
        tokenElevationType = [T50]::TokenInt(18)
        tokenElevated = [T50]::TokenInt(20)
        evidenceVolumeEqualsProgramFilesVolume = $volumeEquality
    }
    results = [ordered]@{}
    verdicts = [ordered]@{}
    stop = $null
}
$runHandle = [T50]::Open($run, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]([T50]::FlagBackup -bor [T50]::FlagOpenReparse))
if ($runHandle.Ok) { $evidence.hostFacts.evidenceFileSystem = [T50]::FileSystemOf($runHandle.Handle); $runHandle.Handle.Dispose() }
if ($evidence.hostFacts.tokenElevated -ne 0) { Stop-Probe 'token-elevated' }

try {
    if ($Cases -eq 'Default') {
        $evidence.results.O5 = Invoke-O5
        $evidence.verdicts.O5 = if ($evidence.results.O5.status -eq 'run') { $evidence.results.O5.verdict } else { $evidence.results.O5.status.ToUpperInvariant() }
        Assert-Deadline
    }
    if ($Cases -eq 'Default' -or $Cases -eq 'Uac') {
        $native = Join-Path $run 'aq1-native.exe'
        $managed = Join-Path $run 'aq1-managed.exe'
        $lazy = Join-Path $run 'aq1-lazy.dll'
        if ($Cases -eq 'Default') { [IO.File]::Copy((Join-Path $system32 'whoami.exe'), $native, $false) }
        Build-Synthetic $lazySource $lazy $false $scratch
        Build-Synthetic $managedSource $managed $true $scratch
        Assert-ImageDirectory
    }
    if ($Cases -eq 'Default') {
        $nb = Invoke-Aq1Run 'CreateProcessSuspended' $native '/?' @() $true
        $nh = Invoke-Aq1Run 'CreateProcessSuspended' $native '/?' @($native) $true
        $evidence.results.AQ1N = [ordered]@{ baseline = $nb; held = $nh; verdict = (Test-HeldRun $nb $nh $true) }
        Assert-Deadline
        $mb = Invoke-Aq1Run 'CreateProcess' $managed '' @() $true
        $mh = Invoke-Aq1Run 'CreateProcess' $managed '' @($managed, $lazy) $true
        $evidence.results.AQ1M = [ordered]@{ baseline = $mb; held = $mh; verdict = (Test-HeldRun $mb $mh $true) }
        if ($mb.Contains('exit') -and $mb.exit -ne '42') { $evidence.results.AQ1M.verdict = 'INCONCLUSIVE-BASELINE' }
        Assert-Deadline
        $dotnet = Join-Path $programFiles 'dotnet\dotnet.exe'
        $sharedRoot = Join-Path $programFiles 'dotnet\shared\Microsoft.NETCore.App'
        $runtime10 = [IO.Directory]::Exists($sharedRoot) -and (@([IO.Directory]::EnumerateDirectories($sharedRoot, '10.0.*')).Count -gt 0)
        if ([IO.File]::Exists($dotnet) -and $runtime10) {
            $config = Join-Path $run 'aq1-managed.runtimeconfig.json'
            [IO.File]::WriteAllText($config, $runtimeConfigText, (New-Object Text.UTF8Encoding $false))
            Assert-ImageDirectory
            $arguments = 'exec --runtimeconfig "' + $config + '" "' + $managed + '"'
            $cb = Invoke-Aq1Run 'CreateProcess' $dotnet $arguments @() $false
            $ch = Invoke-Aq1Run 'CreateProcess' $dotnet $arguments @($managed, $lazy) $false
            $evidence.results.AQ1C = [ordered]@{ baseline = $cb; held = $ch; verdict = (Test-HeldRun $cb $ch $false) }
            if ($cb.Contains('exit') -and $cb.exit -ne '42') { $evidence.results.AQ1C.verdict = 'INCONCLUSIVE-HARNESS' }
        } else { $evidence.results.AQ1C = [ordered]@{ verdict = 'NOT-RUN-NO-DOTNET10-RUNTIME' } }
        $evidence.verdicts.AQ1N = $evidence.results.AQ1N.verdict
        $evidence.verdicts.AQ1M = $evidence.results.AQ1M.verdict
        $evidence.verdicts.AQ1C = $evidence.results.AQ1C.verdict
        Assert-Deadline
        $evidence.results.A2L3S = Invoke-A2L3Synthetic
        $evidence.verdicts.A2L3S = $evidence.results.A2L3S.verdict
    }
    if ($Cases -eq 'Uac') {
        $ub = Invoke-Aq1Run 'CreateProcess' $managed '' @() $true
        $uh = Invoke-Aq1Run 'RunAs' $managed '' @($managed, $lazy) $true   # exactly one UAC prompt
        $evidence.results.AQ1UAC = [ordered]@{ baseline = $ub; held = $uh; verdict = (Test-HeldRun $ub $uh $true) }
        if ($uh.Contains('launchError') -and $uh.launchError -eq 1223) { $evidence.results.AQ1UAC.verdict = 'NOT-RUN-UAC-DECLINED' }
        $evidence.verdicts.AQ1UAC = $evidence.results.AQ1UAC.verdict
    }
    if ($Cases -eq 'PcStewardHolds') {
        $evidence.results.A2L3P = Invoke-A2L3PcSteward
        $evidence.verdicts.A2L3P = $evidence.results.A2L3P.verdict
    }
} catch {
    $evidence.stop = Get-FailureToken $_
} finally {
    $evidence.elapsedMs = $clock.ElapsedMilliseconds
    $null = Redact ($evidence | ConvertTo-Json -Depth 12)   # first pass counts redactions only
    $evidence.redactions = $redactions
    $script:redactions = 0
    $json = Redact ($evidence | ConvertTo-Json -Depth 12)
    [IO.File]::WriteAllText((Join-Path $run 'evidence.json'), $json, (New-Object Text.UTF8Encoding $false))
    'EVIDENCE: <TEMP>\pcsteward-evidence-task50\' + $runName + '\evidence.json'
    'REDACTIONS: ' + $redactions
    foreach ($k in $evidence.verdicts.Keys) { 'VERDICT ' + $k + ': ' + $evidence.verdicts[$k] }
    if ($evidence.stop) { 'STOPPED: ' + $evidence.stop }
}
