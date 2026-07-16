using System;
using System.IO;

namespace CK3MPS
{
    internal static class TransactionalMigrationTests
    {
        private static int failures;

        private static void Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "CK3MPS-transaction-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestSuccessfulCommit(root);
                TestConflictLeavesBothRootsUntouched(root);
                TestCopiedFaultRollsBack(root);
                TestCommittedFaultRecovers(root);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null);
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }

            if (failures > 0)
                Environment.Exit(1);
            Console.WriteLine("Transactional migration tests passed.");
        }

        private static void TestSuccessfulCommit(string root)
        {
            string source = Path.Combine(root, "success-source");
            string target = Path.Combine(root, "success-target");
            WriteState(source, "alpha");

            TransactionalStateMigration.Migrate(source, target, true);

            Check(File.Exists(Path.Combine(target, "nested", "state.txt")), "successful migration copied state");
            Check(File.ReadAllText(Path.Combine(target, "nested", "state.txt")) == "alpha", "successful migration preserved content");
            Check(File.ReadAllText(Path.Combine(target, "settings.ini")).IndexOf("portableMode=True", StringComparison.OrdinalIgnoreCase) >= 0, "successful migration committed target mode");
            Check(!File.Exists(Path.Combine(source, "nested", "state.txt")), "successful migration cleaned source");
            Check(!File.Exists(Path.Combine(source, ".ck3mps-state-migration")) && !File.Exists(Path.Combine(target, ".ck3mps-state-migration")), "successful migration removed journals");
        }

        private static void TestConflictLeavesBothRootsUntouched(string root)
        {
            string source = Path.Combine(root, "conflict-source");
            string target = Path.Combine(root, "conflict-target");
            WriteState(source, "source");
            WriteState(target, "target");

            bool threw = false;
            try { TransactionalStateMigration.Migrate(source, target, true); }
            catch (IOException) { threw = true; }

            Check(threw, "conflicting roots are rejected");
            Check(File.ReadAllText(Path.Combine(source, "nested", "state.txt")) == "source", "conflict preserved source");
            Check(File.ReadAllText(Path.Combine(target, "nested", "state.txt")) == "target", "conflict preserved target");
        }

        private static void TestCopiedFaultRollsBack(string root)
        {
            string source = Path.Combine(root, "copied-source");
            string target = Path.Combine(root, "copied-target");
            WriteState(source, "copied-fault");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", "copied");
            bool threw = false;
            try { TransactionalStateMigration.Migrate(source, target, true); }
            catch (IOException) { threw = true; }
            finally { Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null); }

            Check(threw, "copied-phase fault was injected");
            Check(File.Exists(Path.Combine(source, "nested", "state.txt")), "copied-phase rollback preserved source");
            Check(!File.Exists(Path.Combine(target, "nested", "state.txt")), "copied-phase rollback removed created target");
            Check(!File.Exists(Path.Combine(source, ".ck3mps-state-migration")) && !File.Exists(Path.Combine(target, ".ck3mps-state-migration")), "copied-phase rollback removed journals");
        }

        private static void TestCommittedFaultRecovers(string root)
        {
            string source = Path.Combine(root, "committed-source");
            string target = Path.Combine(root, "committed-target");
            WriteState(source, "committed-fault");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", "committed");
            bool threw = false;
            try { TransactionalStateMigration.Migrate(source, target, true); }
            catch (IOException) { threw = true; }
            finally { Environment.SetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT", null); }

            Check(threw, "committed-phase fault was injected");
            Check(File.Exists(Path.Combine(target, ".ck3mps-state-migration")), "committed fault preserved recovery journal");
            bool? recovered = TransactionalStateMigration.Recover(source, target);
            Check(recovered.HasValue && recovered.Value, "recovery returned committed portable mode");
            Check(File.Exists(Path.Combine(target, "nested", "state.txt")), "recovery preserved committed target");
            Check(!File.Exists(Path.Combine(source, "nested", "state.txt")), "recovery completed source cleanup");
            Check(!File.Exists(Path.Combine(source, ".ck3mps-state-migration")) && !File.Exists(Path.Combine(target, ".ck3mps-state-migration")), "recovery removed journals");
        }

        private static void WriteState(string root, string content)
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "state.txt"), content);
            File.WriteAllText(Path.Combine(root, "settings.ini"), "portableMode=False" + Environment.NewLine + "logVerbosity=Normal" + Environment.NewLine);
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
