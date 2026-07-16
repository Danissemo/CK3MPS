using System;
using System.IO;
using System.Reflection;

internal static class WorkflowDuplicateHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-workflow-duplicate-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string saveRoot = Path.Combine(docsRoot, "save games");
        string sourceSave = Path.Combine(saveRoot, "campaign.ck3");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(saveRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);
            File.WriteAllText(Path.Combine(docsRoot, "pdx_settings.txt"), "graphics={}\r\n");
            File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
            File.WriteAllText(sourceSave, "save-content");

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "workflowSelectedSavePath", sourceSave, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                MethodInfo duplicateCore = mainFormType.GetMethod("DuplicateWorkflowSaveCore", flags);
                if (duplicateCore == null)
                    throw new InvalidOperationException("DuplicateWorkflowSaveCore was not found.");

                string explicitCopy = Path.Combine(saveRoot, "campaign_copy.ck3");
                string createdPath = Convert.ToString(duplicateCore.Invoke(form, new object[] { sourceSave, explicitCopy })) ?? "";
                Assert(String.Equals(createdPath, explicitCopy, StringComparison.OrdinalIgnoreCase), "workflow duplicate core should return the created copy path");
                Assert(File.Exists(createdPath), "workflow duplicate should create the target copy");
                Assert(File.ReadAllText(createdPath) == "save-content", "workflow duplicate should preserve save contents");

                string blockedCopy = Path.Combine(saveRoot, "blocked_copy.ck3");
                string foreignTemp = blockedCopy + ".tmp";
                File.WriteAllText(foreignTemp, "foreign-temp");

                bool failed = false;
                try
                {
                    duplicateCore.Invoke(form, new object[] { sourceSave, blockedCopy });
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException is IOException)
                        failed = true;
                    else
                        throw;
                }

                Assert(failed, "workflow duplicate should fail when the temp copy path is already occupied");
                Assert(File.Exists(foreignTemp), "workflow duplicate cleanup should not delete a preexisting foreign temp file");
                Assert(File.ReadAllText(foreignTemp) == "foreign-temp", "workflow duplicate should leave foreign temp contents untouched");
                Assert(!File.Exists(blockedCopy), "workflow duplicate should not create the final copy when temp-path collision blocks the operation");
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
