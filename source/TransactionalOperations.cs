using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CK3MPS
{
    internal static class TransactionalStateMigration
    {
        private const string JournalFileName = ".ck3mps-state-migration";
        private const string StagePrefix = ".ck3mps-migration-stage-";

        private sealed class MigrationJournal
        {
            public string TransactionId = "";
            public string SourceRoot = "";
            public string TargetRoot = "";
            public string StageRoot = "";
            public string Phase = "Prepared";
            public bool DesiredPortableMode;
            public readonly List<string> Files = new List<string>();
            public readonly List<string> CreatedTargets = new List<string>();
        }

        public static void Migrate(string sourceRoot, string targetRoot, bool desiredPortableMode)
        {
            MutationAudit.RecordMutation("state-migration", sourceRoot + " -> " + targetRoot);

            string source;
            string target;
            ValidateRoots(sourceRoot, targetRoot, out source, out target);
            if (String.Equals(source, target, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))
                return;

            Directory.CreateDirectory(target);
            Recover(source, target);

            MigrationJournal journal = new MigrationJournal();
            journal.TransactionId = Guid.NewGuid().ToString("N");
            journal.SourceRoot = source;
            journal.TargetRoot = target;
            journal.StageRoot = Path.Combine(target, StagePrefix + journal.TransactionId);
            journal.DesiredPortableMode = desiredPortableMode;
            CollectRelativeFiles(source, source, journal.Files);

            ValidateDestinationConflicts(journal);
            Directory.CreateDirectory(journal.StageRoot);
            PersistJournal(journal);

            try
            {
                CopyToStage(journal);
                journal.Phase = "Copied";
                PersistJournal(journal);
                ThrowIfMigrationFault("copied");

                CommitStagedFiles(journal);
                WritePortableModeSetting(Path.Combine(target, "settings.ini"), desiredPortableMode);
                journal.Phase = "Committed";
                PersistJournal(journal);
                ThrowIfMigrationFault("committed");

                journal.Phase = "Cleanup";
                PersistJournal(journal);
                CompleteCommittedCleanup(journal);
                RemoveJournalCopies(journal);
            }
            catch
            {
                if (String.Equals(journal.Phase, "Prepared", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(journal.Phase, "Copied", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        RollbackUncommitted(journal);
                        RemoveJournalCopies(journal);
                    }
                    catch
                    {
                        // Keep the journal when rollback is incomplete so startup recovery can retry.
                    }
                }
                throw;
            }
        }

        public static bool? Recover(string firstRoot, string secondRoot)
        {
            string first;
            string second;
            ValidateRoots(firstRoot, secondRoot, out first, out second);

            MigrationJournal journal = TryReadJournal(Path.Combine(first, JournalFileName));
            if (journal == null)
                journal = TryReadJournal(Path.Combine(second, JournalFileName));
            if (journal == null)
                return null;

            string source;
            string target;
            ValidateRoots(journal.SourceRoot, journal.TargetRoot, out source, out target);
            if ((!String.Equals(source, first, StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(source, second, StringComparison.OrdinalIgnoreCase))
                || (!String.Equals(target, first, StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(target, second, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("State migration journal points outside the configured CK3MPS roots.");

            journal.SourceRoot = source;
            journal.TargetRoot = target;
            if (String.Equals(journal.Phase, "Committed", StringComparison.OrdinalIgnoreCase)
                || String.Equals(journal.Phase, "Cleanup", StringComparison.OrdinalIgnoreCase))
            {
                CompleteCommittedCleanup(journal);
                RemoveJournalCopies(journal);
                return journal.DesiredPortableMode;
            }

            RollbackUncommitted(journal);
            RemoveJournalCopies(journal);
            return null;
        }

        private static void ThrowIfMigrationFault(string phase)
        {
            string requested = Environment.GetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT");
            if (String.Equals(requested, phase, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Injected state migration fault at phase: " + phase);
        }
        private static void ValidateRoots(string sourceRoot, string targetRoot, out string source, out string target)
        {
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(sourceRoot, out source)
                || !PathContainmentUtilities.TryNormalizeAbsolutePath(targetRoot, out target))
                throw new InvalidOperationException("Portable state migration roots are invalid.");
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(source)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(target))
                throw new InvalidOperationException("Portable state migration refuses reparse-point roots.");
            if (PathContainmentUtilities.IsWithinRoot(source, target)
                || PathContainmentUtilities.IsWithinRoot(target, source))
                throw new InvalidOperationException("Portable state roots must not contain one another.");
        }

        private static void CollectRelativeFiles(string root, string current, List<string> files)
        {
            if (!Directory.Exists(current))
                return;
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(current))
                throw new InvalidOperationException("State migration encountered a reparse-point directory: " + current);

            foreach (string file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                if (String.Equals(Path.GetFileName(file), JournalFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                files.Add(RelativePath(root, file));
            }

            foreach (string directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(directory).StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                CollectRelativeFiles(root, directory, files);
            }
        }

        private static void ValidateDestinationConflicts(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string source = CombineContained(journal.SourceRoot, relative);
                string target = CombineContained(journal.TargetRoot, relative);
                if (File.Exists(target) && !FilesEqual(source, target))
                    throw new IOException("Portable migration conflict; both roots contain different data: " + relative);
            }
        }

        private static void CopyToStage(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string source = CombineContained(journal.SourceRoot, relative);
                string staged = CombineContained(journal.StageRoot, relative);
                string directory = Path.GetDirectoryName(staged);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.Copy(source, staged, false);
                if (!FilesEqual(source, staged))
                    throw new IOException("Portable migration staging verification failed: " + relative);
            }
        }

        private static void CommitStagedFiles(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string source = CombineContained(journal.SourceRoot, relative);
                string staged = CombineContained(journal.StageRoot, relative);
                string target = CombineContained(journal.TargetRoot, relative);

                if (File.Exists(target))
                {
                    if (!FilesEqual(source, target))
                        throw new IOException("Portable migration destination changed before commit: " + relative);
                    continue;
                }

                string directory = Path.GetDirectoryName(target);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (!journal.CreatedTargets.Contains(relative))
                {
                    journal.CreatedTargets.Add(relative);
                    PersistJournal(journal);
                }
                File.Move(staged, target);
                if (!FilesEqual(source, target))
                    throw new IOException("Portable migration commit verification failed: " + relative);
            }
        }

        private static void RollbackUncommitted(MigrationJournal journal)
        {
            for (int i = journal.CreatedTargets.Count - 1; i >= 0; i--)
            {
                string relative = journal.CreatedTargets[i];
                string source = CombineContained(journal.SourceRoot, relative);
                string target = CombineContained(journal.TargetRoot, relative);
                if (!File.Exists(target))
                    continue;
                if (!File.Exists(source) || !FilesEqual(source, target))
                    throw new IOException("Cannot safely roll back portable migration target: " + relative);
                File.Delete(target);
                DeleteEmptyParents(Path.GetDirectoryName(target), journal.TargetRoot);
            }
            DeleteDirectoryTree(journal.StageRoot);
        }

        private static void CompleteCommittedCleanup(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string source = CombineContained(journal.SourceRoot, relative);
                string target = CombineContained(journal.TargetRoot, relative);
                if (!File.Exists(target))
                    throw new IOException("Committed portable migration is missing target data: " + relative);
                if (!File.Exists(source))
                    continue;

                bool settingsFile = String.Equals(relative, "settings.ini", StringComparison.OrdinalIgnoreCase);
                if (!settingsFile && !FilesEqual(source, target))
                    throw new IOException("Committed portable migration target no longer matches source: " + relative);
                File.Delete(source);
            }

            DeleteDirectoryTree(journal.StageRoot);
            DeleteEmptyDirectoryTree(journal.SourceRoot);
        }

        private static void WritePortableModeSetting(string path, bool enabled)
        {
            List<string> lines = new List<string>();
            if (File.Exists(path))
                lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));

            bool replaced = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].TrimStart().StartsWith("portableMode=", StringComparison.OrdinalIgnoreCase))
                    continue;
                lines[i] = "portableMode=" + enabled;
                replaced = true;
            }
            if (!replaced)
                lines.Add("portableMode=" + enabled);
            SafeAtomicFile.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        private static void PersistJournal(MigrationJournal journal)
        {
            string[] lines = SerializeJournal(journal);
            Directory.CreateDirectory(journal.SourceRoot);
            Directory.CreateDirectory(journal.TargetRoot);
            SafeAtomicFile.WriteAllLines(Path.Combine(journal.SourceRoot, JournalFileName), lines, Encoding.UTF8);
            SafeAtomicFile.WriteAllLines(Path.Combine(journal.TargetRoot, JournalFileName), lines, Encoding.UTF8);
        }

        private static string[] SerializeJournal(MigrationJournal journal)
        {
            List<string> lines = new List<string>();
            lines.Add("version=1");
            lines.Add("transaction=" + Encode(journal.TransactionId));
            lines.Add("source=" + Encode(journal.SourceRoot));
            lines.Add("target=" + Encode(journal.TargetRoot));
            lines.Add("stage=" + Encode(journal.StageRoot));
            lines.Add("phase=" + journal.Phase);
            lines.Add("portable=" + journal.DesiredPortableMode);
            foreach (string relative in journal.Files)
                lines.Add("file=" + Encode(relative));
            foreach (string relative in journal.CreatedTargets)
                lines.Add("created=" + Encode(relative));
            return lines.ToArray();
        }

        private static MigrationJournal TryReadJournal(string path)
        {
            if (!File.Exists(path))
                return null;

            MigrationJournal journal = new MigrationJournal();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = raw.IndexOf('=');
                if (separator <= 0)
                    continue;
                string key = raw.Substring(0, separator);
                string value = raw.Substring(separator + 1);
                if (String.Equals(key, "transaction", StringComparison.OrdinalIgnoreCase))
                    journal.TransactionId = Decode(value);
                else if (String.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                    journal.SourceRoot = Decode(value);
                else if (String.Equals(key, "target", StringComparison.OrdinalIgnoreCase))
                    journal.TargetRoot = Decode(value);
                else if (String.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                    journal.StageRoot = Decode(value);
                else if (String.Equals(key, "phase", StringComparison.OrdinalIgnoreCase))
                    journal.Phase = value;
                else if (String.Equals(key, "portable", StringComparison.OrdinalIgnoreCase))
                    journal.DesiredPortableMode = String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                else if (String.Equals(key, "file", StringComparison.OrdinalIgnoreCase))
                    journal.Files.Add(Decode(value));
                else if (String.Equals(key, "created", StringComparison.OrdinalIgnoreCase))
                    journal.CreatedTargets.Add(Decode(value));
            }

            if (String.IsNullOrWhiteSpace(journal.TransactionId)
                || String.IsNullOrWhiteSpace(journal.SourceRoot)
                || String.IsNullOrWhiteSpace(journal.TargetRoot)
                || String.IsNullOrWhiteSpace(journal.StageRoot))
                throw new InvalidDataException("State migration journal is incomplete: " + path);
            return journal;
        }

        private static void RemoveJournalCopies(MigrationJournal journal)
        {
            DeleteFileNoThrow(Path.Combine(journal.SourceRoot, JournalFileName));
            DeleteFileNoThrow(Path.Combine(journal.TargetRoot, JournalFileName));
        }

        private static string CombineContained(string root, string relative)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, relative ?? ""));
            if (!PathContainmentUtilities.IsWithinRoot(root, candidate))
                throw new InvalidDataException("Migration journal contains an unsafe relative path: " + relative);
            return candidate;
        }

        private static string RelativePath(string root, string path)
        {
            return path.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool FilesEqual(string left, string right)
        {
            FileInfo leftInfo = new FileInfo(left);
            FileInfo rightInfo = new FileInfo(right);
            if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
                return false;
            using (SHA256 sha = SHA256.Create())
            using (FileStream leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] leftHash = sha.ComputeHash(leftStream);
                byte[] rightHash = sha.ComputeHash(rightStream);
                if (leftHash.Length != rightHash.Length)
                    return false;
                for (int i = 0; i < leftHash.Length; i++)
                    if (leftHash[i] != rightHash[i])
                        return false;
                return true;
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        private static void DeleteFileNoThrow(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void DeleteDirectoryTree(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(path))
                throw new InvalidOperationException("Refusing to delete a migration staging tree containing a reparse point.");
            Directory.Delete(path, true);
        }

        private static void DeleteEmptyDirectoryTree(string path)
        {
            if (!Directory.Exists(path))
                return;
            foreach (string child in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
                DeleteEmptyDirectoryTree(child);
            if (Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).Length == 0
                && Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly).Length == 0)
                Directory.Delete(path, false);
        }

        private static void DeleteEmptyParents(string path, string stopRoot)
        {
            string current = path;
            while (!String.IsNullOrEmpty(current)
                && !String.Equals(current, stopRoot, StringComparison.OrdinalIgnoreCase)
                && PathContainmentUtilities.IsWithinRoot(stopRoot, current)
                && Directory.Exists(current)
                && Directory.GetFiles(current).Length == 0
                && Directory.GetDirectories(current).Length == 0)
            {
                string parent = Path.GetDirectoryName(current);
                Directory.Delete(current, false);
                current = parent;
            }
        }
    }
}
