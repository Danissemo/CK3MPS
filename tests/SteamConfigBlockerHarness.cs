using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class SteamConfigBlockerHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-steam-blocker-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string steamUserDir = Path.Combine(tempRoot, "Steam", "userdata", "123456", "config");
        string localConfig = Path.Combine(steamUserDir, "localconfig.vdf");
        string sharedConfig = Path.Combine(steamUserDir, "sharedconfig.vdf");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(Path.Combine(docsRoot, "save games"));
            Directory.CreateDirectory(Path.Combine(docsRoot, "logs"));
            Directory.CreateDirectory(steamUserDir);
            File.Copy(assemblyPath, assemblyCopyPath, true);
            File.WriteAllText(localConfig, BuildDriftedLocalConfig());
            File.WriteAllText(sharedConfig, BuildDriftedSharedConfig());

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "localConfig", localConfig, flags);
                SetField(mainFormType, form, "sharedConfig", sharedConfig, flags);
                SetField(mainFormType, form, "lastQuarantine", "", flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                string fakeSteamPath = Path.Combine(tempRoot, "steam.exe");
                Process fakeSteam = null;
                try
                {
                    fakeSteam = StartFakeSteamProcess(fakeSteamPath);
                    Invoke(mainFormType, form, "StabilizeSteamSettings", flags);

                    bool launchOverrideBlocked = false;
                    bool cloudOverrideBlocked = false;
                    try
                    {
                        Invoke(mainFormType, form, "RemoveSteamLaunchOptionsOverride", flags);
                    }
                    catch (TargetInvocationException ex)
                    {
                        if (ex.InnerException is InvalidOperationException)
                            launchOverrideBlocked = true;
                        else
                            throw;
                    }

                    try
                    {
                        Invoke(mainFormType, form, "RemoveSteamCloudOverride", flags);
                    }
                    catch (TargetInvocationException ex)
                    {
                        if (ex.InnerException is InvalidOperationException)
                            cloudOverrideBlocked = true;
                        else
                            throw;
                    }

                    Assert(File.ReadAllText(localConfig) == BuildDriftedLocalConfig(), "Steam running blocker must prevent localconfig mutation");
                    Assert(File.ReadAllText(sharedConfig) == BuildDriftedSharedConfig(), "Steam running blocker must prevent sharedconfig mutation");
                    Assert(launchOverrideBlocked, "Steam running blocker must reject default restore of launch options override");
                    Assert(cloudOverrideBlocked, "Steam running blocker must reject default restore of Steam Cloud override");
                }
                finally
                {
                    if (fakeSteam != null)
                    {
                        try
                        {
                            if (!fakeSteam.HasExited)
                                fakeSteam.Kill();
                        }
                        catch
                        {
                        }
                        fakeSteam.Dispose();
                    }
                }

                Thread.Sleep(500);
                Invoke(mainFormType, form, "EnsureNoAsync", flags);
                Invoke(mainFormType, form, "RemoveDebugModeLaunchOption", flags);
                Invoke(mainFormType, form, "DisableSteamCloudFlag", flags);

                string repairedLocal = File.ReadAllText(localConfig);
                string repairedShared = File.ReadAllText(sharedConfig);
                Assert(repairedLocal.IndexOf("-noasync", StringComparison.OrdinalIgnoreCase) >= 0, "Steam repair routines should restore -noasync");
                Assert(repairedLocal.IndexOf("debug_mode", StringComparison.OrdinalIgnoreCase) < 0, "Steam repair routines should remove debug_mode");
                Assert(repairedShared.IndexOf("\"cloudenabled\"\t\t\"0\"", StringComparison.OrdinalIgnoreCase) >= 0, "Steam repair routines should disable Steam Cloud");

                if (!IsAnySteamProcessRunning())
                {
                    File.WriteAllText(localConfig, BuildDriftedLocalConfig());
                    File.WriteAllText(sharedConfig, BuildDriftedSharedConfig());
                    bool launchOverrideRemoved = Convert.ToBoolean(Invoke(mainFormType, form, "RemoveSteamLaunchOptionsOverride", flags));
                    bool cloudOverrideRemoved = Convert.ToBoolean(Invoke(mainFormType, form, "RemoveSteamCloudOverride", flags));
                    Assert(launchOverrideRemoved, "default restore should remove Steam launch options override after Steam exits");
                    Assert(cloudOverrideRemoved, "default restore should remove Steam Cloud override after Steam exits");
                    Assert(File.ReadAllText(localConfig).IndexOf("\"LaunchOptions\"", StringComparison.OrdinalIgnoreCase) < 0, "default restore should remove LaunchOptions entry");
                    Assert(File.ReadAllText(sharedConfig).IndexOf("\"cloudenabled\"", StringComparison.OrdinalIgnoreCase) < 0, "default restore should remove cloudenabled entry");
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

    private static Process StartFakeSteamProcess(string fakeSteamPath)
    {
        string cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        File.Copy(cmdPath, fakeSteamPath, true);

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = fakeSteamPath;
        psi.Arguments = "/c ping -n 8 127.0.0.1 >nul";
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        Process process = Process.Start(psi);
        Thread.Sleep(500);
        return process;
    }

    private static bool IsAnySteamProcessRunning()
    {
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    private static string BuildDriftedLocalConfig()
    {
        return "\"UserLocalConfigStore\"\r\n{\r\n" +
            "\t\"Software\"\r\n\t{\r\n" +
            "\t\t\"Valve\"\r\n\t\t{\r\n" +
            "\t\t\t\"Steam\"\r\n\t\t\t{\r\n" +
            "\t\t\t\t\"apps\"\r\n\t\t\t\t{\r\n" +
            "\t\t\t\t\t\"1158310\"\r\n\t\t\t\t\t{\r\n" +
            "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"debug_mode\"\r\n" +
            "\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}\r\n";
    }

    private static string BuildDriftedSharedConfig()
    {
        return "\"UserRoamingConfigStore\"\r\n{\r\n" +
            "\t\"Software\"\r\n\t{\r\n" +
            "\t\t\"Valve\"\r\n\t\t{\r\n" +
            "\t\t\t\"Steam\"\r\n\t\t\t{\r\n" +
            "\t\t\t\t\"apps\"\r\n\t\t\t\t{\r\n" +
            "\t\t\t\t\t\"1158310\"\r\n\t\t\t\t\t{\r\n" +
            "\t\t\t\t\t\t\"cloudenabled\"\t\t\"1\"\r\n" +
            "\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}\r\n";
    }
}
