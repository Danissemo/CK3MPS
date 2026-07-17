using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CK3MPS
{
    internal static class UpdaterRecovery
    {
        [DataContract]
        private sealed class RequestView
        {
            [DataMember(Name = "installRoot")] public string InstallRoot;
        }

        [DataContract]
        private sealed class JournalView
        {
            [DataMember(Name = "stage")] public string Stage;
            [DataMember(Name = "installRoot")] public string InstallRoot;
            [DataMember(Name = "recoveryRoot")] public string RecoveryRoot;
            [DataMember(Name = "newRoot")] public string NewRoot;
            [DataMember(Name = "files")] public List<JournalFileView> Files;
        }

        [DataContract]
        private sealed class JournalFileView
        {
            [DataMember(Name = "relativePath")] public string RelativePath;
            [DataMember(Name = "existed")] public bool Existed;
        }

        internal static void RecoverBeforeApply(string[] args)
        {
            if (args == null || args.Length != 2 || !String.Equals(args[0], SafeUpdater.ApplyArgument, StringComparison.Ordinal))
                return;

            string requestPath = Path.GetFullPath(args[1]);
            RequestView request = ReadJson<RequestView>(requestPath);
            if (request == null || String.IsNullOrWhiteSpace(request.InstallRoot))
                throw new InvalidDataException("Updater request does not identify the installation root.");

            string installRoot = CanonicalDirectory(request.InstallRoot);
            DirectoryInfo parent = Directory.GetParent(installRoot);
            if (parent == null || !parent.Exists)
                throw new DirectoryNotFoundException("Updater installation parent is unavailable.");

            foreach (DirectoryInfo transaction in parent.GetDirectories(".ck3mps-update-*", SearchOption.TopDirectoryOnly))
            {
                if ((transaction.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Updater recovery refuses a reparse-point transaction directory: " + transaction.FullName);

                string journalPath = Path.Combine(transaction.FullName, "update-journal.json");
                if (!File.Exists(journalPath))
                    continue;

                JournalView journal = ReadJson<JournalView>(journalPath);
                ValidateJournal(transaction.FullName, installRoot, journal);
                if (String.Equals(journal.Stage, "Healthy", StringComparison.Ordinal))
                {
                    transaction.Delete(true);
                    continue;
                }

                Rollback(journal);
                transaction.Delete(true);
            }
        }

        private static void ValidateJournal(string transactionRoot, string installRoot, JournalView journal)
        {
            if (journal == null || journal.Files == null)
                throw new InvalidDataException("Interrupted updater journal is incomplete.");
            if (!String.Equals(CanonicalDirectory(journal.InstallRoot), installRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Interrupted updater journal belongs to another installation.");

            string canonicalTransaction = CanonicalDirectory(transactionRoot) + Path.DirectorySeparatorChar;
            string recovery = CanonicalDirectory(journal.RecoveryRoot) + Path.DirectorySeparatorChar;
            string prepared = CanonicalDirectory(journal.NewRoot) + Path.DirectorySeparatorChar;
            if (!recovery.StartsWith(canonicalTransaction, StringComparison.OrdinalIgnoreCase)
                || !prepared.StartsWith(canonicalTransaction, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Interrupted updater journal escapes its transaction root.");
        }

        private static void Rollback(JournalView journal)
        {
            List<Exception> failures = new List<Exception>();
            for (int index = journal.Files.Count - 1; index >= 0; index--)
            {
                JournalFileView file = journal.Files[index];
                try
                {
                    string relative = ValidateRelativePath(file.RelativePath);
                    string destination = ContainedPath(journal.InstallRoot, relative);
                    string backup = ContainedPath(journal.RecoveryRoot, relative);
                    if (file.Existed)
                    {
                        if (File.Exists(backup))
                        {
                            if (File.Exists(destination))
                                File.Replace(backup, destination, null, true);
                            else
                                new FileInfo(backup).MoveTo(destination);
                        }
                    }
                    else if (File.Exists(destination))
                    {
                        new FileInfo(destination).Delete();
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            if (failures.Count != 0)
                throw new AggregateException("Interrupted updater rollback was incomplete. Recovery data was preserved.", failures);
        }

        private static string ValidateRelativePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                throw new InvalidDataException("Interrupted updater journal contains an invalid path.");
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            foreach (string part in normalized.Split(Path.DirectorySeparatorChar))
                if (part.Length == 0 || part == "." || part == "..")
                    throw new InvalidDataException("Interrupted updater journal contains path traversal.");
            return normalized;
        }

        private static string ContainedPath(string root, string relative)
        {
            string canonicalRoot = CanonicalDirectory(root) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
            if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Interrupted updater recovery path escapes its root.");
            return candidate;
        }

        private static string CanonicalDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static T ReadJson<T>(string path)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.OpenRead(path))
                return (T)serializer.ReadObject(stream);
        }
    }
}
