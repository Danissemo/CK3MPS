using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class ReadOnlyScanHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-readonly-scan-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string installRoot = Path.Combine(tempRoot, "Game");
        string binariesRoot = Path.Combine(installRoot, "binaries");
        string stateRoot = Path.Combine(tempRoot, "CK3MPS_State");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(docsRoot);
            Directory.CreateDirectory(Path.Combine(docsRoot, "save games"));
            Directory.CreateDirectory(Path.Combine(docsRoot, "logs"));
            Directory.CreateDirectory(binariesRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);
            File.WriteAllText(Path.Combine(docsRoot, "pdx_settings.txt"), "graphics={}\r\n");
            File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
            File.WriteAllText(Path.Combine(binariesRoot, "ck3.exe"), "");

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);
            try
            {
                ((Form)form).CreateControl();
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "ck3Install", installRoot, flags);
                SetField(mainFormType, form, "ck3Bin", binariesRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "lastQuarantine", "", flags);
                SetField(mainFormType, form, "liveLogFilePath", "", flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                MethodInfo runCheckOnlyAsync = mainFormType.GetMethod("RunCheckOnlyAsync", flags);
                if (runCheckOnlyAsync == null)
                    throw new InvalidOperationException("RunCheckOnlyAsync was not found.");

                System.Threading.Tasks.Task scanTask = (System.Threading.Tasks.Task)runCheckOnlyAsync.Invoke(form, null);
                Label status = (Label)mainFormType.GetField("statusLabel", flags).GetValue(form);
                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                while (DateTime.UtcNow < deadline && !scanTask.IsCompleted)
                {
                    Application.DoEvents();
                    Thread.Sleep(25);
                }
                if (!scanTask.IsCompleted)
                {
                    MethodInfo snapshotRunLogLines = mainFormType.GetMethod("SnapshotRunLogLines", flags);
                    string[] lines = snapshotRunLogLines == null
                        ? new string[0]
                        : (string[])snapshotRunLogLines.Invoke(form, null);
                    Console.Error.WriteLine("Read-only Scan timeout. Last status: " + status.Text);
                    for (int i = Math.Max(0, lines.Length - 25); i < lines.Length; i++)
                        Console.Error.WriteLine("SCAN " + lines[i]);
                }
                Assert(scanTask.IsCompleted, "full read-only Scan did not complete before timeout; last status: " + status.Text);
                scanTask.GetAwaiter().GetResult();
                Assert(status.Text.IndexOf("Scan complete", StringComparison.OrdinalIgnoreCase) >= 0, "full read-only Scan did not complete before timeout");
                string reportText = Convert.ToString(mainFormType.GetField("lastCheckOnlyReportText", flags).GetValue(form)) ?? "";
                Assert(reportText.IndexOf("CK3MPS compact check", StringComparison.Ordinal) >= 0, "full read-only Scan should retain an in-memory export report");
                string[] mutationAttempts = (string[])mainFormType.GetField("lastReadOnlyMutationAttempts", flags).GetValue(form);
                Assert(mutationAttempts != null && mutationAttempts.Length == 0, "recording mutation abstraction should observe zero file, registry, or command mutations during Scan");

                Assert(!Directory.Exists(stateRoot), "read-only scan must not create state root");
                Assert(!File.Exists(Path.Combine(stateRoot, "history.txt")), "read-only scan must not create history");
                Assert(!File.Exists(Path.Combine(stateRoot, "check_only_report.txt")), "read-only scan must not create check-only report");
                Assert(!Directory.Exists(Path.Combine(stateRoot, "LiveLogs")), "read-only scan must not create live logs");
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
            DumpStateRoot(stateRoot);
            Console.Error.WriteLine(ex.InnerException ?? ex);
            return 1;
        }
        catch (Exception ex)
        {
            DumpStateRoot(stateRoot);
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

    private static void DumpStateRoot(string stateRoot)
    {
        try
        {
            Console.Error.WriteLine("StateRoot=" + stateRoot);
            if (!Directory.Exists(stateRoot))
            {
                Console.Error.WriteLine("StateRootMissing");
                return;
            }

            foreach (string path in Directory.GetFileSystemEntries(stateRoot, "*", SearchOption.AllDirectories))
                Console.Error.WriteLine("StateItem=" + path);
        }
        catch
        {
        }
    }
}
