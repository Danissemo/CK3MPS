using System;
using System.IO;
using System.Reflection;

internal static class WorkflowCrashRecoveryHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-workflow-crash-recovery-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string saveRoot = Path.Combine(docsRoot, "save games");
        string stateRoot = Path.Combine(tempRoot, "CK3MPS_State");
        string quarantineRoot = Path.Combine(stateRoot, "quarantine");
        string savePath = Path.Combine(saveRoot, "campaign.ck3");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(saveRoot);
            Directory.CreateDirectory(quarantineRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);
            File.WriteAllText(Path.Combine(docsRoot, "pdx_settings.txt"), "graphics={}\r\n");
            File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
            File.WriteAllText(savePath, "save-content");

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            string quarantinedPath;
            using (IDisposable form = (IDisposable)Activator.CreateInstance(mainFormType, true))
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "lastQuarantine", quarantineRoot, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                Invoke(mainFormType, form, "InitializeRestoreManifest", flags);
                object[] quarantineArgs = new object[] { savePath, null };
                Invoke(mainFormType, form, "QuarantineWorkflowSaveTransactional", flags, quarantineArgs);
                quarantinedPath = Convert.ToString(quarantineArgs[1]) ?? "";
            }

            Assert(!String.IsNullOrWhiteSpace(quarantinedPath) && File.Exists(quarantinedPath), "workflow save should exist in quarantine before restart recovery");
            Assert(!File.Exists(savePath), "workflow source should be absent before restart recovery");

            string manifestPath = Path.Combine(quarantineRoot, "restore_manifest.tsv");
            string manifestText = File.ReadAllText(manifestPath);
            manifestText = manifestText.Replace("\tcommitted\t", "\tprepared\t");
            File.WriteAllText(manifestPath, manifestText);

            using (IDisposable restartedForm = (IDisposable)Activator.CreateInstance(mainFormType, true))
            {
                SetField(mainFormType, restartedForm, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, restartedForm, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, restartedForm, "lastQuarantine", quarantineRoot, flags);
                SetField(mainFormType, restartedForm, "liveLogWritesEnabled", false, flags);

                object entries = Invoke(mainFormType, restartedForm, "ReadRestoreEntries", flags);
                object reconciledEntry = FindEntry(entries, quarantinedPath);
                Assert(reconciledEntry != null, "restart recovery should keep the workflow restore entry visible");

                Type entryType = reconciledEntry.GetType();
                string status = Convert.ToString(entryType.GetField("Status").GetValue(reconciledEntry)) ?? "";
                Assert(String.Equals(status, "committed", StringComparison.OrdinalIgnoreCase), "prepared workflow move should reconcile to committed after restart when backup exists and source is missing");

                Invoke(mainFormType, restartedForm, "RestoreFileEntry", flags, reconciledEntry);
                Assert(File.Exists(savePath), "reconciled workflow entry should restore the save to the original path");
                Assert(File.ReadAllText(savePath) == "save-content" + Environment.NewLine || File.ReadAllText(savePath) == "save-content", "restored workflow save should preserve original content after restart recovery");
            }

            return 0;
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

    private static object FindEntry(object entriesObject, string backupPath)
    {
        System.Collections.IEnumerable entries = entriesObject as System.Collections.IEnumerable;
        if (entries == null)
            return null;

        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            string kind = Convert.ToString(entryType.GetField("Kind").GetValue(entry)) ?? "";
            string backup = Convert.ToString(entryType.GetField("BackupPath").GetValue(entry)) ?? "";
            if (String.Equals(kind, "moved_file", StringComparison.OrdinalIgnoreCase)
                && String.Equals(backup, backupPath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
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
