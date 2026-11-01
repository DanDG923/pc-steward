namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// N1 file identity (Task 50 design rev 6 §2, M1): the volume serial number plus the 128-bit file ID from
/// <c>GetFileInformationByHandleEx(FileIdInfo)</c>. Provider.Chocolatey cannot reference Execution, so this mirrors
/// Execution's file identity type; an architecture test pins the two <c>FILE_ID_INFO</c> layouts as identical.
/// </summary>
internal readonly record struct NativeFileIdentity(ulong VolumeSerial, UInt128 FileId);

/// <summary>Length and SHA-256 (uppercase hex) of a file read through a held handle up to end of file (§2.3, L5).</summary>
internal readonly record struct NativeObservedFile(long Length, string Sha256);
