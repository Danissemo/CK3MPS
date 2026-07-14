using System;
using System.IO;
using CK3MPS;

internal static class UtilityTests
{
    private static int failures;

    private static void Main()
    {
        TestVersionComparison();
        TestPathNormalizationAndValidation();

        if (failures > 0)
            Environment.Exit(1);
    }

    private static void TestVersionComparison()
    {
        Assert(VersionUtilities.IsNewerRelease("v0.2-beta", "v0.1-beta"), "v0.2 should be newer than v0.1");
        Assert(VersionUtilities.IsNewerRelease("v1.0.1", "v1.0.0"), "patch version should compare");
        Assert(!VersionUtilities.IsNewerRelease("v0.1-beta", "v0.1-beta"), "same version should not be newer");
        Assert(!VersionUtilities.IsNewerRelease("v0.1-beta", "v0.2-beta"), "older version should not be newer");
    }

    private static void TestPathNormalizationAndValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "CK3MPS-tests-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "Crusader Kings III");
        string binaries = Path.Combine(game, "binaries");
        string settings = Path.Combine(root, "Documents", "Paradox Interactive", "Crusader Kings III");
        string saves = Path.Combine(settings, "save games");

        try
        {
            Directory.CreateDirectory(binaries);
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(binaries, "ck3.exe"), "");
            File.WriteAllText(Path.Combine(settings, "pdx_settings.txt"), "");

            Assert(Ck3PathUtilities.NormalizeGameFolderSelection(binaries) == game, "binaries folder should normalize to game root");
            Assert(Ck3PathUtilities.NormalizeSettingsFolderSelection(saves) == settings, "save games folder should normalize to settings root");
            Assert(Ck3PathUtilities.IsValidGameFolder(game), "game folder should require binaries ck3.exe");
            Assert(Ck3PathUtilities.IsValidSettingsFolder(settings), "settings folder should accept CK3 markers");
            Assert(!Ck3PathUtilities.IsValidGameFolder(settings), "settings folder must not validate as game folder");
            Assert(!Ck3PathUtilities.IsValidSettingsFolder(game), "game folder must not validate as settings folder");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + message);
            return;
        }

        Console.WriteLine("FAIL " + message);
        failures++;
    }
}
