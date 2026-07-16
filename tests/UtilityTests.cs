using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
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
        TestBoundedTraversalUtilities();
        TestIncidentHistoryJsonUtilities();
        TestSafeAtomicFile();
        TestWindowsCommandLineUtilities();
        TestValveVdfUtilities();
        TestPdxSettingsUtilities();

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
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(ck3Docs, ck3Docs, localLauncher, roamingLauncher), "default restore blocks CK3 Documents root");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "save games"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks save games folder deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "save games", "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks save file deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks top-level CK3 save file");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "mod", "example.mod"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks mod descriptor deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(ck3Docs, "mod"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks mod root deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(root, "Steam", "userdata", "localconfig.vdf"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks Steam config deletion");
        Assert(!RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(Path.Combine(root, "Documents", "Paradox Interactive", "Crusader Kings III hacked", "pdx_settings.txt"), ck3Docs, localLauncher, roamingLauncher), "default restore blocks sibling root with shared prefix");
        Assert(RestoreManifestUtilities.IsDefaultRestorablePath(Path.Combine(ck3Docs, "pdx_settings.txt"), ck3Docs, localLauncher, roamingLauncher), "default restore allows explicit CK3 config file");
        Assert(!RestoreManifestUtilities.IsDefaultRestorablePath(Path.Combine(ck3Docs, "save games", "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher), "default restore denies managed workflow save");
        Assert(!RestoreManifestUtilities.IsDefaultRestorablePath(Path.Combine(ck3Docs, "mod"), ck3Docs, localLauncher, roamingLauncher), "default restore denies mod root");
        Assert(RestoreManifestUtilities.GetRestorePathKind(Path.Combine(ck3Docs, "save games", "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher) == RestoreManifestUtilities.RestorePathKind.ManagedWorkflowSave, "restore selected recognizes a direct managed save");
        Assert(RestoreManifestUtilities.GetRestorePathKind(Path.Combine(ck3Docs, "save games", "nested", "campaign.ck3"), ck3Docs, localLauncher, roamingLauncher) == RestoreManifestUtilities.RestorePathKind.Unknown, "restore selected rejects nested save paths");
        Assert(RestoreManifestUtilities.GetRestorePathKind(Path.Combine(ck3Docs, "mod", "payload.bin"), ck3Docs, localLauncher, roamingLauncher) == RestoreManifestUtilities.RestorePathKind.Unknown, "restore selected rejects arbitrary mod files");
        Assert(RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled"), "registry allowlist accepts known GameDVR value");
        Assert(RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences\C:\Games\Crusader Kings III\binaries\ck3.exe"), "registry allowlist accepts CK3 GPU preference value");
        Assert(!RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences\C:\Games\OtherGame\game.exe"), "registry allowlist rejects foreign executable value");
        Assert(!RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(@"HKLM\SOFTWARE\Example\Dangerous\Anything"), "registry allowlist rejects unrelated HKLM path");
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

    private static void TestSafeAtomicFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "CK3MPS-atomic-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "history.txt");

        try
        {
            Directory.CreateDirectory(root);

            AtomicWriteResult validationFailed = SafeAtomicFile.TryWriteAllText(target, "first", new System.Text.UTF8Encoding(false), delegate { return false; });
            Assert(validationFailed.Status == AtomicWriteStatus.ValidationFailed, "atomic writer reports validation failure");
            Assert(!File.Exists(target), "atomic writer does not create target on validation failure");

            Task[] writers = new Task[10];
            for (int i = 0; i < writers.Length; i++)
            {
                int lineNumber = i;
                writers[i] = Task.Run(delegate
                {
                    AtomicWriteResult result = SafeAtomicFile.TryAppendText(target, "line-" + lineNumber + Environment.NewLine, new System.Text.UTF8Encoding(false));
                    if (!result.Succeeded)
                        throw new InvalidOperationException(result.Message);
                });
            }

            Task.WaitAll(writers);
            string[] lines = File.ReadAllLines(target);
            Assert(lines.Length == 10, "atomic append serializes concurrent writers");

            File.WriteAllText(target, new string('x', (2 * 1024 * 1024) - 4));
            AtomicWriteResult rotated = SafeAtomicFile.TryAppendText(target, "rotation-trigger", new System.Text.UTF8Encoding(false));
            Assert(rotated.Succeeded, "bounded history append succeeds after rotation");
            Assert(File.Exists(target + ".1"), "bounded history append rotates the previous file");
            Assert(File.ReadAllText(target) == "rotation-trigger", "bounded history append starts a fresh current file");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestWindowsCommandLineUtilities()
    {
        string commandLine = "-noasync \"C:\\Program Files\\Paradox\\launcher helper.exe\" -debug_mode=1 plain";
        var tokens = WindowsCommandLineUtilities.Tokenize(commandLine);
        Assert(tokens.Count == 4, "command-line tokenizer keeps quoted argument groups");
        Assert(tokens[1] == "C:\\Program Files\\Paradox\\launcher helper.exe", "command-line tokenizer preserves spaces inside quotes");
        Assert(tokens[2] == "-debug_mode=1", "command-line tokenizer preserves = style flags");
        Assert(WindowsCommandLineUtilities.QuoteArgument("C:\\Program Files\\Paradox\\launcher helper.exe") == "\"C:\\Program Files\\Paradox\\launcher helper.exe\"", "command-line quoting re-wraps space-containing arguments");
        Assert(WindowsCommandLineUtilities.QuoteArgument("plain") == "plain", "command-line quoting leaves simple arguments unchanged");
    }

    private static void TestValveVdfUtilities()
    {
        string text =
            "\"UserLocalConfigStore\"\r\n{\r\n" +
            "\t\"Software\"\r\n\t{\r\n" +
            "\t\t\"Valve\"\r\n\t\t{\r\n" +
            "\t\t\t\"Steam\"\r\n\t\t\t{\r\n" +
            "\t\t\t\t\"apps\"\r\n\t\t\t\t{\r\n" +
            "\t\t\t\t\t\"1158310\"\r\n\t\t\t\t\t{\r\n" +
            "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"\\\"C:\\\\Program Files\\\\Paradox\\\\launcher helper.exe\\\" -debug_mode=1\"\r\n" +
            "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"stale\"\r\n" +
            "\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}\r\n";

        ValveVdfUtilities.VdfObject root;
        string error;
        Assert(ValveVdfUtilities.TryParse(text, out root, out error), "VDF parser accepts nested Steam config text");
        var app = ValveVdfUtilities.FindPath(root, "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", "1158310");
        Assert(app != null, "VDF parser navigates nested object path");
        Assert(app.GetString("LaunchOptions") == "\"C:\\Program Files\\Paradox\\launcher helper.exe\" -debug_mode=1", "VDF parser keeps escaped quoted string values");
        app.SetString("LaunchOptions", "-noasync \"C:\\Program Files\\Paradox\\launcher helper.exe\"");
        string serialized = ValveVdfUtilities.Serialize(root);
        Assert(serialized.IndexOf("\\\"C:\\\\Program Files\\\\Paradox\\\\launcher helper.exe\\\"", StringComparison.Ordinal) >= 0, "VDF serializer re-escapes quoted paths");
        Assert(serialized.IndexOf("\"LaunchOptions\"\t\t\"stale\"", StringComparison.OrdinalIgnoreCase) < 0, "VDF SetString replaces duplicate keys with one predictable value");
        app.RemoveAll("LaunchOptions");
        Assert(app.GetString("LaunchOptions") == "", "VDF remove clears matching keys");
    }

    private static void TestPdxSettingsUtilities()
    {
        string text =
            "\"game\"={\r\n" +
            "\t\"autosave\"={\r\n" +
            "\t\t\"version\"=0\r\n" +
            "\t\t\"value\"=\"MONTHLY\"\r\n" +
            "\t}\r\n" +
            "}\r\n" +
            "\"Graphics\"={\r\n" +
            "\t\"renderer\"={\r\n" +
            "\t\t\"version\"=0\r\n" +
            "\t\t\"value\"=\"OpenGL\"\r\n" +
            "\t}\r\n" +
            "}\r\n";

        PdxSettingsUtilities.PdxObject root;
        string error;
        Assert(PdxSettingsUtilities.TryParse(text, out root, out error), "pdx_settings parser accepts nested settings text");
        var graphics = PdxSettingsUtilities.FindPath(root, "Graphics");
        Assert(graphics != null, "pdx_settings parser navigates to section object");
        Assert(PdxSettingsUtilities.SectionSettingMatches(text, "Graphics", "renderer", new Dictionary<string, string> { { "version", "0" }, { "value", "OpenGL" } }), "pdx_settings matcher reads expected field values");

        string updated = PdxSettingsUtilities.SetSectionSettingBlock(text, "Graphics", "renderer", new Dictionary<string, string> { { "version", "0" }, { "value", "Vulkan" } });
        Assert(PdxSettingsUtilities.SectionSettingMatches(updated, "Graphics", "renderer", new Dictionary<string, string> { { "version", "0" }, { "value", "Vulkan" } }), "pdx_settings writer replaces existing section setting");

        string inserted = PdxSettingsUtilities.SetSectionSettingBlock(updated, "Audio", "audio_debug_log_level", new Dictionary<string, string> { { "version", "0" }, { "value", "error" } });
        Assert(PdxSettingsUtilities.SectionSettingMatches(inserted, "Audio", "audio_debug_log_level", new Dictionary<string, string> { { "version", "0" }, { "value", "error" } }), "pdx_settings writer inserts missing section setting");

        string sectionBody = PdxSettingsUtilities.ExtractSectionBody(inserted, "Graphics");
        Assert(sectionBody.IndexOf("\"renderer\"", StringComparison.OrdinalIgnoreCase) >= 0, "pdx_settings section extraction returns serialized section body");
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
            string cycleLink = Path.Combine(root, "cycle-link");
            TryRemoveJunction(cycleLink);

            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                    TryRemoveDirectoryTreeWithCmd(root);
                }
            }
        }
    }

    private static void TestBoundedTraversalUtilities()
    {
        string root = Path.Combine(Path.GetTempPath(), "CK3MPS-traversal-" + Guid.NewGuid().ToString("N"));
        string userData = Path.Combine(root, "Steam", "userdata");
        string numericUser = Path.Combine(userData, "123456");
        string otherUser = Path.Combine(userData, "not-a-user");
        string appDir = Path.Combine(numericUser, "1158310");
        string similarAppDir = Path.Combine(numericUser, "1158310_extra");
        string configDir = Path.Combine(numericUser, "config");
        string deepRoot = Path.Combine(root, "deep");

        try
        {
            Directory.CreateDirectory(Path.Combine(appDir, "remote", "save games"));
            Directory.CreateDirectory(similarAppDir);
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(otherUser);
            Directory.CreateDirectory(Path.Combine(deepRoot, "level1", "level2", "level3"));
            File.WriteAllText(Path.Combine(configDir, "localconfig.vdf"), "\"1158310\"{}");
            File.WriteAllText(Path.Combine(configDir, "sharedconfig.vdf"), "\"1158310\"{}");
            File.WriteAllText(Path.Combine(deepRoot, "root.txt"), "root");
            File.WriteAllText(Path.Combine(deepRoot, "level1", "one.txt"), "one");
            File.WriteAllText(Path.Combine(deepRoot, "level1", "level2", "two.txt"), "two");
            File.WriteAllText(Path.Combine(deepRoot, "level1", "level2", "level3", "three.txt"), "three");

            var users = BoundedTraversalUtilities.EnumerateSteamUserDirectories(userData, 8, 2000);
            Assert(users.Count == 1 && String.Equals(users[0], numericUser, StringComparison.OrdinalIgnoreCase), "bounded traversal keeps only numeric Steam userdata directories");

            var appDirs = BoundedTraversalUtilities.EnumerateSteamUserAppDirectories(userData, "1158310", 8, 8, 2000);
            Assert(appDirs.Count == 1 && String.Equals(appDirs[0], appDir, StringComparison.OrdinalIgnoreCase), "bounded traversal accepts exact Steam app id directory only");

            var deepFiles = BoundedTraversalUtilities.EnumerateFilesBounded(deepRoot, "*.txt", new BoundedTraversalUtilities.TraversalSettings
            {
                MaxDirectories = 16,
                MaxFiles = 16,
                MaxDepth = 1,
                MaxElapsedMs = 2000
            });
            Assert(deepFiles.Paths.Count == 2, "bounded traversal respects maximum depth");

            var limitedFiles = BoundedTraversalUtilities.EnumerateFilesBounded(deepRoot, "*.txt", new BoundedTraversalUtilities.TraversalSettings
            {
                MaxDirectories = 16,
                MaxFiles = 2,
                MaxDepth = 8,
                MaxElapsedMs = 2000
            });
            Assert(limitedFiles.Paths.Count == 2 && limitedFiles.HitFileLimit, "bounded traversal respects file limit");

            var timedOut = BoundedTraversalUtilities.EnumerateFilesBounded(deepRoot, "*.txt", new BoundedTraversalUtilities.TraversalSettings
            {
                MaxDirectories = 16,
                MaxFiles = 16,
                MaxDepth = 8,
                MaxElapsedMs = 0
            });
            Assert(timedOut.TimedOut, "bounded traversal respects timeout");

            string cycleTarget = Path.Combine(root, "cycle-target");
            string cycleLink = Path.Combine(root, "cycle-link");
            Directory.CreateDirectory(Path.Combine(cycleTarget, "nested"));
            File.WriteAllText(Path.Combine(cycleTarget, "nested", "cycle.txt"), "cycle");
            if (TryCreateJunction(cycleLink, cycleTarget))
            {
                var cycleFiles = BoundedTraversalUtilities.EnumerateFilesBounded(root, "*.txt", new BoundedTraversalUtilities.TraversalSettings
                {
                    MaxDirectories = 32,
                    MaxFiles = 32,
                    MaxDepth = 8,
                    MaxElapsedMs = 2000
                });
                bool foundViaLink = false;
                foreach (string path in cycleFiles.Paths)
                    if (path.IndexOf("cycle-link", StringComparison.OrdinalIgnoreCase) >= 0)
                        foundViaLink = true;
                Assert(!foundViaLink, "bounded traversal skips reparse-point directories");
            }
        }
        finally
        {
            string cycleLink = Path.Combine(root, "cycle-link");
            TryRemoveJunction(cycleLink);

            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                    TryRemoveDirectoryTreeWithCmd(root);
                }
            }
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

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath, true);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c mklink /J \"" + linkPath + "\" \"" + targetPath + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    return false;
                }
            }

            return Directory.Exists(linkPath) && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRemoveJunction(string linkPath)
    {
        try
        {
            if (!Directory.Exists(linkPath))
                return;

            FileAttributes attributes = File.GetAttributes(linkPath);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
                return;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c rmdir \"" + linkPath + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
            }
        }
        catch
        {
        }
    }

    private static void TryRemoveDirectoryTreeWithCmd(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c rmdir /s /q \"" + path + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
            }
        }
        catch
        {
        }
    }
}
