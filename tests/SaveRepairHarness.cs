using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

internal static class SaveRepairHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-save-repair-" + Guid.NewGuid().ToString("N"));
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(tempRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                RunPlainTextRepairScenario(mainFormType, form, flags, tempRoot);
                RunDangerousZipScenario(mainFormType, form, flags, tempRoot);
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

    private static void RunPlainTextRepairScenario(Type mainFormType, object form, BindingFlags flags, string tempRoot)
    {
        string savePath = Path.Combine(tempRoot, "plain_host.ck3");
        File.WriteAllText(savePath,
            "meta_data={}\r\n"
            + "game_rules={\r\n"
            + "\tsettings={ default_multiplayer_murder_schemes ai_laamp_numbers_200 situation_the_great_steppe_toggle_on natural_disaster_earthquakes_regular natural_disaster_floods_regular }\r\n"
            + "}\r\n",
            new UTF8Encoding(false));

        string repairedPath;
        string failureReason;
        bool success = InvokeRepair(mainFormType, form, flags, savePath, out repairedPath, out failureReason);
        Assert(success, "plain-text save should produce a repaired safe copy");
        Assert(String.IsNullOrEmpty(failureReason), "plain-text repair should not report a failure reason");
        Assert(File.Exists(repairedPath), "plain-text repair should create the repaired file");

        string repairedText = File.ReadAllText(repairedPath);
        Assert(repairedText.IndexOf("no_players_multiplayer_murder_schemes", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair should normalize murder schemes");
        Assert(repairedText.IndexOf("ai_laamp_numbers_25", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair should normalize landless adventurers");
        Assert(repairedText.IndexOf("situation_the_great_steppe_toggle_off", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair should normalize great steppe");
        Assert(repairedText.IndexOf("natural_disaster_earthquakes_disabled", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair should normalize earthquakes");
        Assert(repairedText.IndexOf("natural_disaster_floods_disabled", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair should normalize floods");
        Assert(File.ReadAllText(savePath).IndexOf("ai_laamp_numbers_200", StringComparison.OrdinalIgnoreCase) >= 0, "plain-text repair must not mutate the original save");
    }

    private static void RunDangerousZipScenario(Type mainFormType, object form, BindingFlags flags, string tempRoot)
    {
        string savePath = Path.Combine(tempRoot, "dangerous_zip.ck3");
        string existingRepairedPath = Path.Combine(tempRoot, "dangerous_zip_ck3mps_safe.ck3");
        File.WriteAllText(existingRepairedPath, "existing-safe-copy", new UTF8Encoding(false));

        using (FileStream stream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            WriteZipEntry(archive, "meta", "meta_data={}\r\n");
            WriteZipEntry(archive, "../evil", "bad");
            WriteZipEntry(archive, "gamestate", "game_rules={ settings={ default_multiplayer_murder_schemes } }");
        }

        string repairedPath;
        string failureReason;
        bool success = InvokeRepair(mainFormType, form, flags, savePath, out repairedPath, out failureReason);
        Assert(!success, "dangerous zip entry should block repair");
        Assert(repairedPath == existingRepairedPath, "repair path should still point to the target safe-copy file");
        Assert(failureReason.IndexOf("dangerous path entry", StringComparison.OrdinalIgnoreCase) >= 0, "dangerous zip entry failure should be explicit");
        Assert(File.ReadAllText(existingRepairedPath) == "existing-safe-copy", "failed repair must not overwrite an existing repaired save");
    }

    private static bool InvokeRepair(Type mainFormType, object form, BindingFlags flags, string savePath, out string repairedPath, out string failureReason)
    {
        MethodInfo method = mainFormType.GetMethod("TryCreateSafeHostSaveCopy", flags);
        if (method == null)
            throw new InvalidOperationException("TryCreateSafeHostSaveCopy was not found.");

        object[] parameters = new object[] { savePath, null, null, null };
        bool success = (bool)method.Invoke(form, parameters);
        repairedPath = Convert.ToString(parameters[1]) ?? "";
        failureReason = Convert.ToString(parameters[3]) ?? "";
        return success;
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            writer.Write(text);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
