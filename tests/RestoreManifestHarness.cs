using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;

internal static class RestoreManifestHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-restore-manifest-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string stateRoot = Path.Combine(tempRoot, "CK3MPS_State");
        string quarantineRoot = Path.Combine(stateRoot, "quarantine");
        string backupRoot = Path.Combine(quarantineRoot, "restore_backups");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(docsRoot);
            Directory.CreateDirectory(backupRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);

            string sourceA = Path.Combine(docsRoot, "pdx_settings.txt");
            string sourceB = Path.Combine(docsRoot, "dlc_load.json");
            string backupA = Path.Combine(backupRoot, "pdx_settings.txt");
            string backupB = Path.Combine(backupRoot, "dlc_load.json");
            string sourceMod = Path.Combine(docsRoot, "mod", "payload.bin");
            string backupMod = Path.Combine(backupRoot, "payload.bin");
            string compareLeft = Path.Combine(tempRoot, "compare-left");
            string compareRight = Path.Combine(tempRoot, "compare-right");
            File.WriteAllText(sourceA, "graphics={}\r\n");
            File.WriteAllText(sourceB, "{\"enabled_mods\":[]}\r\n");
            File.WriteAllText(backupA, "backup-a\r\n");
            File.WriteAllText(backupB, "backup-b\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceMod));
            File.WriteAllText(sourceMod, "untrusted-mod-payload");
            File.WriteAllText(backupMod, "replacement-payload");
            Directory.CreateDirectory(Path.Combine(compareLeft, "nested", "empty"));
            Directory.CreateDirectory(Path.Combine(compareRight, "nested", "empty"));
            File.WriteAllText(Path.Combine(compareLeft, "nested", "same.txt"), "same");
            File.WriteAllText(Path.Combine(compareRight, "nested", "same.txt"), "same");

            string manifestPath = Path.Combine(quarantineRoot, "restore_manifest.tsv");
            File.WriteAllText(manifestPath, BuildManifest(sourceA, sourceB, sourceMod, backupA, backupB, backupMod), new UTF8Encoding(false));

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "lastQuarantine", quarantineRoot, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                IEnumerable entries = (IEnumerable)Invoke(mainFormType, form, "ReadRestoreEntries", flags);

                int count = 0;
                int duplicateCount = 0;
                bool sawMalformedColumns = false;
                bool sawMalformedCreated = false;
                bool sawMalformedRunId = false;
                bool sawUnsupportedKind = false;
                bool sawBlockedModPayload = false;

                foreach (object entry in entries)
                {
                    count++;
                    Type entryType = entry.GetType();
                    string description = Convert.ToString(entryType.GetField("Description").GetValue(entry)) ?? "";
                    string validation = Convert.ToString(entryType.GetField("ValidationError").GetValue(entry)) ?? "";
                    string status = Convert.ToString(entryType.GetField("Status").GetValue(entry)) ?? "";

                    Assert(String.Equals(status, "invalid", StringComparison.OrdinalIgnoreCase), "every malformed manifest row should surface as invalid");

                    if (validation.IndexOf("duplicated", StringComparison.OrdinalIgnoreCase) >= 0)
                        duplicateCount++;
                    if (validation.IndexOf("9 or 10 tab-separated columns", StringComparison.OrdinalIgnoreCase) >= 0)
                        sawMalformedColumns = true;
                    if (validation.IndexOf("Created timestamp is malformed", StringComparison.OrdinalIgnoreCase) >= 0)
                        sawMalformedCreated = true;
                    if (validation.IndexOf("Run ID is malformed", StringComparison.OrdinalIgnoreCase) >= 0)
                        sawMalformedRunId = true;
                    if (validation.IndexOf("kind is not supported", StringComparison.OrdinalIgnoreCase) >= 0)
                        sawUnsupportedKind = true;
                    if (validation.IndexOf("outside the CK3/Paradox allowlist", StringComparison.OrdinalIgnoreCase) >= 0)
                        sawBlockedModPayload = true;

                    if (description.IndexOf("Invalid restore manifest row", StringComparison.OrdinalIgnoreCase) >= 0)
                        Assert(validation.Length > 0, "synthetic invalid manifest entries should keep a validation message");
                }

                Assert(count == 8, "all eight manifest rows should remain visible for operator review");
                Assert(duplicateCount == 2, "both rows that share a duplicated restore entry id should be invalidated");
                Assert(sawMalformedColumns, "manifest parser should surface malformed column counts");
                Assert(sawMalformedCreated, "manifest parser should surface malformed created timestamps");
                Assert(sawMalformedRunId, "manifest parser should surface malformed run ids");
                Assert(sawUnsupportedKind, "manifest parser should still surface unsupported kinds");
                Assert(sawBlockedModPayload, "manifest parser should reject arbitrary files under the CK3 mod directory");

                Type restoreEntryType = mainFormType.GetNestedType("RestoreEntry", BindingFlags.NonPublic);
                object trustedEntry = Activator.CreateInstance(restoreEntryType, true);
                restoreEntryType.GetField("Id").SetValue(trustedEntry, "toctou-entry");
                restoreEntryType.GetField("Kind").SetValue(trustedEntry, "file");
                restoreEntryType.GetField("SourcePath").SetValue(trustedEntry, sourceA);
                restoreEntryType.GetField("BackupPath").SetValue(trustedEntry, backupA);
                Array trustedEntries = Array.CreateInstance(restoreEntryType, 1);
                trustedEntries.SetValue(trustedEntry, 0);
                object snapshots = Invoke(mainFormType, form, "CaptureRestoreOperationSnapshots", flags, trustedEntries);
                mainFormType.GetField("activeRestoreOperationSnapshots", flags).SetValue(form, snapshots);
                File.AppendAllText(sourceA, "changed-after-confirmation");
                bool toctouBlocked = false;
                try
                {
                    Invoke(mainFormType, form, "EnsureRestoreOperationStillAllowed", flags, trustedEntry, "test restore");
                }
                catch (TargetInvocationException ex)
                {
                    toctouBlocked = ex.InnerException != null && ex.InnerException.Message.IndexOf("changed after confirmation", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                Assert(toctouBlocked, "restore should block a source file that changes after confirmation");
                mainFormType.GetField("activeRestoreOperationSnapshots", flags).SetValue(form, null);

                bool sameDirectories = Convert.ToBoolean(Invoke(mainFormType, form, "DirectoryContentsEqual", flags, compareLeft, compareRight));
                Assert(sameDirectories, "restore directory compare should treat identical bounded trees as equal");
                File.WriteAllText(Path.Combine(compareRight, "nested", "same.txt"), "different");
                bool changedDirectories = Convert.ToBoolean(Invoke(mainFormType, form, "DirectoryContentsEqual", flags, compareLeft, compareRight));
                Assert(!changedDirectories, "restore directory compare should detect changed file contents without AllDirectories traversal");
                return 0;
            }
            finally
            {
                IDisposable disposable = form as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine(ex.InnerException ?? ex);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
    }

    private static string BuildManifest(string sourceA, string sourceB, string sourceMod, string backupA, string backupB, string backupMod)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\trun_id");
        sb.AppendLine("dup001\t2026-07-16 09:46:00\tfile\t" + sourceA + "\t" + backupA + "\tduplicate first\tbefore\tafter\tactive\t20260716_094600");
        sb.AppendLine("dup001\t2026-07-16 09:46:01\tfile\t" + sourceB + "\t" + backupB + "\tduplicate second\tbefore\tafter\tactive\t20260716_094601");
        sb.AppendLine("short-row");
        sb.AppendLine("badcreated\t2026/07/16\tfile\t" + sourceA + "\t" + backupA + "\tbad created\tbefore\tafter\tactive\t20260716_094602");
        sb.AppendLine("badrun\t2026-07-16 09:46:03\tfile\t" + sourceA + "\t" + backupA + "\tbad run\tbefore\tafter\tactive\t2026bad");
        sb.AppendLine("badkind\t2026-07-16 09:46:04\tdangerous_kind\t" + sourceA + "\t" + backupA + "\tbad kind\tbefore\tafter\tactive\t20260716_094604");
        sb.AppendLine("toomany\t2026-07-16 09:46:05\tfile\t" + sourceA + "\t" + backupA + "\textra columns\tbefore\tafter\tactive\t20260716_094605\textra");
        sb.AppendLine("badmod\t2026-07-16 09:46:06\tfile\t" + sourceMod + "\t" + backupMod + "\tmod payload\tbefore\tafter\tactive\t20260716_094606");
        return sb.ToString();
    }

    private static object Invoke(Type type, object instance, string methodName, BindingFlags flags, params object[] parameters)
    {
        MethodInfo method = type.GetMethod(methodName, flags);
        if (method == null)
            throw new InvalidOperationException("Method not found: " + methodName);
        return method.Invoke(instance, parameters);
    }

    private static void SetField(Type type, object instance, string fieldName, object value, BindingFlags flags)
    {
        FieldInfo field = type.GetField(fieldName, flags);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(instance, value);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
