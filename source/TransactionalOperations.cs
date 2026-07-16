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
        private const string BackupDirectoryName = ".backup";
        private const int CurrentJournalVersion = 2;

        private sealed class MigrationJournal
        {
            public int Version = CurrentJournalVersion;
            public string TransactionId = "";
            public string SourceRoot = "";
            public string TargetRoot = "";
            public string StageRoot = "";
            public string Phase = "Prepared";
            public bool DesiredPortableMode;
            public readonly List<string> Files = new List<string>();
            public readonly List<string> GeneratedFiles = new List<string>();
            public readonly List<string> CreatedTargets = new List<string>();
            public readonly List<string> ReplacedTargets = new List<string>();
            public readonly Dictionary<string, string> SourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> TargetHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class JournalReadResult
        {
            public bool Exists;
            public MigrationJournal Journal;
            public Exception Error;
        }

        private sealed class InjectedMigrationFaultException : IOException
        {
            public readonly bool SimulatesCrash;

            public InjectedMigrationFaultException(string phase, bool simulatesCrash)
                : base("Injected state migration fault at phase: " + phase)
            {
                SimulatesCrash = simulatesCrash;
            }
        }

        public static void Migrate(string sourceRoot, string targetRoot, bool desiredPortableMode)
        {
            MutationAudit.RecordMutation("state-migration", sourceRoot + " -> " + targetRoot);

            string source;
            string target;
            ValidateRoots(sourceRoot, targetRoot, out source, out target);
            if (!Directory.Exists(source))
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
            EnsureSettingsEntry(journal);

            ValidateDestinationConflicts(journal);
            Directory.CreateDirectory(journal.StageRoot);
            PersistJournal(journal, true);

            try
            {
                ThrowIfMigrationFault("before-copy");
                CopyToStage(journal);
                PrepareStagedPortableSetting(journal);
                PopulateAndVerifyHashes(journal);
                journal.Phase = "Copied";
                PersistJournal(journal, true);
                ThrowIfMigrationFault("after-copy", "copied");
                ThrowIfMigrationFault("before-commit");

                journal.Phase = "Committing";
                PersistJournal(journal, true);
                CommitStagedFiles(journal);
                VerifyCommittedTargets(journal);
                journal.Phase = "Committed";
                PersistJournal(journal, true);
                ThrowIfMigrationFault("after-commit", "committed");

                journal.Phase = "Cleanup";
                PersistJournal(journal, true);
                CompleteCommittedCleanup(journal);
                RemoveJournalCopies(journal);
            }
            catch (Exception ex)
            {
                InjectedMigrationFaultException injected = ex as InjectedMigrationFaultException;
                bool preserveForRecovery = injected != null && injected.SimulatesCrash;
                if (!preserveForRecovery && IsUncommittedPhase(journal.Phase))
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

            MigrationJournal journal = LoadJournalPair(first, second);
            if (journal == null)
                return null;

            ValidateJournalForConfiguredRoots(journal, first, second);

            if (String.Equals(journal.Phase, "Committed", StringComparison.OrdinalIgnoreCase)
                || String.Equals(journal.Phase, "Cleanup", StringComparison.OrdinalIgnoreCase))
            {
                VerifyCommittedTargets(journal);
                if (!String.Equals(journal.Phase, "Cleanup", StringComparison.OrdinalIgnoreCase))
                {
                    journal.Phase = "Cleanup";
                    PersistJournal(journal, false);
                }
                CompleteCommittedCleanup(journal);
                RemoveJournalCopies(journal);
                return journal.DesiredPortableMode;
            }

            RollbackUncommitted(journal);
            RemoveJournalCopies(journal);
            return null;
        }

        private static bool IsUncommittedPhase(string phase)
        {
            return String.Equals(phase, "Prepared", StringComparison.OrdinalIgnoreCase)
                || String.Equals(phase, "Copied", StringComparison.OrdinalIgnoreCase)
                || String.Equals(phase, "Committing", StringComparison.OrdinalIgnoreCase);
        }

        private static void ThrowIfMigrationFault(string phase)
        {
            ThrowIfMigrationFault(phase, null);
        }

        private static void ThrowIfMigrationFault(string phase, string compatibilityAlias)
        {
            string requested = Environment.GetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT") ?? "";
            bool crash = requested.StartsWith("crash-", StringComparison.OrdinalIgnoreCase);
            string requestedPhase = crash ? requested.Substring("crash-".Length) : requested;
            if (String.Equals(requestedPhase, phase, StringComparison.OrdinalIgnoreCase)
                || (!String.IsNullOrEmpty(compatibilityAlias)
                    && String.Equals(requestedPhase, compatibilityAlias, StringComparison.OrdinalIgnoreCase)))
                throw new InjectedMigrationFaultException(phase, crash);
        }

        private static void ValidateRoots(string sourceRoot, string targetRoot, out string source, out string target)
        {
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(sourceRoot, out source)
                || !PathContainmentUtilities.TryNormalizeAbsolutePath(targetRoot, out target))
                throw new InvalidOperationException("Portable state migration roots are invalid.");
            if (String.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Portable state migration roots must be distinct.");
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(source)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(target))
                throw new InvalidOperationException("Portable state migration refuses reparse-point roots.");
            if (PathContainmentUtilities.IsWithinRoot(source, target)
                || PathContainmentUtilities.IsWithinRoot(target, source))
                throw new InvalidOperationException("Portable state roots must not contain one another.");
        }

        private static void ValidateJournalForConfiguredRoots(MigrationJournal journal, string first, string second)
        {
            string source;
            string target;
            ValidateRoots(journal.SourceRoot, journal.TargetRoot, out source, out target);

            bool forward = String.Equals(source, first, StringComparison.OrdinalIgnoreCase)
                && String.Equals(target, second, StringComparison.OrdinalIgnoreCase);
            bool reverse = String.Equals(source, second, StringComparison.OrdinalIgnoreCase)
                && String.Equals(target, first, StringComparison.OrdinalIgnoreCase);
            if (!forward && !reverse)
                throw new InvalidOperationException("State migration journal points outside the configured CK3MPS roots.");

            string expectedStage = Path.Combine(target, StagePrefix + journal.TransactionId);
            string normalizedStage;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(journal.StageRoot, out normalizedStage)
                || !String.Equals(expectedStage, normalizedStage, StringComparison.OrdinalIgnoreCase)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalizedStage))
                throw new InvalidDataException("State migration journal contains an invalid staging root.");

            journal.SourceRoot = source;
            journal.TargetRoot = target;
            journal.StageRoot = normalizedStage;
            ValidateJournalEntries(journal);
        }

        private static void CollectRelativeFiles(string root, string current, List<string> files)
        {
            if (!Directory.Exists(current))
                return;
            EnsureNotReparsePoint(current, "directory");

            foreach (string file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(file, "file");
                if (String.Equals(Path.GetFileName(file), JournalFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                files.Add(RelativePath(root, file));
            }

            foreach (string directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(directory, "directory");
                if (Path.GetFileName(directory).StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                CollectRelativeFiles(root, directory, files);
            }
        }

        private static void EnsureSettingsEntry(MigrationJournal journal)
        {
            const string settingsRelative = "settings.ini";
            if (ContainsRelative(journal.Files, settingsRelative))
                return;
            journal.Files.Add(settingsRelative);
            journal.GeneratedFiles.Add(settingsRelative);
        }

        private static void ValidateDestinationConflicts(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string target = CombineContained(journal.TargetRoot, relative);
                if (!File.Exists(target))
                    continue;

                if (ContainsRelative(journal.GeneratedFiles, relative))
                    throw new IOException("Portable migration conflict; destination contains data missing from source: " + relative);

                string source = CombineContained(journal.SourceRoot, relative);
                if (!File.Exists(source) || !FilesEqual(source, target))
                    throw new IOException("Portable migration conflict; both roots contain different data: " + relative);
            }
        }

        private static void CopyToStage(MigrationJournal journal)
        {
            int copied = 0;
            foreach (string relative in journal.Files)
            {
                string staged = CombineContained(journal.StageRoot, relative);
                string directory = Path.GetDirectoryName(staged);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (ContainsRelative(journal.GeneratedFiles, relative))
                {
                    using (FileStream generated = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                    }
                }
                else
                {
                    string source = CombineContained(journal.SourceRoot, relative);
                    if (!File.Exists(source))
                        throw new FileNotFoundException("Portable migration source file disappeared before copy.", source);
                    EnsureNotReparsePoint(source, "file");
                    File.Copy(source, staged, false);
                    if (!FilesEqual(source, staged))
                        throw new IOException("Portable migration staging verification failed: " + relative);
                }

                copied++;
                if (copied == 1)
                    ThrowIfMigrationFault("during-copy");
            }
        }

        private static void PrepareStagedPortableSetting(MigrationJournal journal)
        {
            string settings = CombineContained(journal.StageRoot, "settings.ini");
            WritePortableModeSetting(settings, journal.DesiredPortableMode);
        }

        private static void PopulateAndVerifyHashes(MigrationJournal journal)
        {
            journal.SourceHashes.Clear();
            journal.TargetHashes.Clear();
            foreach (string relative in journal.Files)
            {
                string staged = CombineContained(journal.StageRoot, relative);
                if (!File.Exists(staged))
                    throw new IOException("Portable migration staging file is missing: " + relative);
                journal.TargetHashes[relative] = ComputeFileHash(staged);

                if (!ContainsRelative(journal.GeneratedFiles, relative))
                {
                    string source = CombineContained(journal.SourceRoot, relative);
                    if (!File.Exists(source))
                        throw new FileNotFoundException("Portable migration source file disappeared after copy.", source);
                    string sourceHash = ComputeFileHash(source);
                    journal.SourceHashes[relative] = sourceHash;
                    if (!String.Equals(relative, "settings.ini", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(sourceHash, journal.TargetHashes[relative], StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Portable migration staging content changed after copy: " + relative);
                }
            }
        }

        private static void CommitStagedFiles(MigrationJournal journal)
        {
            List<string> ordered = new List<string>();
            foreach (string relative in journal.Files)
                if (!String.Equals(relative, "settings.ini", StringComparison.OrdinalIgnoreCase))
                    ordered.Add(relative);
            if (ContainsRelative(journal.Files, "settings.ini"))
                ordered.Add("settings.ini");

            int committed = 0;
            foreach (string relative in ordered)
            {
                string staged = CombineContained(journal.StageRoot, relative);
                string target = CombineContained(journal.TargetRoot, relative);
                string expectedHash = RequiredHash(journal.TargetHashes, relative, "target");

                if (File.Exists(target) && FileMatchesHash(target, expectedHash))
                    continue;

                if (File.Exists(target))
                {
                    if (ContainsRelative(journal.GeneratedFiles, relative))
                        throw new IOException("Portable migration destination appeared before commit: " + relative);

                    string source = CombineContained(journal.SourceRoot, relative);
                    if (!File.Exists(source) || !FilesEqual(source, target))
                        throw new IOException("Portable migration destination changed before commit: " + relative);

                    AddUnique(journal.ReplacedTargets, relative);
                    PersistJournal(journal, true);
                    string backup = BackupPath(journal, relative);
                    string backupDirectory = Path.GetDirectoryName(backup);
                    if (!String.IsNullOrEmpty(backupDirectory))
                        Directory.CreateDirectory(backupDirectory);
                    if (File.Exists(backup))
                        throw new IOException("Portable migration backup already exists: " + relative);
                    File.Move(target, backup);
                }
                else
                {
                    AddUnique(journal.CreatedTargets, relative);
                    PersistJournal(journal, true);
                }

                string targetDirectory = Path.GetDirectoryName(target);
                if (!String.IsNullOrEmpty(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);
                File.Copy(staged, target, false);
                if (!FileMatchesHash(target, expectedHash))
                    throw new IOException("Portable migration commit verification failed: " + relative);

                committed++;
                if (committed == 1)
                    ThrowIfMigrationFault("during-commit");
            }
        }

        private static void VerifyCommittedTargets(MigrationJournal journal)
        {
            foreach (string relative in journal.Files)
            {
                string target = CombineContained(journal.TargetRoot, relative);
                string expectedHash;
                if (journal.TargetHashes.TryGetValue(relative, out expectedHash))
                {
                    if (!FileMatchesHash(target, expectedHash))
                        throw new IOException("Committed portable migration target is missing or corrupt: " + relative);
                    continue;
                }

                // Compatibility for version 1 journals created by older builds.
                string source = CombineContained(journal.SourceRoot, relative);
                if (!File.Exists(target))
                    throw new IOException("Committed portable migration is missing target data: " + relative);
                if (File.Exists(source)
                    && !String.Equals(relative, "settings.ini", StringComparison.OrdinalIgnoreCase)
                    && !FilesEqual(source, target))
                    throw new IOException("Committed portable migration target no longer matches source: " + relative);
            }
        }

        private static void RollbackUncommitted(MigrationJournal journal)
        {
            for (int i = journal.CreatedTargets.Count - 1; i >= 0; i--)
            {
                string relative = journal.CreatedTargets[i];
                string target = CombineContained(journal.TargetRoot, relative);
                if (!File.Exists(target))
                    continue;
                EnsureRollbackTargetIsExpected(journal, relative, target);
                File.Delete(target);
                DeleteEmptyParents(Path.GetDirectoryName(target), journal.TargetRoot);
            }

            for (int i = journal.ReplacedTargets.Count - 1; i >= 0; i--)
            {
                string relative = journal.ReplacedTargets[i];
                string target = CombineContained(journal.TargetRoot, relative);
                string backup = BackupPath(journal, relative);
                if (File.Exists(backup))
                {
                    if (File.Exists(target))
                    {
                        EnsureRollbackTargetIsExpected(journal, relative, target);
                        File.Delete(target);
                    }
                    string targetDirectory = Path.GetDirectoryName(target);
                    if (!String.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);
                    File.Move(backup, target);
                }
                else if (File.Exists(target))
                {
                    string source = CombineContained(journal.SourceRoot, relative);
                    if (!File.Exists(source) || !FilesEqual(source, target))
                        throw new IOException("Cannot safely roll back portable migration replacement: " + relative);
                }
            }

            DeleteDirectoryTree(journal.StageRoot);
        }

        private static void EnsureRollbackTargetIsExpected(MigrationJournal journal, string relative, string target)
        {
            string expectedHash;
            if (journal.TargetHashes.TryGetValue(relative, out expectedHash))
            {
                if (!FileMatchesHash(target, expectedHash))
                    throw new IOException("Cannot safely roll back changed portable migration target: " + relative);
                return;
            }

            string source = CombineContained(journal.SourceRoot, relative);
            if (!File.Exists(source) || !FilesEqual(source, target))
                throw new IOException("Cannot safely roll back portable migration target: " + relative);
        }

        private static void CompleteCommittedCleanup(MigrationJournal journal)
        {
            VerifyCommittedTargets(journal);

            int deleted = 0;
            foreach (string relative in journal.Files)
            {
                if (ContainsRelative(journal.GeneratedFiles, relative))
                    continue;

                string source = CombineContained(journal.SourceRoot, relative);
                if (!File.Exists(source))
                    continue;

                string originalHash;
                if (journal.SourceHashes.TryGetValue(relative, out originalHash)
                    && !FileMatchesHash(source, originalHash))
                    throw new IOException("Portable migration source changed before cleanup: " + relative);

                File.Delete(source);
                deleted++;
                if (deleted == 1)
                    ThrowIfMigrationFault("during-cleanup");
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

        private static void PersistJournal(MigrationJournal journal, bool createRoots)
        {
            string[] lines = SerializeJournal(journal);
            bool wrote = false;
            wrote |= PersistJournalCopy(journal.SourceRoot, lines, createRoots);
            wrote |= PersistJournalCopy(journal.TargetRoot, lines, createRoots);
            if (!wrote)
                throw new IOException("State migration journal has no writable root.");
        }

        private static bool PersistJournalCopy(string root, string[] lines, bool createRoot)
        {
            if (!Directory.Exists(root))
            {
                if (!createRoot)
                    return false;
                Directory.CreateDirectory(root);
            }
            SafeAtomicFile.WriteAllLines(Path.Combine(root, JournalFileName), lines, Encoding.UTF8);
            return true;
        }

        private static string[] SerializeJournal(MigrationJournal journal)
        {
            List<string> lines = new List<string>();
            lines.Add("version=" + CurrentJournalVersion);
            lines.Add("transaction=" + Encode(journal.TransactionId));
            lines.Add("source=" + Encode(journal.SourceRoot));
            lines.Add("target=" + Encode(journal.TargetRoot));
            lines.Add("stage=" + Encode(journal.StageRoot));
            lines.Add("phase=" + journal.Phase);
            lines.Add("portable=" + journal.DesiredPortableMode.ToString().ToLowerInvariant());
            foreach (string relative in journal.Files)
                lines.Add("file=" + Encode(relative));
            foreach (string relative in journal.GeneratedFiles)
                lines.Add("generated=" + Encode(relative));
            AddHashLines(lines, "sourceHash", journal.SourceHashes);
            AddHashLines(lines, "targetHash", journal.TargetHashes);
            foreach (string relative in journal.CreatedTargets)
                lines.Add("created=" + Encode(relative));
            foreach (string relative in journal.ReplacedTargets)
                lines.Add("replaced=" + Encode(relative));
            lines.Add("checksum=" + ComputeTextHash(lines));
            return lines.ToArray();
        }

        private static void AddHashLines(List<string> lines, string key, Dictionary<string, string> hashes)
        {
            List<string> names = new List<string>(hashes.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in names)
                lines.Add(key + "=" + Encode(relative) + "|" + hashes[relative]);
        }

        private static MigrationJournal LoadJournalPair(string first, string second)
        {
            JournalReadResult firstResult = ReadJournal(Path.Combine(first, JournalFileName));
            JournalReadResult secondResult = ReadJournal(Path.Combine(second, JournalFileName));
            if (!firstResult.Exists && !secondResult.Exists)
                return null;

            if (firstResult.Journal != null && secondResult.Journal != null)
                return MergeJournalCopies(firstResult.Journal, secondResult.Journal);
            if (firstResult.Journal != null)
                return firstResult.Journal;
            if (secondResult.Journal != null)
                return secondResult.Journal;

            string detail = firstResult.Error != null ? firstResult.Error.Message : "";
            if (secondResult.Error != null)
                detail = detail + (detail.Length == 0 ? "" : " | ") + secondResult.Error.Message;
            throw new InvalidDataException("No valid state migration journal copy was found. " + detail);
        }

        private static JournalReadResult ReadJournal(string path)
        {
            JournalReadResult result = new JournalReadResult();
            result.Exists = File.Exists(path);
            if (!result.Exists)
                return result;
            try
            {
                result.Journal = ParseJournal(path);
            }
            catch (Exception ex)
            {
                result.Error = ex;
            }
            return result;
        }

        private static MigrationJournal ParseJournal(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0)
                throw new InvalidDataException("State migration journal is empty: " + path);

            int version;
            if (!TryReadVersion(lines[0], out version))
                throw new InvalidDataException("State migration journal version is missing or invalid: " + path);
            if (version == 1)
                return ParseVersion1Journal(lines, path);
            if (version != CurrentJournalVersion)
                throw new InvalidDataException("Unsupported state migration journal version: " + version);
            if (lines.Length < 8 || !lines[lines.Length - 1].StartsWith("checksum=", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("State migration journal is incomplete: " + path);

            string expectedChecksum = lines[lines.Length - 1].Substring("checksum=".Length);
            List<string> payload = new List<string>();
            for (int i = 0; i < lines.Length - 1; i++)
                payload.Add(lines[i]);
            if (!String.Equals(expectedChecksum, ComputeTextHash(payload), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("State migration journal checksum failed: " + path);

            MigrationJournal journal = new MigrationJournal();
            journal.Version = version;
            bool transactionSeen = false;
            bool sourceSeen = false;
            bool targetSeen = false;
            bool stageSeen = false;
            bool phaseSeen = false;
            bool portableSeen = false;

            for (int i = 1; i < lines.Length - 1; i++)
            {
                string key;
                string value;
                SplitJournalLine(lines[i], path, out key, out value);
                if (String.Equals(key, "transaction", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref transactionSeen, key, path);
                    journal.TransactionId = DecodeChecked(value, path);
                }
                else if (String.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref sourceSeen, key, path);
                    journal.SourceRoot = DecodeChecked(value, path);
                }
                else if (String.Equals(key, "target", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref targetSeen, key, path);
                    journal.TargetRoot = DecodeChecked(value, path);
                }
                else if (String.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref stageSeen, key, path);
                    journal.StageRoot = DecodeChecked(value, path);
                }
                else if (String.Equals(key, "phase", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref phaseSeen, key, path);
                    journal.Phase = value;
                }
                else if (String.Equals(key, "portable", StringComparison.OrdinalIgnoreCase))
                {
                    RequireSingle(ref portableSeen, key, path);
                    if (!Boolean.TryParse(value, out journal.DesiredPortableMode))
                        throw new InvalidDataException("State migration journal has an invalid portable flag: " + path);
                }
                else if (String.Equals(key, "file", StringComparison.OrdinalIgnoreCase))
                    journal.Files.Add(DecodeChecked(value, path));
                else if (String.Equals(key, "generated", StringComparison.OrdinalIgnoreCase))
                    journal.GeneratedFiles.Add(DecodeChecked(value, path));
                else if (String.Equals(key, "sourceHash", StringComparison.OrdinalIgnoreCase))
                    ParseHashEntry(journal.SourceHashes, value, path);
                else if (String.Equals(key, "targetHash", StringComparison.OrdinalIgnoreCase))
                    ParseHashEntry(journal.TargetHashes, value, path);
                else if (String.Equals(key, "created", StringComparison.OrdinalIgnoreCase))
                    journal.CreatedTargets.Add(DecodeChecked(value, path));
                else if (String.Equals(key, "replaced", StringComparison.OrdinalIgnoreCase))
                    journal.ReplacedTargets.Add(DecodeChecked(value, path));
                else
                    throw new InvalidDataException("State migration journal contains an unknown field: " + key);
            }

            if (!transactionSeen || !sourceSeen || !targetSeen || !stageSeen || !phaseSeen || !portableSeen)
                throw new InvalidDataException("State migration journal is incomplete: " + path);
            ValidateJournalEntries(journal);
            return journal;
        }

        private static MigrationJournal ParseVersion1Journal(string[] lines, string path)
        {
            MigrationJournal journal = new MigrationJournal();
            journal.Version = 1;
            for (int i = 1; i < lines.Length; i++)
            {
                string key;
                string value;
                SplitJournalLine(lines[i], path, out key, out value);
                if (String.Equals(key, "transaction", StringComparison.OrdinalIgnoreCase))
                    journal.TransactionId = DecodeChecked(value, path);
                else if (String.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                    journal.SourceRoot = DecodeChecked(value, path);
                else if (String.Equals(key, "target", StringComparison.OrdinalIgnoreCase))
                    journal.TargetRoot = DecodeChecked(value, path);
                else if (String.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                    journal.StageRoot = DecodeChecked(value, path);
                else if (String.Equals(key, "phase", StringComparison.OrdinalIgnoreCase))
                    journal.Phase = value;
                else if (String.Equals(key, "portable", StringComparison.OrdinalIgnoreCase))
                    journal.DesiredPortableMode = String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                else if (String.Equals(key, "file", StringComparison.OrdinalIgnoreCase))
                    journal.Files.Add(DecodeChecked(value, path));
                else if (String.Equals(key, "created", StringComparison.OrdinalIgnoreCase))
                    journal.CreatedTargets.Add(DecodeChecked(value, path));
            }
            if (String.IsNullOrWhiteSpace(journal.TransactionId)
                || String.IsNullOrWhiteSpace(journal.SourceRoot)
                || String.IsNullOrWhiteSpace(journal.TargetRoot)
                || String.IsNullOrWhiteSpace(journal.StageRoot))
                throw new InvalidDataException("State migration journal is incomplete: " + path);
            ValidateJournalEntries(journal);
            return journal;
        }

        private static bool TryReadVersion(string line, out int version)
        {
            version = 0;
            if (line == null || !line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                return false;
            return Int32.TryParse(line.Substring("version=".Length), out version);
        }

        private static void SplitJournalLine(string line, string path, out string key, out string value)
        {
            int separator = line == null ? -1 : line.IndexOf('=');
            if (separator <= 0)
                throw new InvalidDataException("State migration journal contains a malformed line: " + path);
            key = line.Substring(0, separator);
            value = line.Substring(separator + 1);
        }

        private static void RequireSingle(ref bool seen, string key, string path)
        {
            if (seen)
                throw new InvalidDataException("State migration journal repeats field " + key + ": " + path);
            seen = true;
        }

        private static void ParseHashEntry(Dictionary<string, string> hashes, string value, string path)
        {
            int separator = value.IndexOf('|');
            if (separator <= 0 || separator == value.Length - 1)
                throw new InvalidDataException("State migration journal contains a malformed hash: " + path);
            string relative = DecodeChecked(value.Substring(0, separator), path);
            string hash = value.Substring(separator + 1);
            if (!IsValidSha256(hash) || hashes.ContainsKey(relative))
                throw new InvalidDataException("State migration journal contains an invalid or duplicate hash: " + path);
            hashes.Add(relative, hash);
        }

        private static MigrationJournal MergeJournalCopies(MigrationJournal first, MigrationJournal second)
        {
            if (!String.Equals(first.TransactionId, second.TransactionId, StringComparison.Ordinal)
                || !String.Equals(first.SourceRoot, second.SourceRoot, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(first.TargetRoot, second.TargetRoot, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(first.StageRoot, second.StageRoot, StringComparison.OrdinalIgnoreCase)
                || first.DesiredPortableMode != second.DesiredPortableMode
                || !SameRelativeSet(first.Files, second.Files)
                || !SameRelativeSet(first.GeneratedFiles, second.GeneratedFiles))
                throw new InvalidDataException("State migration journal copies disagree about the transaction.");

            MigrationJournal merged = PhaseRank(first.Phase) >= PhaseRank(second.Phase) ? first : second;
            MigrationJournal other = Object.ReferenceEquals(merged, first) ? second : first;
            MergeUnique(merged.CreatedTargets, other.CreatedTargets);
            MergeUnique(merged.ReplacedTargets, other.ReplacedTargets);
            MergeHashes(merged.SourceHashes, other.SourceHashes);
            MergeHashes(merged.TargetHashes, other.TargetHashes);
            ValidateJournalEntries(merged);
            return merged;
        }

        private static void MergeHashes(Dictionary<string, string> destination, Dictionary<string, string> source)
        {
            foreach (KeyValuePair<string, string> pair in source)
            {
                string current;
                if (destination.TryGetValue(pair.Key, out current))
                {
                    if (!String.Equals(current, pair.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("State migration journal copies disagree about file content.");
                }
                else
                    destination.Add(pair.Key, pair.Value);
            }
        }

        private static void ValidateJournalEntries(MigrationJournal journal)
        {
            if (String.IsNullOrWhiteSpace(journal.TransactionId)
                || journal.TransactionId.Length != 32
                || !IsHex(journal.TransactionId))
                throw new InvalidDataException("State migration journal contains an invalid transaction id.");
            if (PhaseRank(journal.Phase) < 0)
                throw new InvalidDataException("State migration journal contains an invalid phase: " + journal.Phase);
            if (journal.Files.Count == 0)
                throw new InvalidDataException("State migration journal contains no files.");

            ValidateRelativeList(journal.Files, "file");
            ValidateRelativeList(journal.GeneratedFiles, "generated file");
            ValidateRelativeList(journal.CreatedTargets, "created target");
            ValidateRelativeList(journal.ReplacedTargets, "replaced target");
            EnsureSubset(journal.GeneratedFiles, journal.Files, "generated file");
            EnsureSubset(journal.CreatedTargets, journal.Files, "created target");
            EnsureSubset(journal.ReplacedTargets, journal.Files, "replaced target");
            foreach (string relative in journal.CreatedTargets)
                if (ContainsRelative(journal.ReplacedTargets, relative))
                    throw new InvalidDataException("State migration journal marks a target as both created and replaced: " + relative);

            ValidateHashKeys(journal.SourceHashes, journal.Files, "source");
            ValidateHashKeys(journal.TargetHashes, journal.Files, "target");
            if (journal.Version >= 2 && PhaseRank(journal.Phase) >= PhaseRank("Copied"))
            {
                foreach (string relative in journal.Files)
                {
                    if (!journal.TargetHashes.ContainsKey(relative))
                        throw new InvalidDataException("State migration journal is missing a target hash: " + relative);
                    if (!ContainsRelative(journal.GeneratedFiles, relative) && !journal.SourceHashes.ContainsKey(relative))
                        throw new InvalidDataException("State migration journal is missing a source hash: " + relative);
                }
            }
        }

        private static void ValidateRelativeList(List<string> values, string kind)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in values)
            {
                if (String.IsNullOrWhiteSpace(relative)
                    || Path.IsPathRooted(relative)
                    || String.Equals(relative, JournalFileName, StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("State migration journal contains an unsafe " + kind + ": " + relative);
                string normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                string[] parts = normalized.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    throw new InvalidDataException("State migration journal contains an empty " + kind + ".");
                foreach (string part in parts)
                    if (String.Equals(part, ".", StringComparison.Ordinal) || String.Equals(part, "..", StringComparison.Ordinal))
                        throw new InvalidDataException("State migration journal contains an unsafe " + kind + ": " + relative);
                if (!seen.Add(relative))
                    throw new InvalidDataException("State migration journal contains a duplicate " + kind + ": " + relative);
            }
        }

        private static void ValidateHashKeys(Dictionary<string, string> hashes, List<string> files, string kind)
        {
            foreach (KeyValuePair<string, string> pair in hashes)
            {
                if (!ContainsRelative(files, pair.Key) || !IsValidSha256(pair.Value))
                    throw new InvalidDataException("State migration journal contains an invalid " + kind + " hash: " + pair.Key);
            }
        }

        private static void EnsureSubset(List<string> subset, List<string> files, string kind)
        {
            foreach (string relative in subset)
                if (!ContainsRelative(files, relative))
                    throw new InvalidDataException("State migration journal references an unknown " + kind + ": " + relative);
        }

        private static int PhaseRank(string phase)
        {
            if (String.Equals(phase, "Prepared", StringComparison.OrdinalIgnoreCase)) return 0;
            if (String.Equals(phase, "Copied", StringComparison.OrdinalIgnoreCase)) return 1;
            if (String.Equals(phase, "Committing", StringComparison.OrdinalIgnoreCase)) return 2;
            if (String.Equals(phase, "Committed", StringComparison.OrdinalIgnoreCase)) return 3;
            if (String.Equals(phase, "Cleanup", StringComparison.OrdinalIgnoreCase)) return 4;
            return -1;
        }

        private static void RemoveJournalCopies(MigrationJournal journal)
        {
            DeleteJournalCopy(Path.Combine(journal.SourceRoot, JournalFileName));
            DeleteJournalCopy(Path.Combine(journal.TargetRoot, JournalFileName));
        }

        private static void DeleteJournalCopy(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string BackupPath(MigrationJournal journal, string relative)
        {
            return CombineContained(Path.Combine(journal.StageRoot, BackupDirectoryName), relative);
        }

        private static string CombineContained(string root, string relative)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, relative ?? ""));
            if (!PathContainmentUtilities.IsWithinRoot(root, candidate))
                throw new InvalidDataException("Migration journal contains an unsafe relative path: " + relative);
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(candidate))
                throw new InvalidOperationException("Portable state migration encountered a reparse point: " + candidate);
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
            return String.Equals(ComputeFileHash(left), ComputeFileHash(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool FileMatchesHash(string path, string expectedHash)
        {
            return File.Exists(path)
                && String.Equals(ComputeFileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeFileHash(string path)
        {
            EnsureNotReparsePoint(path, "file");
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ToHex(sha.ComputeHash(stream));
        }

        private static string ComputeTextHash(List<string> lines)
        {
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n", lines.ToArray()))));
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static string RequiredHash(Dictionary<string, string> hashes, string relative, string kind)
        {
            string hash;
            if (!hashes.TryGetValue(relative, out hash))
                throw new InvalidDataException("State migration journal is missing a " + kind + " hash: " + relative);
            return hash;
        }

        private static bool IsValidSha256(string value)
        {
            return value != null && value.Length == 64 && IsHex(value);
        }

        private static bool IsHex(string value)
        {
            if (String.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string DecodeChecked(string value, string path)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("State migration journal contains invalid encoded data: " + path, ex);
            }
        }

        private static void EnsureNotReparsePoint(string path, string kind)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(path))
                throw new InvalidOperationException("State migration encountered a reparse-point " + kind + ": " + path);
        }

        private static void DeleteDirectoryTree(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            EnsureTreeHasNoReparsePoints(path);
            Directory.Delete(path, true);
        }

        private static void EnsureTreeHasNoReparsePoints(string path)
        {
            EnsureNotReparsePoint(path, "directory");
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly))
                EnsureNotReparsePoint(file, "file");
            foreach (string directory in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
                EnsureTreeHasNoReparsePoints(directory);
        }

        private static void DeleteEmptyDirectoryTree(string path)
        {
            if (!Directory.Exists(path))
                return;
            EnsureNotReparsePoint(path, "directory");
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
                EnsureNotReparsePoint(current, "directory");
                string parent = Path.GetDirectoryName(current);
                Directory.Delete(current, false);
                current = parent;
            }
        }

        private static bool ContainsRelative(List<string> values, string relative)
        {
            foreach (string value in values)
                if (String.Equals(value, relative, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void AddUnique(List<string> values, string relative)
        {
            if (!ContainsRelative(values, relative))
                values.Add(relative);
        }

        private static void MergeUnique(List<string> destination, List<string> source)
        {
            foreach (string relative in source)
                AddUnique(destination, relative);
        }

        private static bool SameRelativeSet(List<string> first, List<string> second)
        {
            if (first.Count != second.Count)
                return false;
            foreach (string relative in first)
                if (!ContainsRelative(second, relative))
                    return false;
            return true;
        }
    }
}
