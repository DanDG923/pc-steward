using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PcSteward.SecurityBoundarySpike
{
    public enum ResetCategory
    {
        Database,
        ScanData,
        SourceMetadata,
        Logs,
        UpdateHistory,
        Downloads,
        Settings,
        ConsentRecords,
        UserDecisions,
        ScheduledTask
    }

    public enum DeletionOutcome { Success, Partial, Denied }
    public enum HelperState { AcceptedNoOp, Denied, Expired, Cancelled }

    public sealed class Residual
    {
        public Residual(string relativePath, string reason)
        {
            RelativePath = relativePath;
            Reason = reason;
        }

        public string RelativePath { get; }
        public string Reason { get; }
    }

    public sealed class DeletionReport
    {
        public DeletionOutcome Outcome { get; internal set; }
        public int DeletedCount { get; internal set; }
        public bool AlreadyAbsent { get; internal set; }
        public int Attempts { get; internal set; }
        public IReadOnlyList<Residual> Residuals { get; internal set; } = Array.Empty<Residual>();
    }

    public sealed class HelperDecision
    {
        public HelperDecision(HelperState state, string reason)
        {
            State = state;
            Reason = reason;
        }

        public HelperState State { get; }
        public string Reason { get; }
    }

    public static class SafeDeletion
    {
        private static readonly IReadOnlyDictionary<ResetCategory, string> Manifest =
            new Dictionary<ResetCategory, string>
            {
                [ResetCategory.Database] = "database",
                [ResetCategory.ScanData] = "scan-data",
                [ResetCategory.SourceMetadata] = "source-metadata",
                [ResetCategory.Logs] = "logs",
                [ResetCategory.UpdateHistory] = "update-history",
                [ResetCategory.Downloads] = "downloads",
                [ResetCategory.Settings] = "settings",
                [ResetCategory.ConsentRecords] = "consent-records",
                [ResetCategory.UserDecisions] = "user-decisions",
                [ResetCategory.ScheduledTask] = "scheduled-task"
            };

        public static IReadOnlyDictionary<ResetCategory, string> GetResetManifest()
        {
            return Manifest;
        }

        public static string GetCategoryPath(string root, ResetCategory category)
        {
            string canonicalRoot = ValidateRoot(root);
            if (!Manifest.TryGetValue(category, out string leaf))
                throw new SecurityException("The reset category is not allowlisted.");

            return ValidateCandidate(canonicalRoot, Path.Combine(canonicalRoot, leaf));
        }

        public static string ValidateCandidate(string root, string candidate)
        {
            string canonicalRoot = ValidateRoot(root);
            RejectNonLocalOrStreamPath(candidate, "candidate");

            string canonicalCandidate = Path.GetFullPath(candidate);
            string candidateRoot = Path.GetPathRoot(canonicalCandidate) ?? "";
            string rootVolume = Path.GetPathRoot(canonicalRoot) ?? "";
            if (!StringComparer.OrdinalIgnoreCase.Equals(candidateRoot, rootVolume))
                throw new SecurityException("Alternate volumes are denied.");

            string relative = Path.GetRelativePath(canonicalRoot, canonicalCandidate);
            if (relative == "." || Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new SecurityException("The candidate is not a child of the fixed root.");

            RejectExistingReparseComponents(canonicalRoot, canonicalCandidate);
            return canonicalCandidate;
        }

        public static DeletionReport DeleteCategory(
            string root,
            ResetCategory category,
            int retryCount = 0,
            int retryDelayMilliseconds = 25,
            Action<string> beforeHandleOpen = null)
        {
            if (retryCount < 0 || retryCount > 5)
                throw new ArgumentOutOfRangeException(nameof(retryCount));
            if (retryDelayMilliseconds < 0 || retryDelayMilliseconds > 1000)
                throw new ArgumentOutOfRangeException(nameof(retryDelayMilliseconds));

            string canonicalRoot;
            string categoryPath;
            try
            {
                canonicalRoot = ValidateRoot(root);
                categoryPath = GetCategoryPath(canonicalRoot, category);
            }
            catch (Exception exception) when (
                exception is SecurityException ||
                exception is ArgumentException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return new DeletionReport
                {
                    Outcome = DeletionOutcome.Denied,
                    Attempts = 1,
                    Residuals = new[] { new Residual("<category>", exception.Message) }
                };
            }

            bool initiallyAbsent = !PathExists(categoryPath);
            int deleted = 0;
            List<Residual> residuals = new List<Residual>();
            int attempt;
            for (attempt = 1; attempt <= retryCount + 1; attempt++)
            {
                residuals = new List<Residual>();
                DeleteTree(canonicalRoot, categoryPath, residuals, ref deleted, beforeHandleOpen);
                if (!PathExists(categoryPath))
                    break;
                if (attempt <= retryCount)
                    System.Threading.Thread.Sleep(retryDelayMilliseconds);
            }

            bool remains = PathExists(categoryPath);
            return new DeletionReport
            {
                Outcome = remains ? DeletionOutcome.Partial : DeletionOutcome.Success,
                DeletedCount = deleted,
                AlreadyAbsent = initiallyAbsent,
                Attempts = Math.Min(attempt, retryCount + 1),
                Residuals = remains ? residuals : Array.Empty<Residual>()
            };
        }

        private static void DeleteTree(
            string root,
            string path,
            List<Residual> residuals,
            ref int deleted,
            Action<string> beforeHandleOpen)
        {
            if (!PathExists(path))
                return;

            FileAttributes attributes;
            try
            {
                ValidateCandidate(root, path);
                attributes = File.GetAttributes(path);
            }
            catch (Exception exception) when (
                exception is SecurityException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                residuals.Add(new Residual(ToRelative(root, path), Classify(exception)));
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                residuals.Add(new Residual(ToRelative(root, path), "ReparsePoint"));
                return;
            }

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory)
            {
                string[] children;
                try
                {
                    children = Directory.GetFileSystemEntries(path);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    residuals.Add(new Residual(ToRelative(root, path), Classify(exception)));
                    return;
                }

                foreach (string child in children)
                    DeleteTree(root, child, residuals, ref deleted, beforeHandleOpen);
            }

            try
            {
                beforeHandleOpen?.Invoke(path);
                ValidateCandidate(root, path);
                NativeDelete.DeleteExactObject(path, isDirectory);
                deleted++;
            }
            catch (FileNotFoundException)
            {
                // Another actor removed the same allowlisted object; the final existence check is authoritative.
            }
            catch (DirectoryNotFoundException)
            {
                // Another actor removed the same allowlisted object; the final existence check is authoritative.
            }
            catch (Exception exception) when (
                exception is SecurityException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                if (PathExists(path))
                    residuals.Add(new Residual(ToRelative(root, path), Classify(exception)));
            }
        }

        private static string ValidateRoot(string root)
        {
            RejectNonLocalOrStreamPath(root, "root");
            string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!Directory.Exists(canonicalRoot))
                throw new DirectoryNotFoundException("The disposable root does not exist.");

            FileAttributes attributes = File.GetAttributes(canonicalRoot);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new SecurityException("A reparse-point root is denied.");
            return canonicalRoot;
        }

        private static void RejectNonLocalOrStreamPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new SecurityException($"The {parameterName} must be an absolute path.");
            if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                path.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new SecurityException($"UNC and device {parameterName} paths are denied.");

            int firstColon = path.IndexOf(':');
            if (firstColon != 1 || path.IndexOf(':', firstColon + 1) >= 0)
                throw new SecurityException($"Alternate data stream {parameterName} paths are denied.");
        }

        private static void RejectExistingReparseComponents(string root, string candidate)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new SecurityException("A reparse-point root is denied.");

            string relative = Path.GetRelativePath(root, candidate);
            string current = root;
            foreach (string component in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if (!PathExists(current))
                    break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SecurityException("Reparse-point traversal is denied.");
            }
        }

        private static bool PathExists(string path)
        {
            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static string ToRelative(string root, string path)
        {
            try { return Path.GetRelativePath(root, path); }
            catch { return "<unavailable>"; }
        }

        private static string Classify(Exception exception)
        {
            if (exception is SecurityException) return "UnsafePath";
            if (exception is UnauthorizedAccessException) return "AccessDenied";
            if (exception is IOException io)
            {
                int code = io.HResult & 0xFFFF;
                if (code == 32 || code == 33) return "LockedOrInUse";
                if (code == 145) return "NotEmpty";
                return "IoError:" + code;
            }
            return "Denied";
        }
    }

    public static class HelperProtocol
    {
        private static readonly string[] RequiredProperties =
        {
            "protocolVersion", "jobId", "operation", "provider", "sourceId",
            "packageId", "targetVersion", "nonce", "issuedAtUtc", "authTag"
        };

        public static string CreateAuthenticatedRequest(
            Guid jobId,
            string packageId,
            string targetVersion,
            string nonce,
            DateTimeOffset issuedAtUtc,
            byte[] authenticationKey)
        {
            if (authenticationKey == null || authenticationKey.Length < 32)
                throw new ArgumentException("A 256-bit test key is required.", nameof(authenticationKey));

            var fields = new Dictionary<string, string>
            {
                ["protocolVersion"] = "1",
                ["jobId"] = jobId.ToString("D"),
                ["operation"] = "UpdatePackage",
                ["provider"] = "WinGet",
                ["sourceId"] = "winget",
                ["packageId"] = packageId,
                ["targetVersion"] = targetVersion,
                ["nonce"] = nonce,
                ["issuedAtUtc"] = issuedAtUtc.ToUniversalTime().ToString("O")
            };
            fields["authTag"] = Convert.ToBase64String(Sign(fields, authenticationKey));
            return JsonSerializer.Serialize(fields);
        }

        public static HelperDecision Evaluate(
            string requestJson,
            Guid expectedJobId,
            string expectedNonce,
            DateTimeOffset nowUtc,
            byte[] authenticationKey,
            IEnumerable<string> allowedPackageIds,
            bool cancellationRequested = false)
        {
            try
            {
                if (authenticationKey == null || authenticationKey.Length < 32)
                    return Deny("Authentication key rejected.");

                Dictionary<string, string> fields = ParseStrict(requestJson);
                if (fields["protocolVersion"] != "1")
                    return Deny("Protocol version denied.");
                if (!Guid.TryParseExact(fields["jobId"], "D", out Guid jobId) || jobId != expectedJobId)
                    return Deny("Immutable job identifier mismatch.");
                if (fields["operation"] != "UpdatePackage" ||
                    fields["provider"] != "WinGet" ||
                    fields["sourceId"] != "winget")
                    return Deny("Operation, provider, or source is not allowlisted.");
                if (allowedPackageIds == null ||
                    !new HashSet<string>(allowedPackageIds, StringComparer.Ordinal).Contains(fields["packageId"]))
                    return Deny("Package identifier is not in the reviewed job allowlist.");
                if (!IsIdentifier(fields["packageId"], 256) ||
                    !IsIdentifier(fields["targetVersion"], 128))
                    return Deny("Identifier syntax or length rejected.");
                if (!FixedTextEquals(fields["nonce"], expectedNonce))
                    return Deny("Nonce mismatch.");
                if (!DateTimeOffset.TryParseExact(
                    fields["issuedAtUtc"], "O", null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset issued))
                    return Deny("Timestamp rejected.");

                TimeSpan age = nowUtc.ToUniversalTime() - issued.ToUniversalTime();
                if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(2))
                    return new HelperDecision(HelperState.Expired, "Request lifetime expired.");

                byte[] suppliedTag;
                try { suppliedTag = Convert.FromBase64String(fields["authTag"]); }
                catch (FormatException) { return Deny("Authentication tag rejected."); }
                byte[] expectedTag = Sign(fields, authenticationKey);
                if (!CryptographicOperations.FixedTimeEquals(suppliedTag, expectedTag))
                    return Deny("Authentication failed.");
                if (cancellationRequested)
                    return new HelperDecision(HelperState.Cancelled, "Cancellation acknowledged before execution.");

                return new HelperDecision(HelperState.AcceptedNoOp, "Authenticated test request accepted; no process started.");
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is InvalidOperationException ||
                exception is KeyNotFoundException ||
                exception is SecurityException)
            {
                return Deny("Malformed or structurally unsafe request.");
            }
        }

        private static Dictionary<string, string> ParseStrict(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new SecurityException("Object required.");

            var allowed = new HashSet<string>(RequiredProperties, StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!allowed.Contains(property.Name) || result.ContainsKey(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.String)
                    throw new SecurityException("Unknown, duplicate, or non-string field.");
                result[property.Name] = property.Value.GetString() ?? "";
            }
            if (result.Count != RequiredProperties.Length || RequiredProperties.Any(p => !result.ContainsKey(p)))
                throw new SecurityException("Required field missing.");
            return result;
        }

        private static byte[] Sign(IReadOnlyDictionary<string, string> fields, byte[] key)
        {
            string canonical = string.Join("\n", RequiredProperties
                .Where(name => name != "authTag")
                .Select(name => fields[name].Length + ":" + fields[name]));
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        }

        private static bool FixedTextEquals(string left, string right)
        {
            if (left == null || right == null) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
        }

        private static bool IsIdentifier(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength) return false;
            return value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '+');
        }

        private static HelperDecision Deny(string reason)
        {
            return new HelperDecision(HelperState.Denied, reason);
        }
    }

    internal static class NativeDelete
    {
        private const uint DeleteAccess = 0x00010000;
        private const uint ReadAttributes = 0x00000080;
        private const uint ShareRead = 0x00000001;
        private const uint ShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;
        private const int FileDispositionInfo = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDisposition
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool DeleteFile;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            ref FileDisposition fileInformation,
            uint bufferSize);

        public static void DeleteExactObject(string path, bool directory)
        {
            uint flags = OpenReparsePoint | (directory ? BackupSemantics : 0);
            using SafeFileHandle handle = CreateFileW(
                path,
                DeleteAccess | ReadAttributes,
                ShareRead | ShareWrite,
                IntPtr.Zero,
                OpenExisting,
                flags,
                IntPtr.Zero);
            if (handle.IsInvalid)
                throw NewIoException("Opening the exact object for deletion failed.");

            var disposition = new FileDisposition { DeleteFile = true };
            if (!SetFileInformationByHandle(
                handle, FileDispositionInfo, ref disposition,
                (uint)Marshal.SizeOf<FileDisposition>()))
                throw NewIoException("Handle-bound deletion failed.");
        }

        private static IOException NewIoException(string message)
        {
            int error = Marshal.GetLastWin32Error();
            return new IOException(message + " Win32=" + error, unchecked((int)(0x80070000u | (uint)error)));
        }
    }
}
