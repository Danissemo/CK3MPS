using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class OosWatcherHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-oos-watcher-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string oosRoot = Path.Combine(docsRoot, "oos", "sample");
        string logsRoot = Path.Combine(docsRoot, "logs");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(oosRoot);
            Directory.CreateDirectory(logsRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);

            string fixtureRoot = Path.Combine(Environment.CurrentDirectory, "tests", "fixtures", "oos_smoke");
            File.Copy(Path.Combine(fixtureRoot, "oos_metadata_1.txt"), Path.Combine(oosRoot, "oos_metadata_1.txt"), true);
            File.Copy(Path.Combine(fixtureRoot, "savegame_oos_machineid_1.oos"), Path.Combine(oosRoot, "savegame_oos_machineid_1.oos"), true);
            File.Copy(Path.Combine(fixtureRoot, "modifiers_oos_machineid_1.oos"), Path.Combine(oosRoot, "modifiers_oos_machineid_1.oos"), true);
            File.Copy(Path.Combine(fixtureRoot, "error_1.log"), Path.Combine(oosRoot, "error_1.log"), true);
            File.Copy(Path.Combine(fixtureRoot, "error_1.log"), Path.Combine(logsRoot, "error.log"), true);

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", tempRoot, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                MethodInfo schedule = RequireMethod(mainFormType, "ScheduleOosWatcherScan", flags);
                MethodInfo stop = RequireMethod(mainFormType, "StopOosWatcherServices", flags);

                for (int i = 0; i < 100; i++)
                    schedule.Invoke(form, new object[] { "storm" });

                WaitForWatcherProcessCount(mainFormType, form, flags, 1, 15000);
                System.Threading.Thread.Sleep(500);
                int processCount = (int)GetField(mainFormType, form, "oosWatcherProcessCount", flags);
                Assert(processCount == 1, "identical watcher events should coalesce into one background analysis");
                stop.Invoke(form, new object[] { 1000 });

                Environment.SetEnvironmentVariable("CK3MPS_TEST_OOS_WATCHER_DELAY_MS", "5000");
                try
                {
                    schedule.Invoke(form, new object[] { "cancel-test" });
                    System.Threading.Thread.Sleep(200);
                    Stopwatch sw = Stopwatch.StartNew();
                    stop.Invoke(form, new object[] { 500 });
                    sw.Stop();
                    Assert(sw.ElapsedMilliseconds < 2000, "watcher shutdown should stay bounded while analysis is active");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("CK3MPS_TEST_OOS_WATCHER_DELAY_MS", null);
                }

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

    private static void WaitForWatcherProcessCount(Type type, object instance, BindingFlags flags, int expectedCount, int timeoutMs)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            int currentCount = (int)GetField(type, instance, "oosWatcherProcessCount", flags);
            if (currentCount >= expectedCount)
                return;

            System.Threading.Thread.Sleep(50);
        }

        throw new InvalidOperationException("OOS watcher did not reach the expected process count within the timeout.");
    }

    private static MethodInfo RequireMethod(Type type, string methodName, BindingFlags flags)
    {
        MethodInfo method = type.GetMethod(methodName, flags);
        if (method == null)
            throw new InvalidOperationException("Method not found: " + methodName);
        return method;
    }

    private static object GetField(Type type, object instance, string fieldName, BindingFlags flags)
    {
        FieldInfo field = type.GetField(fieldName, flags);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        return field.GetValue(instance);
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
