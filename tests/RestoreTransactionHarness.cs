using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

internal static class RestoreTransactionHarness
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static Assembly AppAssembly;
    private static Type MainFormType;
    private static Type EntryType;
    private static int Passed;
    private static int Failed;

    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");
            AppAssembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            MainFormType = AppAssembly.GetType("CK3MPS.MainForm", true);
            EntryType = MainFormType.GetNestedType("RestoreEntry", BindingFlags.NonPublic);
            if (EntryType == null)
                throw new InvalidOperationException("RestoreEntry type was not found.");

            Run("successful directory replacement", TestSuccessfulDirectoryReplacement);
            Run("successful file restore and identical no-op", TestSuccessfulFileRestoreAndNoOp);
            Run("staging and commit fault rollback", TestDirectoryFaultRollbackMatrix);
            Run("multi-file rollback and manifest restoration", TestMultiFileRollbackAndManifest);
            Run("file plus directory rollback", TestFileDirectoryRollback);
            Run("created_file rollback", TestCreatedFileRollback);
            Run("registry rollback and repeated rollback", TestRegistryRollbackAndRepeatedRollback);
            Run("confirmation snapshot detects target change", TestConfirmationSnapshotChange);
            Run("reparse point is rejected", TestReparsePointRejected);
            Run("moved_file and moved_directory support", TestMovedKinds);
            Run("post-commit user data is not deleted", TestPostCommitUserDataProtection);

            Console.WriteLine("Restore transaction tests: {0} passed, {1} failed.", Passed, Failed);
            return Failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Passed++;
            Console.WriteLine("PASS {0}", name);
        }
        catch (Exception ex)
        {
            Failed++;
            Console.Error.WriteLine("FAIL {0}: {1}", name, ex);
        }
    }

    private static void TestSuccessfulDirectoryReplacement()
    {
        using (TestContext context = new TestContext())
        {
            string target = Path.Combine(context.DocsRoot, "shadercache");
            WriteDirectory(target, new Dictionary<string, string> { { "old.txt", "old" } });
            string backup = context.CreateBackupDirectory("directory-success", new Dictionary<string, string>
            {
                { "new.txt", "new" },
                { Path.Combine("nested", "value.txt"), "nested-new" }
            });
            object entry = context.CreateEntry("directory", target, backup, "old directory");
            context.Execute(context.CreateEntryList(entry), false, null, -1, true);

            Assert(!File.Exists(Path.Combine(target, "old.txt")), "old directory content should be replaced");
            Assert(File.ReadAllText(Path.Combine(target, "new.txt")) == "new", "new directory content should be installed");
            Assert(File.ReadAllText(Path.Combine(target, "nested", "value.txt")) == "nested-new", "nested directory content should be installed");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestSuccessfulFileRestoreAndNoOp()
    {
        using (TestContext context = new TestContext())
        {
            string target = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            File.WriteAllText(target, "old");
            string backup = context.CreateBackupFile("file-success.txt", "new");
            object entry = context.CreateEntry("file", target, backup, "old file");
            context.Execute(context.CreateEntryList(entry), false, null, -1, true);
            Assert(File.ReadAllText(target) == "new", "file restore should install backup content");
            context.AssertNoTransactionTemps();

            string manifestBefore = File.ReadAllText(context.ManifestPath);
            object noOpEntry = context.CreateEntry("file", target, backup, "identical file");
            context.Execute(context.CreateEntryList(noOpEntry), false, null, -1, true);
            Assert(File.ReadAllText(target) == "new", "identical restore should remain unchanged");
            Assert(File.ReadAllText(context.ManifestPath) == manifestBefore, "identical restore should not add a manifest action");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestDirectoryFaultRollbackMatrix()
    {
        string[] faults = { "staging-copy", "before-rename", "after-target-to-rollback", "after-new-target", "manifest-log" };
        foreach (string fault in faults)
        {
            using (TestContext context = new TestContext())
            {
                string target = Path.Combine(context.DocsRoot, "logs");
                WriteDirectory(target, new Dictionary<string, string> { { "old.log", "old-" + fault } });
                string backup = context.CreateBackupDirectory("fault-" + fault, new Dictionary<string, string> { { "new.log", "new-" + fault } });
                object entry = context.CreateEntry("directory", target, backup, "fault " + fault);
                string manifestBefore = File.ReadAllText(context.ManifestPath);

                Exception error = ExpectFailure(delegate
                {
                    context.Execute(context.CreateEntryList(entry), false, fault, 0, true);
                });
                Assert(error.Message.IndexOf("rolled back", StringComparison.OrdinalIgnoreCase) >= 0, "fault should report completed rollback: " + fault);
                Assert(File.Exists(Path.Combine(target, "old.log")), "original directory should be restored after " + fault);
                Assert(File.ReadAllText(Path.Combine(target, "old.log")) == "old-" + fault, "original directory content should survive " + fault);
                Assert(!File.Exists(Path.Combine(target, "new.log")), "new directory content should be removed after " + fault);
                Assert(File.ReadAllText(context.ManifestPath) == manifestBefore, "manifest should be restored after " + fault);
                context.AssertNoTransactionTemps();
            }
        }
    }

    private static void TestMultiFileRollbackAndManifest()
    {
        using (TestContext context = new TestContext())
        {
            string first = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            string second = Path.Combine(context.DocsRoot, "dlc_load.json");
            File.WriteAllText(first, "first-old");
            File.WriteAllText(second, "second-old");
            string firstBackup = context.CreateBackupFile("first.txt", "first-new");
            string secondBackup = context.CreateBackupFile("second.txt", "second-new");
            object firstEntry = context.CreateEntry("file", first, firstBackup, "first");
            object secondEntry = context.CreateEntry("file", second, secondBackup, "second");
            object list = context.CreateEntryList(firstEntry, secondEntry);
            string manifestBefore = File.ReadAllText(context.ManifestPath);

            ExpectFailure(delegate { context.Execute(list, false, "after-new-target", 1, true); });
            Assert(File.ReadAllText(first) == "first-old", "first applied file should be rolled back");
            Assert(File.ReadAllText(second) == "second-old", "failing file should be rolled back");
            Assert(File.ReadAllText(context.ManifestPath) == manifestBefore, "batch rollback should restore the manifest exactly");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestFileDirectoryRollback()
    {
        using (TestContext context = new TestContext())
        {
            string fileTarget = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            string directoryTarget = Path.Combine(context.DocsRoot, "oos");
            File.WriteAllText(fileTarget, "file-old");
            WriteDirectory(directoryTarget, new Dictionary<string, string> { { "old.txt", "directory-old" } });
            string fileBackup = context.CreateBackupFile("combo-file.txt", "file-new");
            string directoryBackup = context.CreateBackupDirectory("combo-directory", new Dictionary<string, string> { { "new.txt", "directory-new" } });
            object fileEntry = context.CreateEntry("file", fileTarget, fileBackup, "combo file");
            object directoryEntry = context.CreateEntry("directory", directoryTarget, directoryBackup, "combo directory");

            ExpectFailure(delegate
            {
                context.Execute(context.CreateEntryList(fileEntry, directoryEntry), false, "after-new-target", 1, true);
            });
            Assert(File.ReadAllText(fileTarget) == "file-old", "file should roll back in mixed batch");
            Assert(File.Exists(Path.Combine(directoryTarget, "old.txt")), "directory should roll back in mixed batch");
            Assert(!File.Exists(Path.Combine(directoryTarget, "new.txt")), "new directory should not remain in mixed batch");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestCreatedFileRollback()
    {
        using (TestContext context = new TestContext())
        {
            string createdTarget = Path.Combine(context.DocsRoot, "continue_game.json");
            string laterTarget = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            File.WriteAllText(createdTarget, "created-current");
            File.WriteAllText(laterTarget, "later-old");
            string laterBackup = context.CreateBackupFile("created-later.txt", "later-new");
            object createdEntry = context.CreateEntry("created_file", createdTarget, "", "created file");
            object laterEntry = context.CreateEntry("file", laterTarget, laterBackup, "later file");

            ExpectFailure(delegate
            {
                context.Execute(context.CreateEntryList(createdEntry, laterEntry), false, "after-new-target", 1, true);
            });
            Assert(File.Exists(createdTarget), "created_file should be restored when a later item fails");
            Assert(File.ReadAllText(createdTarget) == "created-current", "created_file rollback should preserve its snapshot");
            Assert(File.ReadAllText(laterTarget) == "later-old", "later file should also roll back");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestRegistryRollbackAndRepeatedRollback()
    {
        string subKey = @"Software\CK3MPS\Tests\" + Guid.NewGuid().ToString("N");
        string valueName = "RestoreValue";
        string registryPath = @"HKCU\" + subKey + @"\" + valueName;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
                key.SetValue(valueName, "current", RegistryValueKind.String);

            using (TestContext context = new TestContext())
            {
                string laterTarget = Path.Combine(context.DocsRoot, "pdx_settings.txt");
                File.WriteAllText(laterTarget, "later-old");
                string laterBackup = context.CreateBackupFile("registry-later.txt", "later-new");
                object registryEntry = context.CreateEntry("registry", registryPath, "", "registry", "String:restored");
                object laterEntry = context.CreateEntry("file", laterTarget, laterBackup, "later");

                ExpectFailure(delegate
                {
                    context.Execute(context.CreateEntryList(registryEntry, laterEntry), false, "after-new-target", 1, true);
                });
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                    Assert(Convert.ToString(key.GetValue(valueName)) == "current", "registry value should roll back to its pre-batch value");

                IEnumerable rollbackRecords = context.GetField("restoreTransactionTestLastRollbackRecords") as IEnumerable;
                Assert(rollbackRecords != null, "test rollback records should be available");
                foreach (object record in rollbackRecords)
                    context.Invoke("RollbackRestoreRecord", record);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                    Assert(Convert.ToString(key.GetValue(valueName)) == "current", "repeated rollback should be idempotent");
                Assert(File.ReadAllText(laterTarget) == "later-old", "repeated rollback should leave file snapshot intact");
                context.AssertNoTransactionTemps();
            }
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
        }
    }

    private static void TestConfirmationSnapshotChange()
    {
        using (TestContext context = new TestContext())
        {
            string target = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            File.WriteAllText(target, "confirmed-old");
            string backup = context.CreateBackupFile("confirmation.txt", "restore-new");
            object entry = context.CreateEntry("file", target, backup, "confirmation");
            object list = context.CreateEntryList(entry);
            context.CaptureConfirmationSnapshots(list);
            File.WriteAllText(target, "changed-after-confirmation");
            string manifestBefore = File.ReadAllText(context.ManifestPath);

            Exception error = ExpectFailure(delegate
            {
                context.Execute(list, false, null, -1, false);
            });
            Assert(error.Message.IndexOf("changed after confirmation", StringComparison.OrdinalIgnoreCase) >= 0, "target change should be detected before commit");
            Assert(File.ReadAllText(target) == "changed-after-confirmation", "changed target should not be overwritten");
            Assert(File.ReadAllText(context.ManifestPath) == manifestBefore, "blocked confirmation change should not alter manifest");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestReparsePointRejected()
    {
        using (TestContext context = new TestContext())
        {
            string outside = Path.Combine(context.TempRoot, "junction-target");
            WriteDirectory(outside, new Dictionary<string, string> { { "outside.txt", "outside" } });
            string junction = Path.Combine(context.DocsRoot, "shadercache");
            CreateJunction(junction, outside);
            try
            {
                string backup = context.CreateBackupDirectory("reparse-backup", new Dictionary<string, string> { { "new.txt", "new" } });
                object entry = context.CreateEntry("directory", junction, backup, "reparse");
                ExpectFailure(delegate { context.Execute(context.CreateEntryList(entry), false, null, -1, true); });
                Assert(File.ReadAllText(Path.Combine(outside, "outside.txt")) == "outside", "reparse target data should remain untouched");
            }
            finally
            {
                RemoveJunction(junction);
            }
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestMovedKinds()
    {
        using (TestContext context = new TestContext())
        {
            string movedFileTarget = Path.Combine(context.DocsRoot, "save games", "campaign.ck3");
            string movedFileBackup = context.CreateBackupFile("campaign.ck3", "save-data");
            object movedFileEntry = context.CreateEntry("moved_file", movedFileTarget, movedFileBackup, "moved file");
            context.Execute(context.CreateEntryList(movedFileEntry), false, null, -1, true);
            Assert(File.ReadAllText(movedFileTarget) == "save-data", "moved_file should restore a missing workflow save");

            string movedDirectoryTarget = Path.Combine(context.DocsRoot, "logs");
            string movedDirectoryBackup = context.CreateBackupDirectory("moved-directory", new Dictionary<string, string> { { "restored.log", "log-data" } });
            object movedDirectoryEntry = context.CreateEntry("moved_directory", movedDirectoryTarget, movedDirectoryBackup, "moved directory");
            context.Execute(context.CreateEntryList(movedDirectoryEntry), false, null, -1, true);
            Assert(File.ReadAllText(Path.Combine(movedDirectoryTarget, "restored.log")) == "log-data", "moved_directory should restore a missing allowed directory");
            context.AssertNoTransactionTemps();
        }
    }

    private static void TestPostCommitUserDataProtection()
    {
        using (TestContext context = new TestContext())
        {
            string target = Path.Combine(context.DocsRoot, "pdx_settings.txt");
            File.WriteAllText(target, "original");
            string backup = context.CreateBackupFile("user-data-protection.txt", "installed");
            object entry = context.CreateEntry("file", target, backup, "user data protection");
            object list = context.CreateEntryList(entry);
            context.CaptureConfirmationSnapshots(list);

            ManualResetEvent reached = new ManualResetEvent(false);
            ManualResetEvent proceed = new ManualResetEvent(false);
            context.SetField("restoreTransactionTestAfterCommitReached", reached);
            context.SetField("restoreTransactionTestContinueAfterCommit", proceed);
            Exception workerError = null;
            Thread worker = new Thread(delegate
            {
                try
                {
                    context.Execute(list, false, "after-new-target-pause", 0, false);
                }
                catch (Exception ex)
                {
                    workerError = ex;
                }
            });
            worker.IsBackground = true;
            worker.Start();
            Assert(reached.WaitOne(10000), "restore did not reach the post-commit test barrier");
            File.WriteAllText(target, "user-data-after-start");
            proceed.Set();
            Assert(worker.Join(15000), "restore worker did not finish");
            Assert(workerError != null, "post-commit mutation should cause a rollback conflict");
            Assert(File.ReadAllText(target) == "user-data-after-start", "rollback must not delete or overwrite post-start user data");
            string transactionParent = Path.Combine(context.StateRoot, "restore_transactions");
            Assert(Directory.Exists(transactionParent), "recovery data should be preserved after a safe rollback refusal");
        }
    }

    private static Exception ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Expected the operation to fail.");
    }

    private static void WriteDirectory(string root, IDictionary<string, string> files)
    {
        Directory.CreateDirectory(root);
        foreach (KeyValuePair<string, string> pair in files)
        {
            string path = Path.Combine(root, pair.Key);
            string parent = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, pair.Value);
        }
    }

    private static void CreateJunction(string junction, string target)
    {
        ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + target + "\"");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using (Process process = Process.Start(info))
        {
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(junction))
                throw new InvalidOperationException("Could not create test junction: " + process.StandardError.ReadToEnd());
        }
    }

    private static void RemoveJunction(string junction)
    {
        if (!Directory.Exists(junction))
            return;
        ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "/c rmdir \"" + junction + "\"");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using (Process process = Process.Start(info))
            process.WaitForExit();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TestContext : IDisposable
    {
        public readonly string TempRoot;
        public readonly string DocsRoot;
        public readonly string StateRoot;
        public readonly string QuarantineRoot;
        public readonly string BackupRoot;
        public readonly string ManifestPath;
        public readonly object Form;

        public TestContext()
        {
            TempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-restore-transactions-" + Guid.NewGuid().ToString("N"));
            DocsRoot = Path.Combine(TempRoot, "Crusader Kings III");
            StateRoot = Path.Combine(TempRoot, "CK3MPS_State");
            QuarantineRoot = Path.Combine(StateRoot, "quarantine");
            BackupRoot = Path.Combine(QuarantineRoot, "restore_backups");
            ManifestPath = Path.Combine(QuarantineRoot, "restore_manifest.tsv");
            Directory.CreateDirectory(Path.Combine(DocsRoot, "save games"));
            Directory.CreateDirectory(BackupRoot);
            File.WriteAllText(ManifestPath, "id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\trun_id\r\n");

            Form = Activator.CreateInstance(MainFormType, true);
            SetField("ck3Docs", DocsRoot);
            SetField("stabilizerRoot", StateRoot);
            SetField("lastQuarantine", QuarantineRoot);
            SetField("liveLogWritesEnabled", false);
        }

        public object CreateEntry(string kind, string source, string backup, string description)
        {
            return CreateEntry(kind, source, backup, description, "before");
        }

        public object CreateEntry(string kind, string source, string backup, string description, string before)
        {
            object entry = Activator.CreateInstance(EntryType, true);
            SetEntryField(entry, "Id", Guid.NewGuid().ToString("N"));
            SetEntryField(entry, "Kind", kind);
            SetEntryField(entry, "SourcePath", source);
            SetEntryField(entry, "BackupPath", backup);
            SetEntryField(entry, "Description", description);
            SetEntryField(entry, "Before", before);
            SetEntryField(entry, "After", "after");
            SetEntryField(entry, "Created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            SetEntryField(entry, "Status", "active");
            SetEntryField(entry, "RunId", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            return entry;
        }

        public object CreateEntryList(params object[] entries)
        {
            Type listType = typeof(List<>).MakeGenericType(EntryType);
            object list = Activator.CreateInstance(listType);
            MethodInfo add = listType.GetMethod("Add");
            foreach (object entry in entries)
                add.Invoke(list, new[] { entry });
            return list;
        }

        public string CreateBackupFile(string name, string content)
        {
            string path = Path.Combine(BackupRoot, Guid.NewGuid().ToString("N") + "_" + name);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateBackupDirectory(string name, IDictionary<string, string> files)
        {
            string path = Path.Combine(BackupRoot, Guid.NewGuid().ToString("N") + "_" + name);
            WriteDirectory(path, files);
            return path;
        }

        public void CaptureConfirmationSnapshots(object list)
        {
            object snapshots = Invoke("CaptureRestoreOperationSnapshots", list);
            SetField("activeRestoreOperationSnapshots", snapshots);
        }

        public void Execute(object list, bool restoreDefault, string faultPoint, int faultIndex, bool captureSnapshots)
        {
            if (captureSnapshots)
                CaptureConfirmationSnapshots(list);
            SetField("restoreTransactionTestFaultPoint", faultPoint ?? "");
            SetField("restoreTransactionTestFaultIndex", faultIndex);
            try
            {
                Invoke("ExecuteRestoreBatch", list, restoreDefault);
            }
            finally
            {
                SetField("activeRestoreOperationSnapshots", null);
                SetField("restoreTransactionTestFaultPoint", "");
                SetField("restoreTransactionTestFaultIndex", -1);
            }
        }

        public object Invoke(string methodName, params object[] parameters)
        {
            MethodInfo method = MainFormType.GetMethod(methodName, Flags);
            if (method == null)
                throw new InvalidOperationException("Method not found: " + methodName);
            try
            {
                return method.Invoke(Form, parameters);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        public void SetField(string fieldName, object value)
        {
            FieldInfo field = MainFormType.GetField(fieldName, Flags);
            if (field == null)
                throw new InvalidOperationException("Field not found: " + fieldName);
            field.SetValue(Form, value);
        }

        public object GetField(string fieldName)
        {
            FieldInfo field = MainFormType.GetField(fieldName, Flags);
            if (field == null)
                throw new InvalidOperationException("Field not found: " + fieldName);
            return field.GetValue(Form);
        }

        public void AssertNoTransactionTemps()
        {
            string transactionParent = Path.Combine(StateRoot, "restore_transactions");
            Assert(!Directory.Exists(transactionParent), "restore transaction directory should be removed");
            foreach (string directory in Directory.GetDirectories(TempRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(directory);
                Assert(!name.StartsWith(".ck3mps-restore-stage-", StringComparison.OrdinalIgnoreCase), "staging directory remains: " + directory);
                Assert(!name.StartsWith(".ck3mps-restore-rollback-", StringComparison.OrdinalIgnoreCase), "rollback directory remains: " + directory);
            }
            foreach (string file in Directory.GetFiles(TempRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                Assert(!(name.IndexOf(".restore-", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)), "restore temp file remains: " + file);
            }
        }

        public void Dispose()
        {
            try
            {
                IDisposable disposable = Form as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
            catch { }
            try
            {
                if (Directory.Exists(TempRoot))
                    Directory.Delete(TempRoot, true);
            }
            catch { }
        }

        private static void SetEntryField(object entry, string fieldName, object value)
        {
            FieldInfo field = EntryType.GetField(fieldName, Flags);
            if (field == null)
                throw new InvalidOperationException("RestoreEntry field not found: " + fieldName);
            field.SetValue(entry, value);
        }
    }
}
