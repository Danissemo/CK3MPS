using System;
using System.IO;
using System.Reflection;

internal static class WorkflowQuarantineHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-workflow-quarantine-" + Guid.NewGuid().ToString("N"));
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
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "lastQuarantine", quarantineRoot, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                Invoke(mainFormType, form, "InitializeRestoreManifest", flags);

                object[] quarantineArgs = new object[] { savePath, null };
                Invoke(mainFormType, form, "QuarantineWorkflowSaveTransactional", flags, quarantineArgs);
                string quarantinedPath = Convert.ToString(quarantineArgs[1]) ?? "";

                Assert(!File.Exists(savePath), "workflow save should be removed from original location after quarantine");
                Assert(File.Exists(quarantinedPath), "workflow save should exist in quarantine destination");

                string manifestPath = Path.Combine(quarantineRoot, "restore_manifest.tsv");
                Assert(File.Exists(manifestPath), "restore manifest should exist after workflow quarantine");
                string manifestText = File.ReadAllText(manifestPath);
                Assert(manifestText.IndexOf("\tmoved_file\t", StringComparison.OrdinalIgnoreCase) >= 0, "restore manifest should record a moved_file entry");
                Assert(manifestText.IndexOf("\tcommitted\t", StringComparison.OrdinalIgnoreCase) >= 0, "restore manifest should commit the workflow quarantine entry");

                object entries = Invoke(mainFormType, form, "ReadRestoreEntries", flags);
                object committedEntry = FindCommittedMovedFileEntry(entries, quarantinedPath);
                if (committedEntry == null)
                {
                    Console.Error.WriteLine("--- MANIFEST ---");
                    Console.Error.WriteLine(manifestText);
                    Console.Error.WriteLine("--- ENTRIES ---");
                    DumpEntries(entries);
                }
                Assert(committedEntry != null, "committed moved_file restore entry should be discoverable");

                File.WriteAllText(savePath, "conflicting-save");
                bool conflictBlocked = false;
                try
                {
                    Invoke(mainFormType, form, "RestoreFileEntry", flags, committedEntry);
                }
                catch (TargetInvocationException ex)
                {
                    IOException io = ex.InnerException as IOException;
                    if (io != null && io.Message.IndexOf("will not overwrite", StringComparison.OrdinalIgnoreCase) >= 0)
                        conflictBlocked = true;
                    else
                        throw;
                }

                Assert(conflictBlocked, "restore should block when a workflow save reappears at the original path");
                Assert(File.ReadAllText(savePath) == "conflicting-save" + Environment.NewLine || File.ReadAllText(savePath) == "conflicting-save", "conflicting save should remain untouched after blocked restore");
                File.Delete(savePath);

                Invoke(mainFormType, form, "RestoreFileEntry", flags, committedEntry);

                Assert(File.Exists(savePath), "restore should return workflow save to original path");
                Assert(File.Exists(quarantinedPath), "quarantine backup should remain available after restore");
                Assert(File.ReadAllText(savePath) == "save-content" + Environment.NewLine || File.ReadAllText(savePath) == "save-content", "restored workflow save should preserve original content");

                Environment.SetEnvironmentVariable("CK3MPS_TEST_WORKFLOW_FAULT", "prepared");
                AssertInjectedWorkflowFault(mainFormType, form, flags, savePath, "prepared");
                Assert(File.Exists(savePath), "prepared-stage fault must leave the original workflow save in place");

                Environment.SetEnvironmentVariable("CK3MPS_TEST_WORKFLOW_FAULT", "move");
                AssertInjectedWorkflowFault(mainFormType, form, flags, savePath, "move");
                Assert(File.Exists(savePath), "move-stage fault must leave the original workflow save in place");

                Environment.SetEnvironmentVariable("CK3MPS_TEST_WORKFLOW_FAULT", "commit");
                AssertInjectedWorkflowFault(mainFormType, form, flags, savePath, "commit");
                Assert(File.Exists(savePath), "commit-stage fault must roll the workflow save back to the original path");
                Assert(File.ReadAllText(savePath) == "save-content" + Environment.NewLine || File.ReadAllText(savePath) == "save-content", "commit-stage rollback must preserve original workflow save content");
                return 0;
            }
            finally
            {
                Environment.SetEnvironmentVariable("CK3MPS_TEST_WORKFLOW_FAULT", null);
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

    private static void AssertInjectedWorkflowFault(Type type, object instance, BindingFlags flags, string savePath, string stage)
    {
        object[] args = new object[] { savePath, null };
        bool faulted = false;
        try
        {
            Invoke(type, instance, "QuarantineWorkflowSaveTransactional", flags, args);
        }
        catch (TargetInvocationException ex)
        {
            IOException io = ex.InnerException as IOException;
            if (io != null && io.Message.IndexOf("Injected workflow fault", StringComparison.OrdinalIgnoreCase) >= 0)
                faulted = true;
            else
                throw;
        }

        Assert(faulted, "workflow fault injection should trigger at stage: " + stage);
    }

    private static object FindCommittedMovedFileEntry(object entriesObject, string backupPath)
    {
        System.Collections.IEnumerable entries = entriesObject as System.Collections.IEnumerable;
        if (entries == null)
            return null;

        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            string kind = Convert.ToString(entryType.GetField("Kind").GetValue(entry)) ?? "";
            string status = Convert.ToString(entryType.GetField("Status").GetValue(entry)) ?? "";
            string backup = Convert.ToString(entryType.GetField("BackupPath").GetValue(entry)) ?? "";
            if (String.Equals(kind, "moved_file", StringComparison.OrdinalIgnoreCase)
                && String.Equals(status, "committed", StringComparison.OrdinalIgnoreCase)
                && String.Equals(backup, backupPath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private static void DumpEntries(object entriesObject)
    {
        System.Collections.IEnumerable entries = entriesObject as System.Collections.IEnumerable;
        if (entries == null)
            return;

        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            Console.Error.WriteLine(
                "Kind=" + Convert.ToString(entryType.GetField("Kind").GetValue(entry))
                + " | Status=" + Convert.ToString(entryType.GetField("Status").GetValue(entry))
                + " | Backup=" + Convert.ToString(entryType.GetField("BackupPath").GetValue(entry))
                + " | Source=" + Convert.ToString(entryType.GetField("SourcePath").GetValue(entry))
                + " | Validation=" + Convert.ToString(entryType.GetField("ValidationError").GetValue(entry)));
        }
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
