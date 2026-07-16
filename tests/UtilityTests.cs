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
        TestRecommendedPresetSafety();
        TestRestoreManifestRules();
        TestCompactReportNames();
        TestChecksumExtraction();
        TestRegistryValueSerialization();
        TestRuntimeModeUtilities();
        TestSaveRuleUtilities();
        TestPathContainmentUtilities();
        TestIncidentHistoryJsonUtilities();

        if (failures > 0)
            Environment.Exit(1);
    }

    private static void TestRestoreManifestRules()
    {
        Assert(RestoreManifestUtilities.EscapeTsv("a\tb\r\nc") == "a b  c", "restore manifest escapes tabs and new lines");
        Assert(RestoreManifestUtilities.InferRunIdFromCreated("2026-07-14 15:04:05") == "20260714_150405", "restore manifest infers run id from legacy timestamp");
        Assert(RestoreManifestUtilities.RunIdFromManifestParts(new[] { "1", "2026-07-14 15:04:05", "file", "a", "b", "c", "d", "e", "active" }, "2026-07-14 15:04:05") == "20260714_150405", "legacy restore manifest without run_id gets inferred run id");
        Assert(RestoreManifestUtilities.RunIdFromManifestParts(new[] { "1", "2026-07-14 15:04:05", "file", "a", "b", "c", "d", "e", "active", "20260714_150500" }, "2026-07-14 15:04:05") == "20260714_150500", "new restore manifest keeps explicit run id");

        string root = Path.Combine(Path.GetTempPath(), "CK3MPS-restore-rules");
        string ck3Docs = Path.Combine(root, "Documents", "Paradox Interactive", "Crusader Kings III");
        string localLauncher = Path.Combine(root, "Local", "Paradox Interactive", "launcher-v2");
        string roamingLauncher = Path.Combine(root, "Roaming", "Paradox Interactive", "launcher-v2");
        Assert(RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "pdx_settings.txt"), ck3Docs, localLauncher, roamingLauncher), "default restore allows CK3 Documents files");
        Assert(RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(localLauncher, "Cache"), ck3Docs, localLauncher, roamingLauncher), "default restore allows launcher local cache");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "save games"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks save games folder deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "save games", "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks save file deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "mod", "example.mod"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks mod descriptor deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(root, "Steam", "userdata", "localconfig.vdf"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks Steam config deletion");
    }

    private static void TestRegistryValueSerialization()
    {
        string serializedString = RestoreManifestUtilities.SerializeRegistryValue("line1;\r\nline2\\tail", Microsoft.Win32.RegistryValueKind.String);
        Assert(serializedString == "String:line1\\;\\r\\nline2\\\\tail", "registry string snapshot escapes control characters");
        Assert((string)RestoreManifestUtilities.ParseSerializedRegistryValue("line1\\;\\r\\nline2\\\\tail", Microsoft.Win32.RegistryValueKind.String) == "line1;\r\nline2\\tail", "registry string snapshot restores escaped value");

        string serializedMulti = RestoreManifestUtilities.SerializeRegistryValue(new[] { "alpha", "semi;colon", "line\r\nbreak", "slash\\tail" }, Microsoft.Win32.RegistryValueKind.MultiString);
        Assert(serializedMulti == "MultiString:alpha;semi\\;colon;line\\r\\nbreak;slash\\\\tail", "registry multistring snapshot stores escaped entries");
        string[] restoredMulti = (string[])RestoreManifestUtilities.ParseSerializedRegistryValue("alpha;semi\\;colon;line\\r\\nbreak;slash\\\\tail", Microsoft.Win32.RegistryValueKind.MultiString);
        Assert(restoredMulti.Length == 4
            && restoredMulti[0] == "alpha"
            && restoredMulti[1] == "semi;colon"
            && restoredMulti[2] == "line\r\nbreak"
            && restoredMulti[3] == "slash\\tail", "registry multistring snapshot restores all entries");
    }

    private static void TestRuntimeModeUtilities()
    {
        Assert(RuntimeModeUtilities.ResolveStabilizerRoot(@"C:\Docs\CK3MPS", @"D:\Apps\CK3MPS_Data", false) == @"C:\Docs\CK3MPS", "non-portable mode keeps Documents state root");
        Assert(RuntimeModeUtilities.ResolveStabilizerRoot(@"C:\Docs\CK3MPS", @"D:\Apps\CK3MPS_Data", true) == @"D:\Apps\CK3MPS_Data", "portable mode switches state root next to exe");
        Assert(RuntimeModeUtilities.ShouldSuppressLogLine("Quiet", "INFO  | details"), "quiet suppresses info lines");
        Assert(RuntimeModeUtilities.ShouldSuppressLogLine("Quiet", "FILE  | report.txt"), "quiet suppresses file lines");
        Assert(!RuntimeModeUtilities.ShouldSuppressLogLine("Quiet", "WARN  | warning"), "quiet keeps warning lines");
        Assert(RuntimeModeUtilities.ShouldSuppressLogLine("Normal", "VERBOSE| extra"), "normal suppresses verbose lines");
        Assert(!RuntimeModeUtilities.ShouldSuppressLogLine("Normal", "INFO  | details"), "normal keeps info lines");
        Assert(!RuntimeModeUtilities.ShouldSuppressLogLine("Verbose", "VERBOSE| extra"), "verbose keeps verbose lines");
        Assert(!RuntimeModeUtilities.ShouldSuppressLogLine("Verbose", ""), "verbose keeps blank separator lines");
    }

    private static void TestSaveRuleUtilities()
    {
        string text = "game_rules={\n\tmultiplayer_murder_schemes=\"no_players\"\n\tgreat_steppe=\"off\"\n\tai_landless_adventurers=25\n}\n";
        string block = SaveRuleUtilities.ExtractBraceBlock(text, "game_rules");
        Assert(block.IndexOf("multiplayer_murder_schemes", StringComparison.OrdinalIgnoreCase) >= 0, "save-rule utility extracts game_rules block");
        Assert(SaveRuleUtilities.ValueLooksNoPlayers("no_players"), "save-rule utility recognizes no-players value");
        Assert(SaveRuleUtilities.ValueLooksDisabled("disabled"), "save-rule utility recognizes disabled value");
        Assert(SaveRuleUtilities.TryParseIntValue("25").HasValue && SaveRuleUtilities.TryParseIntValue("25").Value == 25, "save-rule utility parses integer values");
    }

    private static void TestPathContainmentUtilities()
    {
        string root = Path.Combine(Path.GetTempPath(), "CK3MPS-path-policy-" + Guid.NewGuid().ToString("N"));
        string saveRoot = Path.Combine(root, "Documents", "Paradox Interactive", "Crusader Kings III", "save games");
        string validSave = Path.Combine(saveRoot, "campaign.ck3");
        string sibling = Path.Combine(Path.GetDirectoryName(saveRoot), "save games hacked", "campaign.ck3");

        try
        {
            Directory.CreateDirectory(saveRoot);
            File.WriteAllText(validSave, "save");
            string normalizedValid;
            Assert(PathContainmentUtilities.TryNormalizeAbsolutePath(validSave, out normalizedValid) && normalizedValid == Path.GetFullPath(validSave), "path policy normalizes absolute path");
            Assert(PathContainmentUtilities.IsWithinRoot(saveRoot, validSave), "path policy accepts path inside root");
            Assert(!PathContainmentUtilities.IsWithinRoot(saveRoot, sibling), "path policy rejects sibling with shared prefix");
            Assert(PathContainmentUtilities.IsManagedSaveFilePath(saveRoot, validSave), "path policy accepts managed CK3 save");
            Assert(!PathContainmentUtilities.IsManagedSaveFilePath(saveRoot, Path.Combine(saveRoot, "descriptor.mod")), "path policy rejects non-ck3 file in save root");
            string ignored;
            Assert(!PathContainmentUtilities.TryNormalizeAbsolutePath("relative\\campaign.ck3", out ignored), "path policy rejects relative path");
            Assert(!PathContainmentUtilities.TryNormalizeAbsolutePath(validSave + ":evil", out ignored), "path policy rejects alternate data stream path");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestIncidentHistoryJsonUtilities()
    {
        string line = IncidentHistoryJsonUtilities.BuildJsonLine("2026-07-16T12:34:56Z", "watcher_detected", "incident-1", "Repeated OOS", "Rollback", 87, "high", false, "note with \"quotes\"");
        string[] parsed = IncidentHistoryJsonUtilities.ParseLine(line);
        Assert(parsed != null && parsed.Length >= 9, "incident history JSON line parses into fields");
        Assert(parsed[0] == "2026-07-16T12:34:56Z", "incident history JSON keeps timestamp");
        Assert(parsed[2] == "incident-1", "incident history JSON keeps incident id");
        Assert(parsed[4] == "Rollback", "incident history JSON keeps recommended path");
        Assert(parsed[5] == "87", "incident history JSON keeps risk score");
        Assert(parsed[7] == "blocked", "incident history JSON keeps hotjoin state");
    }

    private static void TestCompactReportNames()
    {
        Assert(StabilizerFileNameUtilities.CompactName("ck3_stabilizer_last_report.txt") == "report.txt", "last report keeps compact legacy file name");
        Assert(StabilizerFileNameUtilities.CompactName("ck3_stabilizer_runtime_verification.txt") == "runtime_verification.txt", "runtime verification keeps unique compact file name");
        Assert(StabilizerFileNameUtilities.CompactName("ck3_stabilizer_latest_oos_summary.txt") == "latest_oos_summary.txt", "latest OOS summary keeps unique compact file name");
        Assert(StabilizerFileNameUtilities.CompactName("ck3_stabilizer_portable_notes.txt") == "portable_notes.txt", "portable notes keeps unique compact file name");
        Assert(StabilizerFileNameUtilities.CompactName("ck3_stabilizer_cache_cleanup.txt") == "cache_cleanup.txt", "cache cleanup keeps unique compact file name");
    }

    private static void TestChecksumExtraction()
    {
        string hashA = new string('A', 64).ToLowerInvariant();
        string hashB = new string('B', 64).ToLowerInvariant();
        Assert(ChecksumUtilities.ExtractExpectedSha256(hashA, "CK3MPS-v0.3.zip") == hashA, "single-hash checksum asset is accepted");
        Assert(ChecksumUtilities.ExtractExpectedSha256(hashA + "  CK3MPS-v0.3.zip\r\n" + hashB + "  CK3MPS.exe", "CK3MPS-v0.3.zip") == hashA, "named checksum line matches the requested asset");
        Assert(ChecksumUtilities.ExtractExpectedSha256("CK3MPS.exe " + hashB + "\r\nCK3MPS-v0.3.zip " + hashA, "CK3MPS.exe") == hashB, "reversed checksum line matches the requested asset");
        Assert(ChecksumUtilities.ExtractExpectedSha256(hashA + "  other-file.zip", "CK3MPS-v0.3.zip") == "", "foreign checksum line is rejected");
    }

    private static void TestRecommendedPresetSafety()
    {
        int[] recommended = PresetUtilities.RecommendedStepIndices();
        Assert(PresetUtilities.ContainsStep(recommended, 11), "recommended keeps Steam launch option stabilization");
        Assert(PresetUtilities.ContainsStep(recommended, 14), "recommended keeps no-mod dlc_load stabilization");
        Assert(PresetUtilities.ContainsStep(recommended, 15), "recommended keeps pdx_settings stabilization");
        Assert(!PresetUtilities.ContainsStep(recommended, 5), "recommended skips firewall rule changes");
        Assert(!PresetUtilities.ContainsStep(recommended, 6), "recommended skips Windows registry game/network tuning");
        Assert(!PresetUtilities.ContainsStep(recommended, 7), "recommended skips powercfg adapter tuning");
        Assert(!PresetUtilities.ContainsStep(recommended, 21), "recommended skips local mod descriptor quarantine");
        Assert(!PresetUtilities.ContainsStep(recommended, 23), "recommended skips save hygiene quarantine");
        Assert(!PresetUtilities.ContainsStep(recommended, 24), "recommended skips broad CK3 Documents cleanup");
    }

    private static void TestVersionComparison()
    {
        Assert(VersionUtilities.IsNewerRelease("v0.3", "v0.2"), "v0.3 should be newer than v0.2");
        Assert(VersionUtilities.IsNewerRelease("v1.0.1", "v1.0.0"), "patch version should compare");
        Assert(!VersionUtilities.IsNewerRelease("v0.3", "v0.3"), "same version should not be newer");
        Assert(!VersionUtilities.IsNewerRelease("v0.2", "v0.3"), "older version should not be newer");
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
