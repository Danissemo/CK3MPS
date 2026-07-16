using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

internal static class SettingsGuardHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-settings-guard-" + Guid.NewGuid().ToString("N"));
        string docsRoot = Path.Combine(tempRoot, "Crusader Kings III");
        string saveRoot = Path.Combine(docsRoot, "save games");
        string stateRoot = Path.Combine(tempRoot, "CK3MPS_State");
        string steamUserDir = Path.Combine(tempRoot, "Steam", "userdata", "123456", "config");
        string localConfig = Path.Combine(steamUserDir, "localconfig.vdf");
        string sharedConfig = Path.Combine(steamUserDir, "sharedconfig.vdf");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");
        string suspiciousSave = Path.Combine(saveRoot, "campaign_desync.ck3");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(saveRoot);
            Directory.CreateDirectory(Path.Combine(docsRoot, "logs"));
            Directory.CreateDirectory(steamUserDir);
            File.Copy(assemblyPath, assemblyCopyPath, true);

            File.WriteAllText(Path.Combine(docsRoot, "pdx_settings.txt"), BuildStablePdxSettings());
            File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
            File.WriteAllText(localConfig, BuildStableLocalConfig());
            File.WriteAllText(sharedConfig, BuildStableSharedConfig());

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "ck3Docs", docsRoot, flags);
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "localConfig", localConfig, flags);
                SetField(mainFormType, form, "sharedConfig", sharedConfig, flags);
                SetField(mainFormType, form, "lastQuarantine", "", flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);
                SetField(mainFormType, form, "settingsGuardAutoRepairEnabled", false, flags);

                Invoke(mainFormType, form, "PrepareSettingsGuardArtifacts", flags, "guard started");

                string pdxSettingsPath = Path.Combine(docsRoot, "pdx_settings.txt");
                string expectedProfilePath = Path.Combine(stateRoot, "state.txt");
                string reportPath = Path.Combine(stateRoot, "settings_guard.txt");
                Assert(File.Exists(expectedProfilePath), "expected profile snapshot should exist after guard setup");
                Assert(File.Exists(reportPath), "settings guard report should exist after guard setup");

                string snapshotBefore = File.ReadAllText(expectedProfilePath);
                File.WriteAllText(pdxSettingsPath, "# drifted settings\r\n");
                File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[\"mod/a\"],\"disabled_dlcs\":[]}");
                File.WriteAllText(localConfig, BuildDriftedLocalConfig());
                File.WriteAllText(suspiciousSave, "drifted-save");

                SetField(mainFormType, form, "settingsGuardActive", true, flags);
                SetField(mainFormType, form, "lastSettingsGuardRepairUtc", DateTime.MinValue, flags);
                Invoke(mainFormType, form, "RunSettingsGuardTick", flags);

                Assert(File.ReadAllText(pdxSettingsPath) == "# drifted settings\r\n", "detect-only guard must not rewrite pdx_settings.txt");
                Assert(File.ReadAllText(Path.Combine(docsRoot, "dlc_load.json")) == "{\"enabled_mods\":[\"mod/a\"],\"disabled_dlcs\":[]}", "detect-only guard must not rewrite dlc_load.json");
                Assert(File.ReadAllText(localConfig) == BuildDriftedLocalConfig(), "detect-only guard must not rewrite Steam localconfig");
                Assert(File.Exists(suspiciousSave), "detect-only guard must not quarantine suspicious saves automatically");
                Assert(File.ReadAllText(expectedProfilePath) == snapshotBefore, "detect-only guard must not rewrite expected profile snapshot");

                string reportText = File.ReadAllText(reportPath);
                Assert(reportText.IndexOf("Reason: drift detected (detect-only)", StringComparison.OrdinalIgnoreCase) >= 0, "guard report should record detect-only drift reason");
                Assert(reportText.IndexOf("Mode: detect-only", StringComparison.OrdinalIgnoreCase) >= 0, "guard report should record detect-only mode");

                File.WriteAllText(pdxSettingsPath, BuildStablePdxSettings());
                File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
                File.WriteAllText(localConfig, BuildDriftedLocalConfig());
                SetField(mainFormType, form, "settingsGuardAutoRepairEnabled", true, flags);
                SetField(mainFormType, form, "lastSettingsGuardRepairUtc", DateTime.MinValue, flags);
                Invoke(mainFormType, form, "RunSettingsGuardTick", flags);

                string repairedLocalConfig = File.ReadAllText(localConfig);
                Assert(repairedLocalConfig.IndexOf("-noasync", StringComparison.OrdinalIgnoreCase) >= 0, "auto-repair guard should restore -noasync launch option");
                Assert(repairedLocalConfig.IndexOf("debug_mode", StringComparison.OrdinalIgnoreCase) < 0, "auto-repair guard should remove debug_mode launch option");
                Assert(File.Exists(suspiciousSave), "auto-repair guard must still leave suspicious saves for manual action");

                string fakeSteamPath = Path.Combine(tempRoot, "steam.exe");
                Process fakeSteam = null;
                try
                {
                    File.WriteAllText(Path.Combine(docsRoot, "dlc_load.json"), "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
                    File.WriteAllText(pdxSettingsPath, BuildStablePdxSettings());
                    File.Delete(suspiciousSave);
                    File.WriteAllText(localConfig, BuildDriftedLocalConfig());
                    fakeSteam = StartFakeSteamProcess(fakeSteamPath);
                    SetField(mainFormType, form, "lastSettingsGuardRepairUtc", DateTime.MinValue, flags);
                    Invoke(mainFormType, form, "RunSettingsGuardTick", flags);
                    Assert(File.ReadAllText(localConfig) == BuildDriftedLocalConfig(), "auto-repair guard must not rewrite Steam config while steam.exe is running");
                    reportText = File.ReadAllText(reportPath);
                    Assert(reportText.IndexOf("no repair was allowed", StringComparison.OrdinalIgnoreCase) >= 0, "guard report should record blocked repair when Steam is running");
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

    private static string BuildStablePdxSettings()
    {
        return "\"game\"={\r\n" +
            "\t\"autosave\"={ version=0 value=\"NO_AUTOSAVE\" }\r\n" +
            "\t\"debug_saves\"={ version=0 value=3 }\r\n" +
            "\t\"save_on_exit\"={ version=0 enabled=no }\r\n" +
            "\t\"cloud_save\"={ version=0 enabled=no }\r\n" +
            "\t\"rich_presence\"={ version=0 enabled=no }\r\n" +
            "\t\"file_transfer_speed\"={ version=0 value=\"OPTION_HIGH\" }\r\n" +
            "}\r\n" +
            "\"Graphics\"={\r\n" +
            "\t\"renderer\"={ version=0 value=\"Vulkan\" }\r\n" +
            "\t\"display_mode\"={ version=0 value=\"fullscreen\" }\r\n" +
            "\t\"vsync\"={ version=0 enabled=yes }\r\n" +
            "\t\"adaptive_framerate\"={ version=0 enabled=no }\r\n" +
            "\t\"setting_framerate_cap\"={ version=0 value=\"60\" }\r\n" +
            "\t\"quality\"={ version=0 value=\"low\" }\r\n" +
            "\t\"texture_quality\"={ version=1 value=\"low\" }\r\n" +
            "\t\"shadowmap_resolution\"={ version=2 value=\"disabled\" }\r\n" +
            "\t\"refraction_quality\"={ version=1 value=\"disabled\" }\r\n" +
            "\t\"mesh_lod_bias\"={ version=1 value=\"low\" }\r\n" +
            "\t\"mapobject_quality\"={ version=0 value=\"off\" }\r\n" +
            "\t\"anti_aliasing\"={ version=0 value=\"DISABLED\" }\r\n" +
            "\t\"anisotropic_filtering\"={ version=0 value=\"x4\" }\r\n" +
            "\t\"portrait_multi_sampling\"={ version=0 value=\"x2\" }\r\n" +
            "\t\"terrain_smoothing\"={ version=0 enabled=no }\r\n" +
            "\t\"bloom_enabled\"={ version=0 enabled=no }\r\n" +
            "\t\"ssao\"={ version=0 enabled=no }\r\n" +
            "\t\"depthoffield\"={ version=0 enabled=no }\r\n" +
            "\t\"lensflare\"={ version=0 enabled=no }\r\n" +
            "\t\"secondary_lensflare\"={ version=0 enabled=no }\r\n" +
            "\t\"mesh_lod_fade\"={ version=0 enabled=no }\r\n" +
            "\t\"animated_portraits\"={ version=0 enabled=no }\r\n" +
            "\t\"portraits_ssao\"={ version=0 enabled=no }\r\n" +
            "\t\"portraits_bloom\"={ version=0 enabled=no }\r\n" +
            "\t\"advanced_shaders\"={ version=0 enabled=no }\r\n" +
            "\t\"winter_particle_effects\"={ version=0 enabled=no }\r\n" +
            "\t\"cloud_shadow_enabled\"={ version=0 enabled=no }\r\n" +
            "\t\"tree_dithering_enabled\"={ version=0 enabled=no }\r\n" +
            "\t\"court_scene_low_priority_characters\"={ version=0 enabled=no }\r\n" +
            "\t\"royal_court_anim_camera_idle\"={ version=0 enabled=no }\r\n" +
            "\t\"royal_court_anim_camera_transition\"={ version=0 enabled=no }\r\n" +
            "}\r\n" +
            "\"System\"={\r\n" +
            "\t\"language\"={ version=0 value=\"l_english\" }\r\n" +
            "}\r\n" +
            "\"Audio\"={\r\n" +
            "\t\"audio_debug_log_level\"={ version=0 value=\"error\" }\r\n" +
            "}\r\n";
    }

    private static string BuildStableLocalConfig()
    {
        return "\"UserLocalConfigStore\"\r\n{\r\n" +
            "\t\"Software\"\r\n\t{\r\n" +
            "\t\t\"Valve\"\r\n\t\t{\r\n" +
            "\t\t\t\"Steam\"\r\n\t\t\t{\r\n" +
            "\t\t\t\t\"apps\"\r\n\t\t\t\t{\r\n" +
            "\t\t\t\t\t\"1158310\"\r\n\t\t\t\t\t{\r\n" +
            "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-noasync\"\r\n" +
            "\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}\r\n";
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

    private static string BuildStableSharedConfig()
    {
        return "\"UserRoamingConfigStore\"\r\n{\r\n" +
            "\t\"Software\"\r\n\t{\r\n" +
            "\t\t\"Valve\"\r\n\t\t{\r\n" +
            "\t\t\t\"Steam\"\r\n\t\t\t{\r\n" +
            "\t\t\t\t\"apps\"\r\n\t\t\t\t{\r\n" +
            "\t\t\t\t\t\"1158310\"\r\n\t\t\t\t\t{\r\n" +
            "\t\t\t\t\t\t\"cloudenabled\"\t\t\"0\"\r\n" +
            "\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}\r\n";
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
}
