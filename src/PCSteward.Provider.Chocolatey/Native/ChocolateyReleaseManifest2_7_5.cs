namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>One compiled release-tree entry: root-relative path (forward slashes), exact length and uppercase SHA-256.</summary>
internal readonly record struct ChocolateyReleaseFile(string Path, long Length, string Sha256);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3.1: the compiled 56-entry Chocolatey 2.7.5 release manifest, identified
/// by the published release-asset digest. Generated from <c>docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json</c>; a portable
/// test parses that JSON and requires exact equality with this table, so an edit to either is caught. The JSON is never read
/// at runtime (Profile V2: compile/embed, never accept a manifest supplied by SQLite or a payload). Do not edit by hand.
/// </summary>
internal static class ChocolateyReleaseManifest2_7_5
{
    internal const string Format = "pc-steward-chocolatey-release-manifest:1";
    internal const string Release = "2.7.5";
    internal const string Asset = "chocolatey.2.7.5.nupkg";
    internal const long AssetLength = 5719618;
    internal const string AssetSha256 = "F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839";

    internal static IReadOnlyList<ChocolateyReleaseFile> Files { get; } = Array.AsReadOnly(new ChocolateyReleaseFile[]
    {
        new("choco.exe", 12022880, "4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0"),
        new("choco.exe.manifest", 2997, "D715D6071E5CDD6447D46ED8E903B9B3AD5952ACC7394EE17593D87A546C17FC"),
        new("helpers/Chocolatey.PowerShell.dll", 40544, "B36F60CB783329F341ED0CD0075D9A0BA2863F835FE46A944CCE9F693E9C2480"),
        new("helpers/Chocolatey.PowerShell.dll-help.xml", 59470, "A0BCB2523147215E66A126F8B69E578043C14FEE3D72DF085F54C53E0D3CFBD9"),
        new("helpers/chocolateyInstaller.psm1", 22385, "4AF7D8DCDBA7335F96D4D7F9B7AB75B29A890380D8C7C35C59F60739DB8A604E"),
        new("helpers/chocolateyProfile.psm1", 15622, "ED69CDE7544EFE46ECBC66B10EDC55140E49CD2FA17F5CCF0E214D769E3CAD2B"),
        new("helpers/chocolateyScriptRunner.ps1", 17777, "859CFA5D9DC747A5BC5651331977BEEF2177CF8335A24A8F0A26D7965FD66944"),
        new("helpers/ChocolateyTabExpansion.ps1", 37972, "1A9137DE5F6B86E6BF4E478E7ACDF95433107E010FED460FAAD88D94EABB50E6"),
        new("helpers/functions/Format-FileSize.ps1", 16054, "D47A44752FD6A983F9AB0E48AA8B12A2B0BC772EA0BB380C64723BB8E0B2CCBB"),
        new("helpers/functions/Get-CheckSumValid.ps1", 26167, "5F88F0BEAA6CCE518833111F16B532E33F80BA3EA5B878C2690B4FC1218CF3B2"),
        new("helpers/functions/Get-ChocolateyConfigValue.ps1", 16318, "83EB927EFCD495E15FD4FF5D043E1F0CF4B2DCEDED9AEB5A4AF3DB0CDE2BFD8B"),
        new("helpers/functions/Get-ChocolateyPath.ps1", 17022, "18F1E1DC7EA4C3DAAE3FC51FD1373330C0132270180ED93BCAC7A1D2843353F5"),
        new("helpers/functions/Get-ChocolateyUnzip.ps1", 25780, "BB77491D99FA16F09048E81A2CEDC29F3E6397D0D166BA2F72317ACA04347C25"),
        new("helpers/functions/Get-ChocolateyWebFile.ps1", 31820, "5A763E6F6895FB36C99C942C56B2E5860E316978CE61FFB6D5A4599B357EAE4E"),
        new("helpers/functions/Get-FtpFile.ps1", 23075, "A0D1FCEC293A9D8B1340BBF54194884EF1C7495C3CBE9D4D5673EDF2E5CCFB43"),
        new("helpers/functions/Get-OSArchitectureWidth.ps1", 17145, "30F831B5189132D642EDFD7CC9E4F44B11AE357652E1748073D94206544D4B77"),
        new("helpers/functions/Get-PackageParameters.ps1", 21718, "D537580623CA8088692AD463E8913A83EDB50963BD4B3B2B7B579E4E2B3B71F9"),
        new("helpers/functions/Get-ToolsLocation.ps1", 17518, "10EF1BD8A810EE08F601A207AC83A4C7D9EBAD1A4777378CF3749E3C56B98C48"),
        new("helpers/functions/Get-UACEnabled.ps1", 16240, "3F0C1C8C91696C6AE9C0E41589319D200D2C4BD16CABF4E2F1A11FC947A72F91"),
        new("helpers/functions/Get-UninstallRegistryKey.ps1", 20286, "573FD644BEE61BF85053989C7111BE4A33223CE9BFD0AE5F95E05382FA08A1F5"),
        new("helpers/functions/Get-VirusCheckValid.ps1", 16118, "73D85AA2297033F106A0C8C3138EFB9AD36F97ED108E040F12348FAE94C56F74"),
        new("helpers/functions/Get-WebFile.ps1", 28730, "2DC9AE2D1DE175E6B867FF89F84BA25D08DD5F41B84E2818318CA23F3EB5797E"),
        new("helpers/functions/Get-WebFileName.ps1", 24253, "DB062D9D09D7DAE751E626BF97138EAE6E9350112E2738CB3BE9EF78DBDACE1A"),
        new("helpers/functions/Get-WebHeaders.ps1", 20634, "1D15D738C319132C792AC6F8820F50CCB0FC32597E9C886746BCC31FCCE2C683"),
        new("helpers/functions/Install-BinFile.ps1", 20895, "767FD247F1F93CAC6188BA1A0C3398B87CF3178E25DED4A16CED7E9BB3CD27F6"),
        new("helpers/functions/Install-ChocolateyEnvironmentVariable.ps1", 18759, "223F17F84D214C9FA9478817EFF65A2681D505DFBFB6B81A2121E446E9614000"),
        new("helpers/functions/Install-ChocolateyExplorerMenuItem.ps1", 19037, "1E2FB44E0BE817B5E16A03A30502C65F61DDDC551BD3923EA571E3F83980E049"),
        new("helpers/functions/Install-ChocolateyFileAssociation.ps1", 17879, "9C7C53D9EF8B902F60D5448ADBE96FC1EDA319C1DD6E6E7C8A4FFB3EB46C28DB"),
        new("helpers/functions/Install-ChocolateyInstallPackage.ps1", 29245, "09E1973A0286C5912C7F233FCE89B2EFD9347EFDD085869437D9FCBE69A5C5D7"),
        new("helpers/functions/Install-ChocolateyPackage.ps1", 31292, "16B5DF14198360715D06A5F12F2B1976D38E729BBE37748E0CBB17F57C4F367E"),
        new("helpers/functions/Install-ChocolateyPinnedTaskBarItem.ps1", 17373, "DBA9B9E9329FA5D918B1E941DBFED9363A616033CDFCAD4A0C60AF9C41C4C4AC"),
        new("helpers/functions/Install-ChocolateyPowershellCommand.ps1", 23698, "AEEE93F35724D656A73D1572522FE9B985FA1CAE6978B0405398EF9327A1580D"),
        new("helpers/functions/Install-ChocolateyShortcut.ps1", 22580, "3410AEB9BC5106B29F2C4CBC74C9FEBDC229C569153DDB1E41188A7396079A3B"),
        new("helpers/functions/Install-ChocolateyVsixPackage.ps1", 23306, "4E982CE84C225C6870CC78120E5F85FB622756FEFF4C7E8EB7088473A2538620"),
        new("helpers/functions/Install-ChocolateyZipPackage.ps1", 24593, "B0211091D5FE20E5E917855550EEFB2961BE6556A6F4F8A9C98619723A062CB7"),
        new("helpers/functions/Install-Vsix.ps1", 16518, "DAD0EF31EB232C6C189E0AD947E62E71C5239BF2DAD8F9D72A06CF3544A427A4"),
        new("helpers/functions/Set-PowerShellExitCode.ps1", 16048, "FFFB05319B00EFB87D2705760EF351C11AD2B1913469635B980D386310BF0E1F"),
        new("helpers/functions/Start-ChocolateyProcessAsAdmin.ps1", 32129, "694C4DAED9ADD47D4ECE4BD07568AA57DBC1F3316426F78CE5FD1EF2F2CE2625"),
        new("helpers/functions/Uninstall-BinFile.ps1", 18078, "F706530F0CDABB2F02C9D5B70D7DE77D1F02FC4F6730C815FF8410DCF208B9FC"),
        new("helpers/functions/Uninstall-ChocolateyEnvironmentVariable.ps1", 17361, "C20B736BCE859734C4497C6D5AAEC13BFA3C201461CC02F48A7539FEA54BE59E"),
        new("helpers/functions/Uninstall-ChocolateyPackage.ps1", 20524, "07CED451A144A7F6E3FD24D19BFCB2E2A5EA49A969A036754CB833DC2D2986CB"),
        new("helpers/functions/UnInstall-ChocolateyZipPackage.ps1", 17275, "599EA45533DC1AB68A9646C6A88B71F4FC11A8669FA3EE8F41360435CA8816DC"),
        new("helpers/functions/Write-FunctionCallLogMessage.ps1", 16311, "117E7BBFD11DA2F5BD00F66AA004837DD774485E96334FB42B8AC537F4FB012B"),
        new("tools/7z.dll", 1319424, "1C57100CC237DB626259F68FE91C5A632A5128E0E01BAB1452BAA1104FF84311"),
        new("tools/7z.dll.manifest", 513, "6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033"),
        new("tools/7z.exe", 358912, "C3598543C41918B5C94DAFB9DD724C16C9A217D36B5EE530E3BC10C9F12ACA6C"),
        new("tools/7z.exe.ignore", 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"),
        new("tools/7z.exe.manifest", 513, "6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033"),
        new("tools/7zip.license.txt", 6031, "519AC0A4BDED9C18EA02E0AFB71F663D8C47373BD9FACD3AC96A79F51D77765D"),
        new("tools/checksum.exe", 39008, "04DDF72E7D930D3001D8111E1C5EA0A408A56D5BF4FFCF9C6680427375E31E1D"),
        new("tools/checksum.exe.config", 150, "9B250C32CBEC90D2A61CB90055AC825D7A5F9A5923209CFD0625FCA09A908D0A"),
        new("tools/checksum.exe.ignore", 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"),
        new("tools/checksum.license.txt", 95, "29472B6BE2F4E7134F09CC2FADF088CB87089853B383CA4AF29C19CC8DFC1A62"),
        new("tools/shimgen.exe", 568928, "9033F75F25BAE75853BCA02AAB9BBF1EA73D52CD256822F744820052B1CB70AF"),
        new("tools/shimgen.exe.ignore", 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"),
        new("tools/shimgen.license.txt", 3758, "D4D2EF2C520EC3E4ECFF52C867EBD28E357900E0328BB4173CB46996DED353F4"),
    });
}
