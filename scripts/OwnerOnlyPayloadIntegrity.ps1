# Shared integrity primitives only: no elevation, signing, execution or deletion.
$executionPrincipal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if($executionPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    . (Join-Path $PSScriptRoot 'OwnerOnlyExecution.ps1')
    Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $PSScriptRoot
}
if (!('PCSteward.Deployment.PayloadIntegrity' -as [type])) {
    $framework=[Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
    Add-Type -ReferencedAssemblies @((Join-Path $framework 'System.dll'),(Join-Path $framework 'System.Core.dll'),(Join-Path $framework 'System.Security.dll')) -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Deployment {
    public sealed class PayloadFile {
        public string path { get; private set; }
        public long bytes { get; private set; }
        public string sha256 { get; private set; }
        public PayloadFile(string path, long bytes, string sha256) { this.path=path; this.bytes=bytes; this.sha256=sha256; }
    }
    public sealed class ImageIdentity {
        public long bytes { get; private set; }
        public string sha256 { get; private set; }
        public uint originalChecksum { get; private set; }
        public ImageIdentity(long bytes, string sha256, uint originalChecksum) { this.bytes=bytes; this.sha256=sha256; this.originalChecksum=originalChecksum; }
    }
    public static class PayloadIntegrity {
        const long MaxPayload = 256L*1024*1024;
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct StreamData { public long Size; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=296)] public string Name; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern IntPtr FindFirstStreamW(string path,int level,out StreamData data,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)] static extern bool FindNextStreamW(IntPtr find,out StreamData data);
        [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool FindClose(IntPtr find);
        static string Full(string path) {
            if (String.IsNullOrEmpty(path) || !Regex.IsMatch(path, @"^[A-Za-z]:\\") || path.IndexOf(':',2)>=0) throw new InvalidDataException("An absolute local drive path is required.");
            string full=Path.GetFullPath(path);
            DriveInfo drive=new DriveInfo(Path.GetPathRoot(full));
            if (!drive.IsReady || drive.DriveType!=DriveType.Fixed || !String.Equals(drive.DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only a local fixed NTFS volume is supported.");
            if (String.Equals(full,Path.GetPathRoot(full),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A volume root cannot be a payload input.");
            return full.TrimEnd('\\');
        }
        static void NoReparse(string path) {
            for (string current=path; !String.IsNullOrEmpty(current); current=Path.GetDirectoryName(current)) {
                if ((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Reparse path refused: "+current);
            }
        }
        static void NoStreams(string path) {
            StreamData data; IntPtr find=FindFirstStreamW(path,0,out data,0);
            if (find==new IntPtr(-1)) { if (Marshal.GetLastWin32Error()==38) return; throw new IOException("Cannot establish default-stream-only payload."); }
            try {
                do { if (!String.Equals(data.Name,"::$DATA",StringComparison.Ordinal)) throw new InvalidDataException("Alternate data stream refused."); } while (FindNextStreamW(find,out data));
                if (Marshal.GetLastWin32Error()!=38) throw new IOException("Incomplete payload stream enumeration.");
            } finally { FindClose(find); }
        }
        static void FinalPath(FileStream stream, string expected) {
            StringBuilder name=new StringBuilder(32768);
            uint size=GetFinalPathNameByHandle(stream.SafeFileHandle,name,(uint)name.Capacity,0);
            if (size==0 || size>=name.Capacity) throw new IOException("Cannot establish resolved file path.");
            string actual=name.ToString();
            if (actual.StartsWith(@"\\?\",StringComparison.Ordinal)) actual=actual.Substring(4);
            if (!String.Equals(actual,expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Resolved file escaped or aliased its approved path.");
        }
        static FileStream Open(string path) {
            string full=Full(path); NoReparse(full);
            FileStream stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
            try { FinalPath(stream,full); NoStreams(full); return stream; } catch { stream.Dispose(); throw; }
        }
        public static FileStream HoldStableFile(string path) { return Open(path); }
        public static void AssertHeldPath(FileStream stream,string path) { FinalPath(stream,Full(path)); }
        static string Hash(Stream stream) { using (SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""); }
        static string Hash(byte[] bytes) { using (MemoryStream stream=new MemoryStream(bytes,false)) return Hash(stream); }
        public static string HashHeldFile(FileStream stream) { stream.Position=0; return Hash(stream); }
        static string JsonString(string text,ref int position) {
            StringBuilder value=new StringBuilder(); position++;
            while (position<text.Length) {
                char item=text[position++]; if (item=='"') return value.ToString();
                if (item<32) throw new InvalidDataException("JSON control character.");
                if (item!='\\') { value.Append(item); continue; }
                if (position>=text.Length) break; char escape=text[position++];
                switch (escape) {
                    case '"': case '\\': case '/': value.Append(escape); break;
                    case 'b': value.Append('\b'); break; case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break; case 'r': value.Append('\r'); break; case 't': value.Append('\t'); break;
                    case 'u': if (position>text.Length-4) throw new InvalidDataException("JSON Unicode escape."); value.Append((char)Convert.ToInt32(text.Substring(position,4),16)); position+=4; break;
                    default: throw new InvalidDataException("Invalid JSON escape.");
                }
            }
            throw new InvalidDataException("Unterminated JSON string.");
        }
        public static void ValidateJsonKeys(string text) {
            Stack<HashSet<string>> objects=new Stack<HashSet<string>>();
            for (int position=0;position<text.Length;) {
                char item=text[position];
                if (item=='"') {
                    string value=JsonString(text,ref position); int next=position; while (next<text.Length && Char.IsWhiteSpace(text[next])) next++;
                    if (next<text.Length && text[next]==':') {
                        if (objects.Count==0 || objects.Peek()==null || !objects.Peek().Add(value)) throw new InvalidDataException("Duplicate or misplaced JSON key.");
                    }
                    continue;
                }
                if (item=='{' || item=='[') { if (objects.Count>=64) throw new InvalidDataException("JSON nesting bound."); objects.Push(item=='{' ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null); }
                else if (item=='}' || item==']') { if (objects.Count==0 || (objects.Peek()==null)!=(item==']')) throw new InvalidDataException("JSON delimiter mismatch."); objects.Pop(); }
                position++;
            }
            if (objects.Count!=0) throw new InvalidDataException("Unclosed JSON container.");
        }
        public static byte[] ReadPinnedFile(string path, string expected) {
            if (expected==null || !Regex.IsMatch(expected,@"\A[0-9A-Fa-f]{64}\z")) throw new InvalidDataException("Invalid approved manifest hash.");
            using (FileStream stream=Open(path)) {
                if (stream.Length<2 || stream.Length>1024*1024) throw new InvalidDataException("Manifest size is outside its bound.");
                using (MemoryStream copy=new MemoryStream()) { stream.CopyTo(copy); byte[] bytes=copy.ToArray(); if (!String.Equals(Hash(bytes),expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Approved manifest hash mismatch."); return bytes; }
            }
        }
        public static void ValidateManifest(PayloadFile[] files) {
            if (files==null || files.Length==0 || files.Length>512) throw new InvalidDataException("Invalid manifest count.");
            HashSet<string> paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total=0;
            foreach (PayloadFile file in files) {
                if (file==null || String.IsNullOrEmpty(file.path) || file.path.Length>240 || file.bytes<0 || file.bytes>MaxPayload || file.sha256==null || !Regex.IsMatch(file.sha256,@"\A[0-9A-Fa-f]{64}\z")) throw new InvalidDataException("Invalid manifest entry.");
                foreach (string part in file.path.Split('\\')) {
                    if (String.IsNullOrEmpty(part) || part.Length>255 || part=="." || part==".." || part.EndsWith(".",StringComparison.Ordinal) || part.EndsWith(" ",StringComparison.Ordinal) || part.StartsWith(" ",StringComparison.Ordinal) || !Regex.IsMatch(part,@"\A[A-Za-z0-9_. -]+\z") || Regex.IsMatch(part,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",RegexOptions.IgnoreCase)) throw new InvalidDataException("Noncanonical payload path: "+file.path);
                }
                if (!paths.Add(file.path)) throw new InvalidDataException("Duplicate/case-colliding payload path.");
                total=checked(total+file.bytes); if (total>MaxPayload) throw new InvalidDataException("Payload exceeds byte bound.");
            }
            foreach (string path in paths) for (string parent=Path.GetDirectoryName(path); !String.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent)) if (paths.Contains(parent)) throw new InvalidDataException("File/directory collision.");
        }
        static HashSet<string> Directories(PayloadFile[] files) {
            HashSet<string> result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PayloadFile file in files) for (string parent=Path.GetDirectoryName(file.path); !String.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent)) result.Add(parent);
            return result;
        }
        static void Walk(string root, string current, HashSet<string> paths, HashSet<string> directories) {
            NoReparse(current); NoStreams(current);
            foreach (FileSystemInfo item in new DirectoryInfo(current).GetFileSystemInfos()) {
                if ((item.Attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Payload reparse point refused.");
                string relative=item.FullName.Substring(root.Length+1);
                if ((item.Attributes&FileAttributes.Directory)!=0) { if (!directories.Contains(relative)) throw new InvalidDataException("Unexpected payload directory."); Walk(root,item.FullName,paths,directories); }
                else if (!paths.Remove(relative)) throw new InvalidDataException("Unexpected/case-colliding payload file.");
            }
        }
        public static void AssertTree(string root, PayloadFile[] files) {
            ValidateManifest(files); root=Full(root); NoReparse(root);
            HashSet<string> remaining=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (PayloadFile file in files) remaining.Add(file.path);
            Walk(root,root,remaining,Directories(files)); if (remaining.Count!=0) throw new InvalidDataException("Payload files are missing.");
            foreach (PayloadFile file in files) using (FileStream stream=Open(Path.Combine(root,file.path))) {
                if (stream.Length!=file.bytes || !String.Equals(Hash(stream),file.sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Payload bytes/hash mismatch: "+file.path);
            }
        }
        static void Protect(FileSystemSecurity security) {
            security.SetOwner(new SecurityIdentifier("S-1-5-32-544")); security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"),FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"),FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),FileSystemRights.ReadAndExecute|FileSystemRights.Synchronize,AccessControlType.Allow));
        }
        static FileStream CreateProtected(string path,FileMode mode) {
            FileSecurity security=new FileSecurity(); Protect(security);
            return new FileStream(path,mode,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,security);
        }
        public static void WriteProtectedFile(string path,byte[] data) {
            path=Full(path); NoReparse(Path.GetDirectoryName(path));
            if (File.Exists(path)) NoReparse(path);
            using (FileStream stream=CreateProtected(path,FileMode.OpenOrCreate)) { FinalPath(stream,path); stream.SetLength(0); stream.Write(data,0,data.Length); stream.Flush(true); }
        }
        public static void CopyTree(string source,string destination,PayloadFile[] files) { CopyTreeCore(source,destination,files,false); }
        public static void CopyTreeProtected(string source,string destination,PayloadFile[] files) { CopyTreeCore(source,destination,files,true); }
        static void CopyTreeCore(string source, string destination, PayloadFile[] files,bool protect) {
            AssertTree(source,files); source=Full(source); destination=Full(destination); NoReparse(destination);
            if (new DirectoryInfo(destination).GetFileSystemInfos().Length!=0) throw new InvalidDataException("Staging destination is not empty.");
            List<string> directories=new List<string>(Directories(files)); directories.Sort(delegate(string a,string b) { return a.Length.CompareTo(b.Length); });
            foreach (string directory in directories) {
                string path=Path.Combine(destination,directory); NoReparse(Path.GetDirectoryName(path));
                if (protect) { DirectorySecurity security=new DirectorySecurity(); Protect(security); Directory.CreateDirectory(path,security); }
                else Directory.CreateDirectory(path);
            }
            foreach (PayloadFile file in files) {
                string target=Path.Combine(destination,file.path); NoReparse(Path.GetDirectoryName(target));
                using (FileStream input=Open(Path.Combine(source,file.path))) {
                    if (input.Length!=file.bytes) throw new InvalidDataException("Source changed before copy.");
                    using (FileStream output=protect ? CreateProtected(target,FileMode.CreateNew) : new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { FinalPath(output,target); input.CopyTo(output); output.Flush(true); }
                }
            }
            AssertTree(destination,files);
        }
        sealed class Pe {
            internal int Checksum, Security, ImageEnd; internal uint CertificateOffset, CertificateSize, FileAlignment;
        }
        static uint U32(byte[] data, int offset) { if (offset<0 || offset>data.Length-4) throw new InvalidDataException("Truncated PE."); return BitConverter.ToUInt32(data,offset); }
        static ushort U16(byte[] data,int offset) { if (offset<0 || offset>data.Length-2) throw new InvalidDataException("Truncated PE."); return BitConverter.ToUInt16(data,offset); }
        static Pe Parse(byte[] data) {
            if (data.Length<512 || U16(data,0)!=0x5a4d) throw new InvalidDataException("Invalid PE DOS header.");
            int header=checked((int)U32(data,60)); int optional=checked(header+24);
            if (U32(data,header)!=0x4550 || U16(data,header+4)!=0x8664 || U16(data,header+20)!=240 || U16(data,optional)!=0x20b || U32(data,optional+108)<5) throw new InvalidDataException("Only standard PE32+ x64 apphosts are supported.");
            int count=U16(data,header+6); if (count==0 || count>96) throw new InvalidDataException("Invalid PE section count.");
            uint alignment=U32(data,optional+36);
            if (alignment<512 || alignment>65536 || (alignment&(alignment-1))!=0) throw new InvalidDataException("Unsupported PE FileAlignment.");
            int end=checked((int)U32(data,optional+60)); int sectionTable=checked(optional+240);
            if (end<sectionTable+count*40 || end>data.Length || end%alignment!=0) throw new InvalidDataException("Invalid PE headers extent.");
            List<long[]> sections=new List<long[]>();
            for (int i=0;i<count;i++) { int section=checked(sectionTable+i*40); uint size=U32(data,section+16), start=U32(data,section+20); if (size==0) continue; long stop=(long)start+size; if (start<end || stop>data.Length || start%alignment!=0 || size%alignment!=0) throw new InvalidDataException("Invalid PE section extent."); sections.Add(new long[] {start,stop}); }
            sections.Sort(delegate(long[] a,long[] b) { return a[0].CompareTo(b[0]); });
            long last=end; foreach (long[] section in sections) { if (section[0]<last) throw new InvalidDataException("Overlapping PE sections."); last=section[1]; }
            Pe pe=new Pe(); pe.Checksum=optional+64; pe.Security=optional+144; pe.ImageEnd=checked((int)last); pe.CertificateOffset=U32(data,pe.Security); pe.CertificateSize=U32(data,pe.Security+4); pe.FileAlignment=alignment; return pe;
        }
        static byte[] ReadImage(string path) {
            using (FileStream stream=Open(path)) { if (stream.Length>16*1024*1024) throw new InvalidDataException("Apphost exceeds image bound."); using (MemoryStream copy=new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); } }
        }
        static string Reconstruct(byte[] bytes, Pe pe, ImageIdentity identity) {
            byte[] prefix=new byte[(int)identity.bytes]; Buffer.BlockCopy(bytes,0,prefix,0,prefix.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(identity.originalChecksum),0,prefix,pe.Checksum,4); Array.Clear(prefix,pe.Security,8); return Hash(prefix);
        }
        public static ImageIdentity GetUnsignedImage(string path) {
            byte[] data=ReadImage(path); Pe pe=Parse(data);
            if (pe.CertificateOffset!=0 || pe.CertificateSize!=0 || data.Length!=pe.ImageEnd) throw new InvalidDataException("Candidate must be unsigned with no overlay.");
            return new ImageIdentity(data.Length,Hash(data),U32(data,pe.Checksum));
        }
        public static void AssertSignedImage(string path, ImageIdentity identity) {
            if (identity==null || identity.bytes<512 || identity.bytes>16*1024*1024 || identity.sha256==null || !Regex.IsMatch(identity.sha256,@"\A[0-9A-Fa-f]{64}\z")) throw new InvalidDataException("Invalid original image identity.");
            byte[] data=ReadImage(path); Pe pe=Parse(data);
            if (identity.bytes%pe.FileAlignment!=0 || pe.ImageEnd!=identity.bytes || pe.CertificateOffset!=identity.bytes || pe.CertificateSize<8 || pe.CertificateSize>65536 || (long)pe.CertificateOffset+pe.CertificateSize!=data.Length) throw new InvalidDataException("Signing changed image/certificate/overlay extent.");
            int offset=checked((int)pe.CertificateOffset); uint recordLength=U32(data,offset);
            if (recordLength<8 || recordLength>pe.CertificateSize || ((recordLength+7L)&~7L)!=pe.CertificateSize || U16(data,offset+4)!=0x200 || U16(data,offset+6)!=2) throw new InvalidDataException("Invalid single Authenticode certificate record.");
            for (long index=offset+(long)recordLength;index<data.Length;index++) if (data[(int)index]!=0) throw new InvalidDataException("Unexpected certificate padding.");
            if (!String.Equals(Reconstruct(data,pe,identity),identity.sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Signed executable differs from approved original raw hash.");
        }
        sealed class Der { internal byte Tag; internal int Content,End; }
        static Der ReadDer(byte[] data,ref int position,int limit) {
            if (position<0 || position>limit-2 || limit>data.Length) throw new InvalidDataException("Truncated DER.");
            byte tag=data[position++]; if ((tag&31)==31) throw new InvalidDataException("Unsupported DER high tag.");
            int length=data[position++];
            if ((length&128)!=0) {
                int count=length&127; if (count==0 || count>4 || position>limit-count || data[position]==0) throw new InvalidDataException("Nonminimal or indefinite DER.");
                long size=0; for (int i=0;i<count;i++) size=(size<<8)|data[position++];
                if (size<128 || size>Int32.MaxValue) throw new InvalidDataException("Invalid DER length."); length=(int)size;
            }
            if (length>limit-position) throw new InvalidDataException("DER extent overflow.");
            Der result=new Der(); result.Tag=tag; result.Content=position; result.End=position+length; position=result.End; return result;
        }
        static List<Der> Children(byte[] data,Der value) {
            List<Der> result=new List<Der>(); int position=value.Content;
            while (position<value.End) result.Add(ReadDer(data,ref position,value.End)); return result;
        }
        static void DerTree(byte[] data,Der value,int depth) {
            if (depth>32) throw new InvalidDataException("DER nesting bound.");
            if ((value.Tag&32)!=0) foreach (Der child in Children(data,value)) DerTree(data,child,depth+1);
        }
        static bool Bytes(byte[] data,Der value,byte[] expected) {
            if (value.End-value.Content!=expected.Length) return false;
            for (int i=0;i<expected.Length;i++) if (data[value.Content+i]!=expected[i]) return false; return true;
        }
        static void Sha256Algorithm(byte[] data,Der value) {
            if (value.Tag!=0x30) throw new InvalidDataException("Invalid digest algorithm.");
            List<Der> fields=Children(data,value);
            if (fields.Count<1 || fields.Count>2 || fields[0].Tag!=6 || !Bytes(data,fields[0],new byte[] {0x60,0x86,0x48,1,0x65,3,4,2,1}) || (fields.Count==2 && (fields[1].Tag!=5 || fields[1].Content!=fields[1].End))) throw new InvalidDataException("SHA-256 is required.");
        }
        public static void AssertSignerBlob(string path,string expectedThumbprint) {
            if (expectedThumbprint==null || !Regex.IsMatch(expectedThumbprint,@"\A[0-9A-Fa-f]{40}\z")) throw new InvalidDataException("Invalid expected signer.");
            byte[] data=ReadImage(path); Pe pe=Parse(data);
            if (pe.CertificateSize<8 || pe.CertificateSize>65536 || (long)pe.CertificateOffset+pe.CertificateSize!=data.Length) throw new InvalidDataException("Certificate bounds.");
            int offset=checked((int)pe.CertificateOffset); uint record=U32(data,offset);
            if (record<8 || record>pe.CertificateSize) throw new InvalidDataException("Certificate record bounds.");
            byte[] blob=new byte[record-8]; Buffer.BlockCopy(data,offset+8,blob,0,blob.Length);
            int position=0; Der outer=ReadDer(blob,ref position,blob.Length);
            if (outer.Tag!=0x30 || blob.Length-outer.End>7) throw new InvalidDataException("Invalid PKCS7 envelope.");
            for (int i=outer.End;i<blob.Length;i++) if (blob[i]!=0) throw new InvalidDataException("Nonzero data after PKCS7.");
            DerTree(blob,outer,0);
            List<Der> contentInfo=Children(blob,outer);
            if (contentInfo.Count!=2 || contentInfo[0].Tag!=6 || !Bytes(blob,contentInfo[0],new byte[] {0x2a,0x86,0x48,0x86,0xf7,0x0d,1,7,2}) || contentInfo[1].Tag!=0xa0) throw new InvalidDataException("Invalid SignedData envelope.");
            List<Der> wrapper=Children(blob,contentInfo[1]); if (wrapper.Count!=1 || wrapper[0].Tag!=0x30) throw new InvalidDataException("Invalid SignedData.");
            List<Der> fields=Children(blob,wrapper[0]);
            if (fields.Count!=5 || fields[0].Tag!=2 || fields[1].Tag!=0x31 || fields[2].Tag!=0x30 || fields[3].Tag!=0xa0 || fields[4].Tag!=0x31) throw new InvalidDataException("Unexpected certificate/CRL/SignedData fields.");
            List<Der> algorithms=Children(blob,fields[1]); if (algorithms.Count!=1) throw new InvalidDataException("Exactly one digest algorithm required."); Sha256Algorithm(blob,algorithms[0]);
            byte[] exact=new byte[outer.End]; Buffer.BlockCopy(blob,0,exact,0,exact.Length);
            SignedCms cms=new SignedCms(); cms.Decode(exact);
            if (cms.Detached || cms.ContentInfo.ContentType.Value!="1.3.6.1.4.1.311.2.1.4" || cms.Certificates.Count!=1 || cms.SignerInfos.Count!=1) throw new InvalidDataException("Unsupported Authenticode CMS profile.");
            SignerInfo signer=cms.SignerInfos[0];
            if (signer.Certificate==null || !String.Equals(signer.Certificate.Thumbprint,expectedThumbprint,StringComparison.OrdinalIgnoreCase) || signer.DigestAlgorithm.Value!="2.16.840.1.101.3.4.2.1" || (signer.SignatureAlgorithm.Value!="1.2.840.113549.1.1.1" && signer.SignatureAlgorithm.Value!="1.2.840.113549.1.1.11") || signer.UnsignedAttributes.Count!=0 || signer.CounterSignerInfos.Count!=0) throw new InvalidDataException("Unsupported signer/digest/unsigned attributes.");
            HashSet<string> allowed=new HashSet<string>(StringComparer.Ordinal) {"1.2.840.113549.1.9.3","1.2.840.113549.1.9.4","1.3.6.1.4.1.311.2.1.12","1.3.6.1.4.1.311.2.1.11"};
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach (CryptographicAttributeObject attribute in signer.SignedAttributes) if (!allowed.Contains(attribute.Oid.Value) || !seen.Add(attribute.Oid.Value) || attribute.Values.Count!=1) throw new InvalidDataException("Unexpected signed attribute.");
            if (!seen.Contains("1.2.840.113549.1.9.3") || !seen.Contains("1.2.840.113549.1.9.4")) throw new InvalidDataException("Required signed attributes missing.");
            byte[] indirect=cms.ContentInfo.Content; position=0; Der indirectSequence=ReadDer(indirect,ref position,indirect.Length);
            if (indirectSequence.Tag!=0x30 || indirectSequence.End!=indirect.Length) throw new InvalidDataException("Invalid SpcIndirectData."); DerTree(indirect,indirectSequence,0);
            List<Der> indirectFields=Children(indirect,indirectSequence); if (indirectFields.Count!=2 || indirectFields[1].Tag!=0x30) throw new InvalidDataException("Invalid PE DigestInfo.");
            if (indirectFields[0].Tag!=0x30) throw new InvalidDataException("Invalid indirect-data type.");
            List<Der> imageFields=Children(indirect,indirectFields[0]);
            if (imageFields.Count!=2 || imageFields[0].Tag!=6 || !Bytes(indirect,imageFields[0],new byte[] {0x2b,6,1,4,1,0x82,0x37,2,1,0x0f})) throw new InvalidDataException("Expected SPC_PE_IMAGE_DATAOBJ.");
            List<Der> digestFields=Children(indirect,indirectFields[1]); if (digestFields.Count!=2 || digestFields[1].Tag!=4 || digestFields[1].End-digestFields[1].Content!=32) throw new InvalidDataException("Expected a 32-byte PE digest."); Sha256Algorithm(indirect,digestFields[0]);
            cms.CheckSignature(true); // Cryptographic CMS check only; Windows Valid remains separately mandatory.
        }
    }
}
'@
}

# ADR-022 alignment note (protocol-2 capability statement). The next three functions are pure: no file, store or host
# access. ConvertFrom-OwnerOnlyDeploymentCapability mirrors DeploymentCapabilityPolicy.TryParseCapability byte for byte.
function ConvertFrom-OwnerOnlyDeploymentCapability {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)
    $malformed = 'Malformed deployment capability statement.'
    if ($Bytes.Length -eq 0 -or $Bytes.Length -gt 1024 -or $Bytes[$Bytes.Length - 1] -ne 10) { throw $malformed }
    foreach ($value in $Bytes) { if ($value -ne 10 -and ($value -lt 0x20 -or $value -gt 0x7E)) { throw $malformed } }
    $lines = [Text.Encoding]::ASCII.GetString($Bytes, 0, $Bytes.Length - 1).Split([char]10)
    if ($lines.Length -lt 3 -or $lines.Length -gt 10 -or $lines[0] -cne 'pc-steward-deployment-capability:1' -or !$lines[1].StartsWith('helper-protocol:', [StringComparison]::Ordinal)) { throw $malformed }
    $protocol = $lines[1].Substring(16)
    if ($protocol.Length -eq 0) { throw $malformed }
    # Task 50 activation (ADR-031 owner decision 7): mirrors DeploymentCapabilityPolicy.CapabilityOperationAllowlist.
    $allowed = @('Update/ChocolateyClientSelfUpdateV1', 'Update/ChocolateyMachineRuntimeLimitsV3', 'Update/WinGetMachineSilentV1')
    $operations = [Collections.Generic.List[string]]::new(); $previous = $null
    for ($index = 2; $index -lt $lines.Length; $index++) {
        if (!$lines[$index].StartsWith('operation:', [StringComparison]::Ordinal)) { throw $malformed }
        $token = $lines[$index].Substring(10)
        if ($token -cnotin $allowed -or ($null -ne $previous -and [string]::CompareOrdinal($previous, $token) -ge 0)) { throw $malformed }
        $operations.Add($token); $previous = $token
    }
    [pscustomobject]@{ HelperProtocol = $protocol; Operations = $operations.ToArray() }
}

function Assert-OwnerOnlyDeploymentCapabilityBytes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)
    $capability = ConvertFrom-OwnerOnlyDeploymentCapability -Bytes $Bytes
    # Each desktop/helper pair accepts exactly one protocol; the compiled value is ReviewedUpdateWireCodec.ProtocolVersion.
    if ($capability.HelperProtocol -cne '2') { throw 'The deployment capability statement names an unsupported helper protocol.' }
    $capability
}

function Get-OwnerOnlyCandidatePayloadOrigin {
    param([Parameter(Mandatory)][object[]]$Files)
    $leaf = 'PCSteward.DeploymentCapability.txt'
    $capability = @($Files | Where-Object { $_.path -ieq $leaf -or $_.path -ilike ('*\' + $leaf) })
    if ($capability.Count -eq 0) { return 'pinned-unsigned-v1' }
    if ($capability.Count -ne 1 -or $capability[0].path -cne $leaf -or $capability[0].bytes -lt 1 -or $capability[0].bytes -gt 1024) { throw 'Malformed pinned deployment capability entry.' }
    return 'pinned-unsigned-v2'
}

function Read-OwnerOnlyDeploymentCapability {
    param([Parameter(Mandatory)][string]$PayloadRoot, [Parameter(Mandatory)][PCSteward.Deployment.PayloadFile]$Entry)
    $lease = [PCSteward.Deployment.PayloadIntegrity]::HoldStableFile((Join-Path $PayloadRoot $Entry.path))
    try {
        if ($lease.Length -ne $Entry.bytes -or $lease.Length -gt 1024) { throw 'The pinned deployment capability size differs from the approved manifest.' }
        $bytes = [byte[]]::new([int]$lease.Length); $read = 0
        while ($read -lt $bytes.Length) { $count = $lease.Read($bytes, $read, $bytes.Length - $read); if ($count -le 0) { throw 'Truncated deployment capability read.' }; $read += $count }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') } finally { $sha.Dispose() }
        if (![string]::Equals($hash, $Entry.sha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'The pinned deployment capability hash differs from the approved manifest.' }
        Assert-OwnerOnlyDeploymentCapabilityBytes -Bytes $bytes
    } finally { $lease.Dispose() }
}

function Read-OwnerOnlyCandidateManifest {
    # A manifest that pins PCSteward.DeploymentCapability.txt is a protocol-2 payload: its capability bytes are validated
    # from PayloadRoot (required then) before any caller can sign or install it.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ExpectedSha256, [string]$PayloadRoot)
    $raw = [PCSteward.Deployment.PayloadIntegrity]::ReadPinnedFile($Path,$ExpectedSha256)
    $text = [Text.UTF8Encoding]::new($false,$true).GetString($raw).TrimStart([char]0xfeff)
    if (!$text.TrimStart().StartsWith('[')) { throw 'Candidate manifest must be an array.' }
    [PCSteward.Deployment.PayloadIntegrity]::ValidateJsonKeys($text)
    $entries = ConvertFrom-Json -InputObject $text
    $files = [Collections.Generic.List[PCSteward.Deployment.PayloadFile]]::new()
    foreach ($entry in $entries) {
        $properties = @($entry.PSObject.Properties.Name | Sort-Object)
        if (($properties -join ',') -cne 'bytes,path,sha256' -or $entry.path -isnot [string] -or $entry.sha256 -isnot [string] -or ($entry.bytes -isnot [int] -and $entry.bytes -isnot [long])) { throw 'Malformed candidate manifest entry.' }
        $files.Add([PCSteward.Deployment.PayloadFile]::new($entry.path,[long]$entry.bytes,$entry.sha256))
    }
    $array = $files.ToArray()
    [PCSteward.Deployment.PayloadIntegrity]::ValidateManifest($array)
    foreach ($required in @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe','PCSteward.ScheduledHost.dll','App.xbf','PCSteward.Desktop.pri')) {
        if (@($array | Where-Object { $_.path -ceq $required }).Count -ne 1) { throw "Missing required candidate payload: $required" }
    }
    if (@($array | Where-Object { $_.path -cmatch '^Resources\\.+\.xbf$' }).Count -eq 0) { throw 'Missing compiled XBF resources.' }
    $origin = Get-OwnerOnlyCandidatePayloadOrigin -Files $array
    $helperProtocol = $null
    if ($origin -ceq 'pinned-unsigned-v2') {
        if (!$PayloadRoot) { throw 'A protocol-2 candidate manifest requires its payload root to validate the pinned capability statement.' }
        $helperProtocol = (Read-OwnerOnlyDeploymentCapability -PayloadRoot $PayloadRoot -Entry @($array | Where-Object { $_.path -ceq 'PCSteward.DeploymentCapability.txt' })[0]).HelperProtocol
    }
    [pscustomobject]@{ Files=$array; Raw=$raw; Sha256=$ExpectedSha256.ToUpperInvariant(); PayloadOrigin=$origin; HelperProtocol=$helperProtocol }
}

function Assert-OwnerOnlyPayloadProvenance {
    param([Parameter(Mandatory)][string]$InstallRoot, [Parameter(Mandatory)][string]$StateRoot, [Parameter(Mandatory)]$State)
    $origin = $State.PSObject.Properties['payloadOrigin']
    $hasOtherFields = $null -ne $State.PSObject.Properties['candidateManifestSha256'] -or $null -ne $State.PSObject.Properties['executableImages']
    if (!$origin) { if ($hasOtherFields -or (Test-Path -LiteralPath (Join-Path $StateRoot 'candidate-manifest.json'))) { throw 'Incomplete pinned-payload provenance.' }; return 'legacy-v6' }
    if ($State.payloadOrigin -isnot [string] -or $State.payloadOrigin -cnotin @('pinned-unsigned-v1','pinned-unsigned-v2') -or !$hasOtherFields) { throw 'Unrecognised pinned-payload provenance.' }
    $manifest = Read-OwnerOnlyCandidateManifest -Path (Join-Path $StateRoot 'candidate-manifest.json') -ExpectedSha256 $State.candidateManifestSha256 -PayloadRoot $InstallRoot
    # pinned-unsigned-v2 is recognised only with a valid pinned capability statement, and pinned-unsigned-v1 only without one.
    if ($manifest.PayloadOrigin -cne $State.payloadOrigin) { throw 'Unrecognised pinned-payload provenance.' }
    $signedLeaves = @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')
    $images = @($State.executableImages)
    if ($images.Count -ne 2) { throw 'Exactly two original executable identities are required.' }
    # Keep complete paths/lengths and all non-signed hashes exact. Signed apphost lengths change.
    $installed = [Collections.Generic.List[PCSteward.Deployment.PayloadFile]]::new()
    foreach ($file in $manifest.Files) {
        if ($file.path -cin $signedLeaves) {
            $image = @($images | Where-Object { $_.path -ceq $file.path })
            if ($image.Count -ne 1 -or $image[0].bytes -ne $file.bytes) { throw 'Original executable identity is absent or inconsistent.' }
            if (($image[0].originalChecksum -isnot [int] -and $image[0].originalChecksum -isnot [long]) -or $image[0].originalChecksum -lt 0 -or $image[0].originalChecksum -gt [uint32]::MaxValue) { throw 'Invalid original executable checksum.' }
            $identity = [PCSteward.Deployment.ImageIdentity]::new($file.bytes,$file.sha256,[uint32]$image[0].originalChecksum)
            $path = Join-Path $InstallRoot $file.path
            $verified = Test-OwnerOnlySignedExecutable -Path $path -Identity $identity -ExpectedThumbprint $State.certificateThumbprint
            $ledger = @($State.files | Where-Object { $_.path -ceq $file.path })
            if ($ledger.Count -ne 1 -or $ledger[0].sha256 -cne $verified.sha256) { throw 'Held signed-executable hash differs from protected ledger.' }
            $installed.Add($verified)
        } else { $installed.Add($file) }
    }
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($InstallRoot,$installed.ToArray())
    return $manifest.PayloadOrigin
}

function Test-OwnerOnlySignedExecutable {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][PCSteward.Deployment.ImageIdentity]$Identity,[Parameter(Mandatory)][string]$ExpectedThumbprint)
    $lease = [PCSteward.Deployment.PayloadIntegrity]::HoldStableFile($Path)
    try {
        $length=$lease.Length; $modified=[IO.File]::GetLastWriteTimeUtc($Path)
        [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($Path,$Identity)
        [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($Path,$ExpectedThumbprint)
        $signature=Get-AuthenticodeSignature -LiteralPath $Path
        if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or !$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint) { throw 'Trusted same-signer embedded Authenticode signature required.' }
        [PCSteward.Deployment.PayloadIntegrity]::AssertHeldPath($lease,$Path)
        if ($lease.Length -ne $length -or [IO.File]::GetLastWriteTimeUtc($Path) -ne $modified) { throw 'Signed executable changed during validation.' }
        $hash=[PCSteward.Deployment.PayloadIntegrity]::HashHeldFile($lease)
        [PCSteward.Deployment.PayloadIntegrity]::AssertHeldPath($lease,$Path)
        return [PCSteward.Deployment.PayloadFile]::new([IO.Path]::GetFileName($Path),$length,$hash)
    } finally { $lease.Dispose() }
}

function Assert-OwnerOnlyPayloadProfile {
    param([Parameter(Mandatory)][string]$Origin,[switch]$AllowLegacyPayload)
    if ($Origin -cin @('pinned-unsigned-v1','pinned-unsigned-v2')) { return }
    if ($AllowLegacyPayload -and $Origin -ceq 'legacy-v6') { return }
    throw 'The installed deployment is legacy or unsupported; pinned-payload evidence is required.'
}

function Write-OwnerOnlyProtectedJson {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)]$Value)
    $json=ConvertTo-Json -InputObject $Value -Depth 7
    [PCSteward.Deployment.PayloadIntegrity]::WriteProtectedFile($Path,[Text.UTF8Encoding]::new($false).GetBytes($json))
}
