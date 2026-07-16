using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CK3MPS
{
    internal static class TransactionalMigrationTests
    {
        private const string JournalFileName = ".ck3mps-state-migration";
        private const string StagePrefix = ".ck3mps-migration-stage-";
        private static int failures;

        private static void Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "CK3MPS-transaction-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Run(root, "successful migration both directions", TestSuccessfulMigrationBothDirections);
                Run(root, "missing source and destination", TestMissingSourceAndDestination);
                Run(root, "destination conflict", TestConflictLeavesBothRootsUntouched);
                Run(root, "fault before copy", TestFaultBeforeCopy);
                Run(root, "fault during copy", TestFaultDuringCopy);
                Run(root, "fault after copy", TestFaultAfterCopy);
                Run(root, "fault before commit", TestFaultBeforeCommit);
                Run(root, "pre-commit rollback preserves every source file", TestPreCommitRollbackPreservesAllSourceFiles);
                Run(root, "restart recovery from prepared", TestRestartRecoveryFromPrepared);
                Run(root, "restart recovery from copied", TestRestartRecoveryFromCopied);
                Run(root, "restart recovery from committing", TestRestartRecoveryFromCommitting);
                Run(root, "restart recovery after commit", TestRestartRecoveryAfterCommit);
                Run(root, "restart recovery during cleanup", TestRestartRecoveryDuringCleanup);
                Run(root, "corrupted journal fallback", TestCorruptedJournalFallback);
                Run(root, "partial journal rejection", TestPartialJournalRejection);
                Run(root, "repeated recover is idempotent", TestRepeatedRecoverIsIdempotent);
                Run(root, "foreign journal rejection", TestForeignJournalRejection);
                Run(root, "copied content verified before commit", TestCopiedContentBeforeCommit);
                Run(root, "generated settings file", TestGeneratedSettingsFile);
                Run(root, "identical destination settings rollback", TestIdenticalDestinationSettingsRollback);
                Run(root, "nested roots rejected", TestNestedRootsRejected);
                Run(root, "reparse point rejected", TestReparsePointRejected);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null);
                TryDeleteDirectory(root);
            }

            if (failures > 0)
                Environment.Exit(1);
            Console.WriteLine("Transactional migration tests passed.");
        }

        private static void Run(string root, string name, Action<string> test)
        {
            string testRoot = Path.Combine(root, Sanitize(name) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null);
            try
            {
                test(testRoot);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + name + ": " + ex);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null);
                TryDeleteDirectory(testRoot);
            }
        }

        private static void TestSuccessfulMigrationBothDirections(string root)
        {
            string documents = Path.Combine(root, "DocumentsRoot");
            string portable = Path.Combine(root, "PortableRoot");
            WriteState(documents, "forward");

            TransactionalStateMigration.Migrate(documents, portable, true);
            AssertState(portable, "forward", true, "Documents to portable");
            Check(!File.Exists(Path.Combine(documents, "nested", "state.txt")), "forward cleanup removed source state");
            CheckNoArtifacts(documents, portable, "forward success");

            File.WriteAllText(Path.Combine(portable, "nested", "state.txt"), "reverse", Encoding.UTF8);
            File.WriteAllText(Path.Combine(portable, "other.txt"), "other-reverse", Encoding.UTF8);
            TransactionalStateMigration.Migrate(portable, documents, false);
            AssertState(documents, "reverse", false, "portable to Documents");
            Check(!File.Exists(Path.Combine(portable, "nested", "state.txt")), "reverse cleanup removed source state");
            CheckNoArtifacts(portable, documents, "reverse success");
        }

        private static void TestMissingSourceAndDestination(string root)
        {
            string missing = Path.Combine(root, "missing-source");
            string untouchedTarget = Path.Combine(root, "untouched-target");
            TransactionalStateMigration.Migrate(missing, untouchedTarget, true);
            Check(!Directory.Exists(untouchedTarget), "missing source does not create destination");

            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "new-target");
            WriteState(source, "new-target");
            TransactionalStateMigration.Migrate(source, target, true);
            AssertState(target, "new-target", true, "missing destination created");
            CheckNoArtifacts(source, target, "missing destination success");
        }

        private static void TestConflictLeavesBothRootsUntouched(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "source");
            WriteState(target, "target");

            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "conflicting roots are rejected");
            Check(ReadState(source) == "source", "conflict preserved source");
            Check(ReadState(target) == "target", "conflict preserved destination");
            CheckNoArtifacts(source, target, "conflict");
        }

        private static void TestFaultBeforeCopy(string root)
        {
            AssertImmediatePreCommitRollback(root, "before-copy", "before-copy");
        }

        private static void TestFaultDuringCopy(string root)
        {
            AssertImmediatePreCommitRollback(root, "during-copy", "during-copy");
        }

        private static void TestFaultAfterCopy(string root)
        {
            AssertImmediatePreCommitRollback(root, "after-copy", "after-copy");
        }

        private static void TestFaultBeforeCommit(string root)
        {
            AssertImmediatePreCommitRollback(root, "before-commit", "before-commit");
        }

        private static void AssertImmediatePreCommitRollback(string root, string fault, string label)
        {
            string source = Path.Combine(root, label + "-source");
            string target = Path.Combine(root, label + "-target");
            WriteState(source, label);
            SetFault(fault);
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, label + " fault injected");
            ClearFault();

            AssertSourceFiles(source, label, label + " rollback");
            Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), label + " rollback removed destination state");
            CheckNoArtifacts(source, target, label + " rollback");
        }

        private static void TestPreCommitRollbackPreservesAllSourceFiles(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "all-files");
            SetFault("during-commit");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "during-commit fault injected");
            ClearFault();

            AssertSourceFiles(source, "all-files", "during-commit rollback");
            Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), "during-commit rollback removed created target state");
            Check(!File.Exists(Path.Combine(target, "other.txt")), "during-commit rollback removed every created target file");
            CheckNoArtifacts(source, target, "during-commit rollback");
        }

        private static void TestRestartRecoveryFromPrepared(string root)
        {
            AssertCrashRollback(root, "crash-before-copy", "prepared");
        }

        private static void TestRestartRecoveryFromCopied(string root)
        {
            AssertCrashRollback(root, "crash-after-copy", "copied");
        }

        private static void TestRestartRecoveryFromCommitting(string root)
        {
            AssertCrashRollback(root, "crash-during-commit", "committing");
        }

        private static void AssertCrashRollback(string root, string fault, string label)
        {
            string source = Path.Combine(root, label + "-source");
            string target = Path.Combine(root, label + "-target");
            WriteState(source, label);
            SetFault(fault);
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, label + " crash injected");
            ClearFault();

            Check(HasAnyJournal(source, target), label + " crash preserved recovery journal");
            bool? recovered = TransactionalStateMigration.Recover(source, target);
            Check(!recovered.HasValue, label + " recovery rolled back uncommitted migration");
            AssertSourceFiles(source, label, label + " recovery");
            Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), label + " recovery removed destination state");
            CheckNoArtifacts(source, target, label + " recovery");
        }

        private static void TestRestartRecoveryAfterCommit(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "committed");
            SetFault("after-commit");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "after-commit fault injected");
            ClearFault();

            Check(HasAnyJournal(source, target), "after-commit fault preserved recovery journal");
            AssertState(target, "committed", true, "after-commit target");
            bool? recovered = TransactionalStateMigration.Recover(source, target);
            Check(recovered.HasValue && recovered.Value, "after-commit recovery returns portable mode");
            Check(!File.Exists(Path.Combine(source, "nested", "state.txt")), "after-commit recovery finished source cleanup");
            CheckNoArtifacts(source, target, "after-commit recovery");
        }

        private static void TestRestartRecoveryDuringCleanup(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "cleanup");
            SetFault("during-cleanup");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "during-cleanup fault injected");
            ClearFault();

            AssertState(target, "cleanup", true, "cleanup fault target remains complete");
            Check(AllOriginalFilesExistSomewhere(source, target), "cleanup fault caused no file loss");
            Check(HasAnyJournal(source, target), "cleanup fault preserved recovery journal");

            bool? recovered = TransactionalStateMigration.Recover(source, target);
            Check(recovered.HasValue && recovered.Value, "cleanup recovery returns portable mode");
            AssertState(target, "cleanup", true, "cleanup recovery target");
            Check(!File.Exists(Path.Combine(source, "nested", "state.txt")), "cleanup recovery removed remaining source files");
            CheckNoArtifacts(source, target, "cleanup recovery");
        }

        private static void TestCorruptedJournalFallback(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "corrupt-fallback");
            SetFault("crash-after-copy");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "crash left copied journals");
            ClearFault();

            File.WriteAllText(Path.Combine(source, JournalFileName), "not-a-valid-journal", Encoding.UTF8);
            bool? recovered = TransactionalStateMigration.Recover(source, target);
            Check(!recovered.HasValue, "valid second journal copy recovered corrupted first copy");
            AssertSourceFiles(source, "corrupt-fallback", "corrupted journal fallback");
            CheckNoArtifacts(source, target, "corrupted journal fallback");
        }

        private static void TestPartialJournalRejection(string root)
        {
            string first = Path.Combine(root, "first");
            string second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, JournalFileName), "version=2\ntransaction=YWJj", Encoding.UTF8);
            File.WriteAllText(Path.Combine(second, JournalFileName), "version=2\ntransaction=YWJj\nsource=", Encoding.UTF8);

            ExpectException<InvalidDataException>(delegate { TransactionalStateMigration.Recover(first, second); }, "partial journal copies rejected safely");
            Check(File.Exists(Path.Combine(first, JournalFileName)), "partial first journal preserved for diagnosis");
            Check(File.Exists(Path.Combine(second, JournalFileName)), "partial second journal preserved for diagnosis");
        }

        private static void TestRepeatedRecoverIsIdempotent(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "idempotent");
            SetFault("after-commit");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "idempotent setup fault");
            ClearFault();

            bool? first = TransactionalStateMigration.Recover(source, target);
            bool? second = TransactionalStateMigration.Recover(source, target);
            Check(first.HasValue && first.Value, "first recovery completed commit");
            Check(!second.HasValue, "second recovery is a no-op");
            AssertState(target, "idempotent", true, "idempotent target");
            CheckNoArtifacts(source, target, "idempotent recovery");
        }

        private static void TestForeignJournalRejection(string root)
        {
            string foreignSource = Path.Combine(root, "foreign-source");
            string foreignTarget = Path.Combine(root, "foreign-target");
            WriteState(foreignSource, "foreign");
            SetFault("crash-before-copy");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(foreignSource, foreignTarget, true); }, "foreign journal setup fault");
            ClearFault();

            string configuredFirst = Path.Combine(root, "configured-first");
            string configuredSecond = Path.Combine(root, "configured-second");
            Directory.CreateDirectory(configuredFirst);
            Directory.CreateDirectory(configuredSecond);
            File.Copy(Path.Combine(foreignTarget, JournalFileName), Path.Combine(configuredFirst, JournalFileName));

            ExpectException<InvalidOperationException>(delegate { TransactionalStateMigration.Recover(configuredFirst, configuredSecond); }, "journal from foreign roots rejected");
            Check(File.Exists(Path.Combine(configuredFirst, JournalFileName)), "foreign journal was not consumed");
        }

        private static void TestCopiedContentBeforeCommit(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "staged-content");
            SetFault("crash-after-copy");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "copied-content setup fault");
            ClearFault();

            string stage = SingleStageDirectory(target);
            Check(File.ReadAllText(Path.Combine(stage, "nested", "state.txt"), Encoding.UTF8) == "staged-content", "staged state matches source before commit");
            Check(File.ReadAllText(Path.Combine(stage, "other.txt"), Encoding.UTF8) == "other-staged-content", "all staged files match source before commit");
            string stagedSettings = File.ReadAllText(Path.Combine(stage, "settings.ini"), Encoding.UTF8);
            Check(stagedSettings.IndexOf("portableMode=True", StringComparison.OrdinalIgnoreCase) >= 0, "staged settings contain requested mode before live commit");
            Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), "copied phase has not exposed staged state in destination");

            TransactionalStateMigration.Recover(source, target);
            CheckNoArtifacts(source, target, "copied-content recovery");
        }

        private static void TestGeneratedSettingsFile(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "state.txt"), "generated-settings", Encoding.UTF8);

            TransactionalStateMigration.Migrate(source, target, true);
            Check(File.Exists(Path.Combine(target, "settings.ini")), "migration generated missing settings.ini");
            Check(File.ReadAllText(Path.Combine(target, "settings.ini"), Encoding.UTF8).IndexOf("portableMode=True", StringComparison.OrdinalIgnoreCase) >= 0, "generated settings committed requested mode");
            Check(File.ReadAllText(Path.Combine(target, "nested", "state.txt"), Encoding.UTF8) == "generated-settings", "generated settings migration preserved state");
            CheckNoArtifacts(source, target, "generated settings success");
        }

        private static void TestIdenticalDestinationSettingsRollback(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            WriteState(source, "identical");
            CopyDirectory(source, target);
            SetFault("during-commit");
            ExpectException<IOException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "identical destination rollback fault");
            ClearFault();

            AssertSourceFiles(source, "identical", "identical destination source");
            Check(ReadState(target) == "identical", "identical destination state preserved");
            Check(File.ReadAllText(Path.Combine(target, "settings.ini"), Encoding.UTF8).IndexOf("portableMode=False", StringComparison.OrdinalIgnoreCase) >= 0, "replaced destination settings restored after rollback");
            CheckNoArtifacts(source, target, "identical destination rollback");
        }

        private static void TestNestedRootsRejected(string root)
        {
            string parent = Path.Combine(root, "parent");
            string child = Path.Combine(parent, "child");
            WriteState(parent, "nested");
            Directory.CreateDirectory(child);
            ExpectException<InvalidOperationException>(delegate { TransactionalStateMigration.Migrate(parent, child, true); }, "target nested inside source rejected");
            ExpectException<InvalidOperationException>(delegate { TransactionalStateMigration.Migrate(child, parent, false); }, "source nested inside target rejected");
            Check(ReadState(parent) == "nested", "nested-root rejection preserved source");
        }

        private static void TestReparsePointRejected(string root)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Console.WriteLine("SKIP reparse point test requires Windows junctions.");
                return;
            }

            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            string outside = Path.Combine(root, "outside");
            string junction = Path.Combine(source, "junction");
            WriteState(source, "reparse");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "outside", Encoding.UTF8);

            if (!TryCreateJunction(junction, outside))
            {
                Check(false, "test setup created a directory junction");
                return;
            }

            try
            {
                ExpectException<InvalidOperationException>(delegate { TransactionalStateMigration.Migrate(source, target, true); }, "reparse-point directory rejected");
                AssertSourceFiles(source, "reparse", "reparse rejection");
                Check(File.ReadAllText(Path.Combine(outside, "sentinel.txt"), Encoding.UTF8) == "outside", "reparse target remained untouched");
                Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), "reparse rejection did not create destination data");
            }
            finally
            {
                TryRemoveJunction(junction);
            }
        }

        private static void WriteState(string root, string content)
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "state.txt"), content, Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "other.txt"), "other-" + content, Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "settings.ini"), "portableMode=False" + Environment.NewLine + "logVerbosity=Normal" + Environment.NewLine, Encoding.UTF8);
        }

        private static void AssertState(string root, string content, bool portable, string label)
        {
            Check(File.Exists(Path.Combine(root, "nested", "state.txt")), label + " state exists");
            Check(ReadState(root) == content, label + " state content preserved");
            Check(File.ReadAllText(Path.Combine(root, "other.txt"), Encoding.UTF8) == "other-" + content, label + " secondary file preserved");
            string settings = File.ReadAllText(Path.Combine(root, "settings.ini"), Encoding.UTF8);
            Check(settings.IndexOf("portableMode=" + portable, StringComparison.OrdinalIgnoreCase) >= 0, label + " portableMode committed");
        }

        private static void AssertSourceFiles(string source, string content, string label)
        {
            Check(File.ReadAllText(Path.Combine(source, "nested", "state.txt"), Encoding.UTF8) == content, label + " preserved state file");
            Check(File.ReadAllText(Path.Combine(source, "other.txt"), Encoding.UTF8) == "other-" + content, label + " preserved secondary file");
            Check(File.ReadAllText(Path.Combine(source, "settings.ini"), Encoding.UTF8).IndexOf("portableMode=False", StringComparison.OrdinalIgnoreCase) >= 0, label + " preserved source settings");
        }

        private static string ReadState(string root)
        {
            return File.ReadAllText(Path.Combine(root, "nested", "state.txt"), Encoding.UTF8);
        }

        private static bool AllOriginalFilesExistSomewhere(string source, string target)
        {
            string[] relatives = { Path.Combine("nested", "state.txt"), "other.txt", "settings.ini" };
            foreach (string relative in relatives)
                if (!File.Exists(Path.Combine(source, relative)) && !File.Exists(Path.Combine(target, relative)))
                    return false;
            return true;
        }

        private static void CheckNoArtifacts(string source, string target, string label)
        {
            Check(!File.Exists(Path.Combine(source, JournalFileName)) && !File.Exists(Path.Combine(target, JournalFileName)), label + " removed journals");
            Check(StageDirectories(source).Length == 0 && StageDirectories(target).Length == 0, label + " removed staging directories");
        }

        private static bool HasAnyJournal(string first, string second)
        {
            return File.Exists(Path.Combine(first, JournalFileName)) || File.Exists(Path.Combine(second, JournalFileName));
        }

        private static string[] StageDirectories(string root)
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root, StagePrefix + "*", SearchOption.TopDirectoryOnly) : new string[0];
        }

        private static string SingleStageDirectory(string root)
        {
            string[] stages = StageDirectories(root);
            Check(stages.Length == 1, "exactly one staging directory exists");
            if (stages.Length != 1)
                throw new InvalidOperationException("Expected one staging directory.");
            return stages[0];
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        private static void SetFault(string fault)
        {
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", fault);
        }

        private static void ClearFault()
        {
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null);
        }

        private static void ExpectException<T>(Action action, string name) where T : Exception
        {
            bool threw = false;
            try
            {
                action();
            }
            catch (T)
            {
                threw = true;
            }
            Check(threw, name);
        }

        private static bool TryCreateJunction(string junction, string target)
        {
            ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + junction + "\" \"" + target + "\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(junction);
            }
        }

        private static void TryRemoveJunction(string junction)
        {
            try
            {
                if (!Directory.Exists(junction))
                    return;
                ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/d /c rmdir \"" + junction + "\"");
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                using (Process process = Process.Start(start))
                    process.WaitForExit();
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static string Sanitize(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in value)
                builder.Append(Char.IsLetterOrDigit(c) ? c : '-');
            return builder.ToString();
        }

        private static void Check(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
                return;
            }
            failures++;
            Console.Error.WriteLine("FAIL " + name);
        }
    }
}
