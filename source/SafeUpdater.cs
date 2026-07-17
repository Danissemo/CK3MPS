using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace CK3MPS
{
    internal static class SafeUpdater
    {
        internal const string ApplyArgument = "--apply-update";
        internal const string HealthArgument = "--update-health-check";
        private const string TrustedRepository = "Danissemo/CK3MPS";
        private const string TrustedPublisherSubject = "CN=Danissemo";
        private const int DefaultHealthTimeoutSeconds = 30;

        [DataContract]
        internal sealed class UpdateRequest
        {
            [DataMember(Name = "packagePath")] public string PackagePath;
            [DataMember(Name = "manifestPath")] public string ManifestPath;
            [DataMember(Name = "installRoot")] public string InstallRoot;
            [DataMember(Name = "stagingRoot")] public string StagingRoot;
            [DataMember(Name = "currentVersion")] public string CurrentVersion;
            [DataMember(Name = "targetVersion")] public string TargetVersion;
            [DataMember(Name = "allowDowngrade")] public bool AllowDowngrade;
        }

        [DataContract]
        internal sealed class PackageManifest
        {
            [DataMember(Name = "schemaVersion")] public int SchemaVersion;
            [DataMember(Name = "repository")] public string Repository;
            [DataMember(Name = "version")] public string Version;
            [DataMember(Name = "packageAsset")] public string PackageAsset;
            [DataMember(Name = "packageSha256")] public string PackageSha256;
            [DataMember(Name = "publisherSubject")] public string PublisherSubject;
            [DataMember(Name = "healthTimeoutSeconds")] public int HealthTimeoutSeconds;
            [DataMember(Name = "files")] public PackageFile[] Files;
        }

        [DataContract]
        internal sealed class PackageFile
        {
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "sha256")] public string Sha256;
            [DataMember(Name = "signed")] public bool Signed;
        }

        [DataContract]
        private sealed class UpdateJournal
        {
            [DataMember(Name = "stage")] public string Stage;
            [DataMember(Name = "installRoot")] public string InstallRoot;
            [DataMember(Name = "recoveryRoot")] public string RecoveryRoot;
            [DataMember(Name = "newRoot")] public string NewRoot;
            [DataMember(Name = "files")] public List<JournalFile> Files;
        }

        [DataContract]
        private sealed class JournalFile
        {
            [DataMember(Name = "relativePath")] public string RelativePath;
            [DataMember(Name = "existed")] public bool Existed;
        }

        internal static bool TryHandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0)
                return false;

            if (String.Equals(args[0], HealthArgument, StringComparison.Ordinal))
            {
                if (args.Length != 2)
                    return true;
                string token = CanonicalFile(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(token));
                File.WriteAllText(token, "healthy", new UTF8Encoding(false));
                return true;
            }

            if (!String.Equals(args[0], ApplyArgument, StringComparison.Ordinal))
                return false;

            if (args.Length != 2)
                throw new InvalidOperationException("Updater request path is missing.");

            Apply(CanonicalFile(args[1]));
            return true;
        }

        private static void Apply(string requestPath)
        {
            UpdateRequest request = ReadJson<UpdateRequest>(requestPath);
            ValidateRequest(request);
            string installRoot = CanonicalDirectory(request.InstallRoot);
            string stagingRoot = CanonicalDirectory(request.StagingRoot);
            RejectReparsePath(stagingRoot);
            RequireContainedFile(stagingRoot, request.PackagePath);
            RequireContainedFile(stagingRoot, request.ManifestPath);

            PackageManifest manifest = ReadJson<PackageManifest>(CanonicalFile(request.ManifestPath));
            ValidateManifest(request, manifest);
            VerifyHash(CanonicalFile(request.PackagePath), manifest.PackageSha256, "package");
            EnsureFreeSpace(stagingRoot, new FileInfo(request.PackagePath).Length * 3L);

            string transactionId = Guid.NewGuid().ToString("N");
            string installParent = Directory.GetParent(installRoot).FullName;
            string transactionRoot = Path.Combine(installParent, ".ck3mps-update-" + transactionId);
            string newRoot = Path.Combine(transactionRoot, "new");
            string recoveryRoot = Path.Combine(transactionRoot, "recovery");
            string journalPath = Path.Combine(transactionRoot, "update-journal.json");
            Directory.CreateDirectory(newRoot);
            Directory.CreateDirectory(recoveryRoot);

            UpdateJournal journal = new UpdateJournal
            {
                Stage = "Prepared",
                InstallRoot = installRoot,
                RecoveryRoot = recoveryRoot,
                NewRoot = newRoot,
                Files = new List<JournalFile>()
            };
            WriteJsonAtomic(journalPath, journal);

            try
            {
                ExtractVerifiedPackage(request.PackagePath, newRoot, manifest);
                journal.Stage = "Verified";
                WriteJsonAtomic(journalPath, journal);

                foreach (PackageFile file in manifest.Files ?? new PackageFile[0])
                {
                    string relative = ValidateRelativeAppPath(file.Path);
                    string source = ContainedPath(newRoot, relative);
                    string destination = ContainedPath(installRoot, relative);
                    string backup = ContainedPath(recoveryRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    bool existed = File.Exists(destination);
                    journal.Files.Add(new JournalFile { RelativePath = relative, Existed = existed });
                    WriteJsonAtomic(journalPath, journal);

                    if (existed)
                        File.Replace(source, destination, backup, true);
                    else
                        File.Move(source, destination);
                }

                journal.Stage = "Replaced";
                WriteJsonAtomic(journalPath, journal);

                int timeout = manifest.HealthTimeoutSeconds > 0 ? manifest.HealthTimeoutSeconds : DefaultHealthTimeoutSeconds;
                if (!RunHealthCheck(ContainedPath(installRoot, "CK3MPS.exe"), transactionRoot, timeout))
                    throw new InvalidOperationException("The new CK3MPS process did not pass the startup health check.");

                journal.Stage = "Healthy";
                WriteJsonAtomic(journalPath, journal);
                TryDeleteDirectory(transactionRoot);
                StartApplication(ContainedPath(installRoot, "CK3MPS.exe"));
            }
            catch
            {
                Rollback(journal);
                throw;
            }
        }

        private static void ValidateRequest(UpdateRequest request)
        {
            if (request == null)
                throw new InvalidDataException("Update request is empty.");
            if (String.IsNullOrWhiteSpace(request.PackagePath) || String.IsNullOrWhiteSpace(request.ManifestPath)
                || String.IsNullOrWhiteSpace(request.InstallRoot) || String.IsNullOrWhiteSpace(request.StagingRoot))
                throw new InvalidDataException("Update request is incomplete.");
            int comparison = VersionUtilities.CompareReleaseTags(request.TargetVersion, request.CurrentVersion);
            if (comparison < 0 && !request.AllowDowngrade)
                throw new InvalidOperationException("Downgrade is blocked. Use the explicit advanced downgrade action.");
            if (comparison == 0)
                throw new InvalidOperationException("The requested update version is already installed.");
        }

        private static void ValidateManifest(UpdateRequest request, PackageManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported update manifest schema.");
            if (!String.Equals(manifest.Repository, TrustedRepository, StringComparison.Ordinal))
                throw new InvalidDataException("Update manifest repository is not allowlisted.");
            if (!String.Equals(manifest.Version, request.TargetVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update manifest version does not match the selected release.");
            string expectedAsset = "CK3MPS-" + NormalizeVersion(request.TargetVersion) + ".zip";
            if (!String.Equals(manifest.PackageAsset, expectedAsset, StringComparison.Ordinal))
                throw new InvalidDataException("Update package asset name is not exact.");
            if (!String.Equals(manifest.PublisherSubject, TrustedPublisherSubject, StringComparison.Ordinal))
                throw new InvalidDataException("Update publisher is not allowlisted.");
            if (manifest.Files == null || manifest.Files.Length == 0)
                throw new InvalidDataException("Update manifest contains no files.");
        }

        private static void ExtractVerifiedPackage(string packagePath, string destinationRoot, PackageManifest manifest)
        {
            Dictionary<string, PackageFile> expected = new Dictionary<string, PackageFile>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageFile file in manifest.Files)
            {
                string relative = ValidateRelativeAppPath(file.Path);
                if (expected.ContainsKey(relative))
                    throw new InvalidDataException("Duplicate package manifest path: " + relative);
                expected.Add(relative, file);
            }

            using (ZipArchive archive = ZipFile.OpenRead(packagePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name))
                        continue;
                    string relative = ValidateRelativeAppPath(entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    PackageFile declared;
                    if (!expected.TryGetValue(relative, out declared))
                        throw new InvalidDataException("Package contains an undeclared file: " + relative);
                    string target = ContainedPath(destinationRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, false);
                    VerifyHash(target, declared.Sha256, relative);
                    if (declared.Signed)
                        VerifyTrustedSignature(target, manifest.PublisherSubject);
                    expected.Remove(relative);
                }
            }

            if (expected.Count != 0)
                throw new InvalidDataException("Package is missing one or more manifest files.");
        }

        private static void VerifyTrustedSignature(string path, string expectedSubject)
        {
            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("File is not Authenticode-signed: " + Path.GetFileName(path), ex);
            }

            using (certificate)
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                if (!String.Equals(certificate.Subject, expectedSubject, StringComparison.Ordinal)
                    || !chain.Build(certificate))
                    throw new InvalidDataException("Authenticode signature is invalid or the publisher is not trusted: " + Path.GetFileName(path));
            }
        }

        private static bool RunHealthCheck(string executable, string transactionRoot, int timeoutSeconds)
        {
            string token = Path.Combine(transactionRoot, "health.ok");
            ProcessStartInfo info = new ProcessStartInfo(executable, Quote(HealthArgument) + " " + Quote(token));
            info.UseShellExecute = false;
            Process process = Process.Start(info);
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, Math.Min(timeoutSeconds, 120)));
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(token) && String.Equals(File.ReadAllText(token).Trim(), "healthy", StringComparison.Ordinal))
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    return true;
                }
                if (process.HasExited && !File.Exists(token))
                    return false;
                Thread.Sleep(200);
            }
            try { if (!process.HasExited) process.Kill(); } catch { }
            return false;
        }

        private static void Rollback(UpdateJournal journal)
        {
            if (journal == null || journal.Files == null)
                return;
            for (int index = journal.Files.Count - 1; index >= 0; index--)
            {
                JournalFile file = journal.Files[index];
                string destination = ContainedPath(journal.InstallRoot, file.RelativePath);
                string backup = ContainedPath(journal.RecoveryRoot, file.RelativePath);
                try
                {
                    if (file.Existed && File.Exists(backup))
                    {
                        if (File.Exists(destination))
                            File.Replace(backup, destination, null, true);
                        else
                            File.Move(backup, destination);
                    }
                    else if (!file.Existed && File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }
                catch { }
            }
        }

        private static void VerifyHash(string path, string expected, string label)
        {
            if (String.IsNullOrWhiteSpace(expected) || expected.Length != 64)
                throw new InvalidDataException("Missing or invalid SHA-256 for " + label + ".");
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!String.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch for " + label + ".");
            }
        }

        private static void EnsureFreeSpace(string path, long required)
        {
            string root = Path.GetPathRoot(path);
            DriveInfo drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < required + 16L * 1024L * 1024L)
                throw new IOException("Insufficient free space for update staging and rollback.");
        }

        private static string ValidateRelativeAppPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                throw new InvalidDataException("Package path must be relative.");
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == "..")
                    throw new InvalidDataException("Package path traversal is blocked: " + path);
            string first = parts[0];
            if (String.Equals(first, "CK3MPS_Data", StringComparison.OrdinalIgnoreCase)
                || String.Equals(first, "data", StringComparison.OrdinalIgnoreCase)
                || String.Equals(first, "quarantine", StringComparison.OrdinalIgnoreCase)
                || String.Equals(first, "logs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update package attempts to replace user state: " + path);
            return String.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        private static string ContainedPath(string root, string relative)
        {
            string canonicalRoot = CanonicalDirectory(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
            if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path escapes the allowed root.");
            return candidate;
        }

        private static void RequireContainedFile(string root, string path)
        {
            string canonicalRoot = CanonicalDirectory(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = CanonicalFile(path);
            if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Updater input is outside staging.");
        }

        private static void RejectReparsePath(string path)
        {
            DirectoryInfo current = new DirectoryInfo(path);
            while (current != null)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse points are not allowed in updater staging paths.");
                current = current.Parent;
            }
        }

        private static string CanonicalDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Directory path is empty.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string CanonicalFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("File path is empty.");
            return Path.GetFullPath(path);
        }

        private static string NormalizeVersion(string version)
        {
            return (version ?? "").Trim().TrimStart('v', 'V');
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static void StartApplication(string executable)
        {
            ProcessStartInfo info = new ProcessStartInfo(executable);
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private static T ReadJson<T>(string path)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.OpenRead(path))
                return (T)serializer.ReadObject(stream);
        }

        internal static void WriteJson<T>(string path, T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                serializer.WriteObject(stream, value);
        }

        private static void WriteJsonAtomic<T>(string path, T value)
        {
            string temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteJson(temporary, value);
            if (File.Exists(path)) File.Replace(temporary, path, null, true); else File.Move(temporary, path);
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
