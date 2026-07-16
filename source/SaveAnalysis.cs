using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class CriticalSaveRuleDefinition
        {
            public string Id;
            public string DisplayName;
            public string Expected;
            public string[] Keys;
            public string[] SettingsTokens;
            public string SafeToken;
            public Func<string, bool> SafeCheck;

            public CriticalSaveRuleDefinition(string id, string displayName, string expected, string[] keys, string[] settingsTokens, string safeToken, Func<string, bool> safeCheck)
            {
                Id = id;
                DisplayName = displayName;
                Expected = expected;
                Keys = keys ?? new string[0];
                SettingsTokens = settingsTokens ?? new string[0];
                SafeToken = safeToken ?? "";
                SafeCheck = safeCheck;
            }
        }

        private static readonly CriticalSaveRuleDefinition[] CriticalSaveRuleDefinitions = new[]
        {
            new CriticalSaveRuleDefinition(
                "multiplayer_murder_schemes",
                "Multiplayer murder schemes",
                "No Players",
                new[] { "multiplayer_murder_schemes", "multiplayer_murder_scheme", "murder_schemes" },
                new[] { "default_multiplayer_murder_schemes", "no_players_multiplayer_murder_schemes", "no_player_families_multiplayer_murder_schemes" },
                "no_players_multiplayer_murder_schemes",
                RuleValueIsNoPlayersToken),
            new CriticalSaveRuleDefinition(
                "ai_landless_adventurers",
                "AI Landless Adventurers",
                "25 or lower",
                new[] { "ai_landless_adventurers", "landless_adventurers", "landless_adventurer_ai", "ai_laamp_numbers" },
                new[] { "ai_laamp_numbers_200", "ai_laamp_numbers_150", "ai_laamp_numbers_100", "ai_laamp_numbers_50", "ai_laamp_numbers_25" },
                "ai_laamp_numbers_25",
                RuleValueIsAtMost25Static),
            new CriticalSaveRuleDefinition(
                "great_steppe",
                "Great Steppe",
                "Off",
                new[] { "great_steppe", "great_steppe_frequency", "rule_great_steppe" },
                new[] { "situation_the_great_steppe_toggle_on", "situation_the_great_steppe_toggle_off" },
                "situation_the_great_steppe_toggle_off",
                RuleValueIsSteppeOffToken),
            new CriticalSaveRuleDefinition(
                "natural_disaster_earthquakes",
                "Natural disaster earthquakes",
                "Disabled",
                new[] { "natural_disaster_earthquakes", "earthquakes", "rule_earthquakes" },
                new[] { "natural_disaster_earthquakes_regular", "natural_disaster_earthquakes_few", "natural_disaster_earthquakes_disabled" },
                "natural_disaster_earthquakes_disabled",
                RuleValueIsDisabledToken),
            new CriticalSaveRuleDefinition(
                "natural_disaster_floods",
                "Natural disaster floods",
                "Disabled",
                new[] { "natural_disaster_floods", "floods", "rule_floods" },
                new[] { "natural_disaster_floods_regular", "natural_disaster_floods_few", "natural_disaster_floods_disabled" },
                "natural_disaster_floods_disabled",
                RuleValueIsDisabledToken)
        };

        private HostSaveCandidateResult AnalyzeBestHostSaveCandidate()
        {
            string cacheKey = BuildBestHostSaveCandidateCacheKey();
            if (cachedBestHostSaveCandidate != null
                && String.Equals(cachedBestHostSaveCandidateKey, cacheKey, StringComparison.Ordinal))
                return cachedBestHostSaveCandidate;

            HostSaveCandidateResult best = AnalyzeBestHostSaveCandidateCore();
            cachedBestHostSaveCandidate = best;
            cachedBestHostSaveCandidateKey = cacheKey;
            return best;
        }

        private HostSaveCandidateResult AnalyzeBestHostSaveCandidateCore()
        {
            HostSaveCandidateResult best = null;
            foreach (string path in EnumerateHostSaveCandidates())
            {
                HostSaveCandidateResult candidate = AnalyzeHostSaveCandidate(path);
                if (String.Equals(candidate.Verdict, "RECOMMENDED HOST BASELINE", StringComparison.OrdinalIgnoreCase))
                    return candidate;

                if (best == null || candidate.Score > best.Score)
                    best = candidate;
                else if (candidate.Score == best.Score
                    && File.Exists(candidate.Save.Path)
                    && File.Exists(best.Save.Path)
                    && File.GetLastWriteTimeUtc(candidate.Save.Path) > File.GetLastWriteTimeUtc(best.Save.Path))
                    best = candidate;
            }

            if (best != null)
                return best;

            HostSaveCandidateResult empty = new HostSaveCandidateResult();
            empty.Verdict = "NO SAVE FOUND";
            empty.Issues.Add("No local .ck3 save files were found in the active save folder.");
            return empty;
        }

        private string BuildBestHostSaveCandidateCacheKey()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(NullText(ck3Docs));
                sb.Append("|");
                sb.Append(DetectInstalledVersion());
                foreach (string path in EnumerateHostSaveCandidates())
                {
                    FileInfo info = new FileInfo(path);
                    sb.Append("|");
                    sb.Append(info.Name);
                    sb.Append("|");
                    sb.Append(info.Length);
                    sb.Append("|");
                    sb.Append(info.LastWriteTimeUtc.Ticks);
                }
                return sb.ToString();
            }
            catch
            {
                return NullText(ck3Docs) + "|fallback";
            }
        }

        private IEnumerable<string> EnumerateHostSaveCandidates()
        {
            return EnumerateHostSaveCandidates(12);
        }

        private IEnumerable<string> EnumerateHostSaveCandidates(int maxCount)
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
                yield break;

            FileInfo[] saves = new DirectoryInfo(saveDir).GetFiles("*.ck3");
            Array.Sort(saves, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });

            int count = 0;
            foreach (FileInfo save in saves)
            {
                yield return save.FullName;
                count++;
                if (count >= Math.Max(1, maxCount))
                    yield break;
            }
        }

        private HostSaveCandidateResult AnalyzeWorkflowHostSaveCandidate()
        {
            if (String.IsNullOrWhiteSpace(workflowSelectedSavePath))
            {
                HostSaveCandidateResult empty = new HostSaveCandidateResult();
                empty.Verdict = "NO SAVE SELECTED";
                empty.Issues.Add("No workflow host save is selected.");
                return empty;
            }

            if (!File.Exists(workflowSelectedSavePath))
            {
                HostSaveCandidateResult missing = new HostSaveCandidateResult();
                missing.Save.Path = workflowSelectedSavePath;
                missing.Verdict = "MISSING SELECTED SAVE";
                missing.Issues.Add("The selected workflow host save file does not exist.");
                return missing;
            }

            HostSaveCandidateResult selected = AnalyzeHostSaveCandidate(workflowSelectedSavePath);
            if (selected.Score >= 85 && selected.Save.Readable && selected.Save.VersionMatchesInstalled && AllCriticalRulesSafe(selected.Save.Rules))
                selected.Verdict = "SELECTED HOST BASELINE";
            return selected;
        }

        private string WorkflowHostSaveSelectionDescription()
        {
            return "selected workflow host save";
        }

        private HostSaveCandidateResult AnalyzeHostSaveCandidate(string path)
        {
            SaveAnalysisResult analysis = AnalyzeSave(path);
            HostSaveCandidateResult result = new HostSaveCandidateResult();
            result.Save = analysis;
            result.Score = 100;

            if (!analysis.Readable)
            {
                result.Score -= 35;
                result.Issues.Add("Save could not be parsed safely.");
            }
            else
            {
                result.Strengths.Add("Save is readable from " + NullText(analysis.SourceKind) + ".");
            }

            if (analysis.SuspiciousName)
            {
                result.Score -= 40;
                result.Issues.Add("Save name looks like autosave/recovery/desync-like state.");
            }
            else if (!String.IsNullOrEmpty(analysis.SaveName))
            {
                result.Strengths.Add("Save name is not flagged as suspicious.");
            }

            if (!analysis.VersionMatchesInstalled)
            {
                result.Score -= 25;
                result.Issues.Add("Save version does not match installed CK3 version.");
            }
            else if (!String.IsNullOrEmpty(analysis.Version))
            {
                result.Strengths.Add("Save version matches installed CK3 version.");
            }

            int unsafeRules = 0;
            int unknownRules = 0;
            foreach (SaveRuleCheckResult rule in analysis.Rules)
            {
                if (!rule.Found)
                {
                    unknownRules++;
                    continue;
                }

                if (!rule.Safe)
                {
                    unsafeRules++;
                    result.Issues.Add(rule.DisplayName + " is not on the safe MP profile: " + NullText(rule.Actual));
                }
                else
                {
                    result.Strengths.Add(rule.DisplayName + " matches the safe profile.");
                }
            }

            result.Score -= unsafeRules * 12;
            result.Score -= Math.Min(12, unknownRules * 2);

            if (result.Score < 0)
                result.Score = 0;

            if (result.Score >= 85 && analysis.Readable && !analysis.SuspiciousName && analysis.VersionMatchesInstalled && unsafeRules == 0)
                result.Verdict = "RECOMMENDED HOST BASELINE";
            else if (result.Score >= 70 && analysis.Readable && !analysis.SuspiciousName)
                result.Verdict = "USABLE WITH REVIEW";
            else if (result.Score >= 50)
                result.Verdict = "RISKY";
            else
                result.Verdict = "DO NOT HOST FROM THIS SAVE";

            return result;
        }

        private SaveAnalysisResult AnalyzeSave(string path)
        {
            SaveAnalysisResult result = new SaveAnalysisResult();
            result.Path = path ?? "";
            result.SaveName = Path.GetFileName(path ?? "");
            result.SuspiciousName = IsSuspiciousSaveName(result.SaveName);

            string text;
            string sourceKind;
            result.Readable = TryReadSaveAnalysisText(path, out text, out sourceKind);
            result.SourceKind = sourceKind;
            result.Version = ExtractSaveStructuredValue(text, "version");
            result.Date = ExtractSaveStructuredValue(text, "meta_date");
            result.Player = ExtractSaveStructuredValue(text, "meta_player_name");
            result.Title = ExtractSaveStructuredValue(text, "meta_title_name");
            string installedVersion = DetectInstalledVersion();
            result.VersionMatchesInstalled = !String.IsNullOrEmpty(result.Version)
                && !String.IsNullOrEmpty(installedVersion)
                && String.Equals(result.Version, installedVersion, StringComparison.OrdinalIgnoreCase);
            result.Rules.AddRange(EvaluateCriticalGameRules(text));
            return result;
        }

        private bool TryReadSaveAnalysisText(string path, out string text, out string sourceKind)
        {
            text = "";
            sourceKind = "";
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxSaveAnalysisFileBytes)
                    return false;

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] signature = new byte[4];
                    int read = stream.Read(signature, 0, signature.Length);
                    stream.Position = 0;
                    bool isZip = read >= 4 && signature[0] == (byte)'P' && signature[1] == (byte)'K';
                    if (!isZip)
                    {
                        byte[] prefixBytes = ReadStreamPrefixBytes(stream, MaxSaveAnalysisPrefixBytes);
                        text = Encoding.UTF8.GetString(prefixBytes);
                        int embeddedZipOffset = FindEmbeddedZipOffset(prefixBytes);
                        if (embeddedZipOffset > 0 && info.Length - embeddedZipOffset <= MaxEmbeddedZipAnalysisBytes)
                        {
                            stream.Position = embeddedZipOffset;
                            using (MemoryStream zipStream = new MemoryStream())
                            {
                                CopyFixedBytes(stream, zipStream, (int)Math.Min(MaxEmbeddedZipAnalysisBytes, info.Length - embeddedZipOffset));
                                zipStream.Position = 0;
                                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
                                {
                                    StringBuilder sb = new StringBuilder();
                                    AppendZipEntryText(archive, sb, "meta", 1024 * 1024);
                                    AppendZipEntryText(archive, sb, "gamestate", 3 * 1024 * 1024);
                                    string zipText = sb.ToString();
                                    if (zipText.Length > 0)
                                    {
                                        text = zipText;
                                        sourceKind = "hybrid embedded zip meta/gamestate";
                                        return true;
                                    }
                                }
                            }
                        }

                        sourceKind = "plaintext header";
                        return text.IndexOf("meta_data", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("game_rules", StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
                    {
                        StringBuilder sb = new StringBuilder();
                        AppendZipEntryText(archive, sb, "meta", 1024 * 1024);
                        AppendZipEntryText(archive, sb, "gamestate", 3 * 1024 * 1024);
                        text = sb.ToString();
                        sourceKind = "zip meta/gamestate";
                        return text.Length > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private string ReadStreamPrefixText(Stream stream, int maxBytes)
        {
            return Encoding.UTF8.GetString(ReadStreamPrefixBytes(stream, maxBytes));
        }

        private byte[] ReadStreamPrefixBytes(Stream stream, int maxBytes)
        {
            long safeLength = 0;
            try
            {
                safeLength = Math.Max(0, stream.Length);
            }
            catch
            {
                safeLength = maxBytes;
            }
            int capacity = (int)Math.Min(maxBytes, safeLength);
            byte[] buffer = new byte[capacity];
            int read = stream.Read(buffer, 0, capacity);
            if (read == buffer.Length)
                return buffer;

            byte[] actual = new byte[read];
            Buffer.BlockCopy(buffer, 0, actual, 0, read);
            return actual;
        }

        private void CopyFixedBytes(Stream source, Stream destination, int maxBytes)
        {
            int remaining = Math.Max(0, maxBytes);
            byte[] buffer = new byte[8192];
            while (remaining > 0)
            {
                int read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0)
                    break;
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private void AppendZipEntryText(ZipArchive archive, StringBuilder sb, string entryName, int maxBytes)
        {
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(delegate (ZipArchiveEntry current)
            {
                return String.Equals(current.Name, entryName, StringComparison.OrdinalIgnoreCase);
            });
            if (entry == null)
                return;

            using (Stream entryStream = entry.Open())
            {
                int remaining = maxBytes;
                byte[] buffer = new byte[8192];
                while (remaining > 0)
                {
                    int read = entryStream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                        break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    remaining -= read;
                }
            }
        }

        private IEnumerable<SaveRuleCheckResult> EvaluateCriticalGameRules(string text)
        {
            List<SaveRuleCheckResult> results = new List<SaveRuleCheckResult>();
            string rulesBlock = SaveRuleUtilities.ExtractBraceBlock(text, "game_rules");
            string rulesSource = String.IsNullOrEmpty(rulesBlock) ? text : rulesBlock;
            List<string> settingsTokens = ExtractGameRuleSettingsTokens(text);

            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                results.Add(EvaluateRule(definition, settingsTokens, rulesSource, text));

            return results;
        }

        private SaveRuleCheckResult EvaluateRule(CriticalSaveRuleDefinition definition, List<string> settingsTokens, string rulesSource, string fullText)
        {
            string raw = ExtractRuleValueFromTokens(settingsTokens, definition.SettingsTokens);
            if (String.IsNullOrEmpty(raw))
                raw = ExtractRuleValue(rulesSource, definition.Keys);
            if (String.IsNullOrEmpty(raw))
                raw = ExtractRuleValue(fullText, definition.Keys);

            SaveRuleCheckResult result = new SaveRuleCheckResult();
            result.Id = definition.Id;
            result.DisplayName = definition.DisplayName;
            result.Expected = definition.Expected;
            result.Actual = raw;
            result.Evidence = String.Join(", ", definition.Keys);
            result.Found = !String.IsNullOrEmpty(raw);
            result.Safe = result.Found && definition.SafeCheck != null && definition.SafeCheck(raw);
            return result;
        }

        private bool RuleValueIsAtMost25(string value)
        {
            int? parsed = SaveRuleUtilities.TryParseIntValue(value);
            return parsed.HasValue && parsed.Value <= 25;
        }

        private static bool RuleValueIsAtMost25Static(string value)
        {
            int? parsed = SaveRuleUtilities.TryParseIntValue(value);
            return parsed.HasValue && parsed.Value <= 25;
        }

        private static bool RuleValueIsNoPlayersToken(string value)
        {
            string normalized = SaveRuleUtilities.NormalizeRuleValue(value).Replace(" ", "_");
            return SaveRuleUtilities.ValueLooksNoPlayers(value)
                || normalized == "no_players_multiplayer_murder_schemes";
        }

        private static bool RuleValueIsSteppeOffToken(string value)
        {
            string normalized = SaveRuleUtilities.NormalizeRuleValue(value);
            return SaveRuleUtilities.ValueLooksDisabled(value)
                || normalized.EndsWith("_off", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("toggle_off");
        }

        private static bool RuleValueIsDisabledToken(string value)
        {
            string normalized = SaveRuleUtilities.NormalizeRuleValue(value);
            return SaveRuleUtilities.ValueLooksDisabled(value)
                || normalized.EndsWith("_disabled", StringComparison.OrdinalIgnoreCase);
        }

        private List<string> ExtractGameRuleSettingsTokens(string text)
        {
            List<string> tokens = new List<string>();
            string body;
            int openIndex;
            int closeIndex;
            if (!TryLocateGameRuleSettingsBody(text, out body, out openIndex, out closeIndex))
                return tokens;

            MatchCollection matches = Regex.Matches(body, "\\S+");
            foreach (Match match in matches)
            {
                string token = match.Value.Trim();
                if (!String.IsNullOrEmpty(token))
                    tokens.Add(token);
            }
            return tokens;
        }

        private string ExtractRuleValueFromTokens(List<string> settingsTokens, string[] allowedTokens)
        {
            if (settingsTokens == null || allowedTokens == null || settingsTokens.Count == 0 || allowedTokens.Length == 0)
                return "";

            foreach (string token in settingsTokens)
                foreach (string allowed in allowedTokens)
                    if (String.Equals(token, allowed, StringComparison.OrdinalIgnoreCase))
                        return token;

            return "";
        }

        private bool TryLocateGameRuleSettingsBody(string text, out string body, out int openIndex, out int closeIndex)
        {
            body = "";
            openIndex = -1;
            closeIndex = -1;
            if (String.IsNullOrEmpty(text))
                return false;

            Match rulesMatch = Regex.Match(text, "(?is)\\bgame_rules\\s*=\\s*\\{");
            if (!rulesMatch.Success)
                return false;

            int rulesOpen = text.IndexOf('{', rulesMatch.Index);
            int rulesClose = FindMatchingBrace(text, rulesOpen);
            if (rulesOpen < 0 || rulesClose <= rulesOpen)
                return false;

            string rulesBody = text.Substring(rulesOpen + 1, rulesClose - rulesOpen - 1);
            Match settingsMatch = Regex.Match(rulesBody, "(?is)\\bsettings\\s*=\\s*\\{");
            if (!settingsMatch.Success)
                return false;

            openIndex = rulesOpen + 1 + settingsMatch.Index + settingsMatch.Length - 1;
            closeIndex = FindMatchingBrace(text, openIndex);
            if (closeIndex <= openIndex)
            {
                openIndex = -1;
                closeIndex = -1;
                return false;
            }

            body = text.Substring(openIndex + 1, closeIndex - openIndex - 1);
            return true;
        }

        private bool EnsureSafeWorkflowHostSave()
        {
            HostSaveCandidateResult best = AnalyzeWorkflowHostSaveCandidate();
            if (best == null || String.IsNullOrEmpty(best.Save.Path) || !File.Exists(best.Save.Path))
            {
                Log("WARN Workflow could not find a local host save to repair.");
                return false;
            }

            if (AllCriticalRulesSafe(best.Save.Rules))
            {
                Log("INFO Workflow host save rules are already on the safe profile: " + best.Save.Path);
                return true;
            }

            string repairedPath;
            List<string> repairedRules;
            string failureReason;
            if (!TryCreateSafeHostSaveCopy(best.Save.Path, out repairedPath, out repairedRules, out failureReason))
            {
                if (!String.IsNullOrEmpty(failureReason))
                    Log("WARN Workflow could not repair host save rules automatically: " + failureReason);
                return false;
            }

            InvalidateHostSaveAnalysisCache();
            workflowSelectedSavePath = repairedPath;
            RefreshWorkflowSaveSelectionList();
            SaveAppConfig();

            HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
            bool repairedSafe = repaired != null && AllCriticalRulesSafe(repaired.Save.Rules);
            string repairedRuleText = repairedRules == null || repairedRules.Count == 0 ? "none" : String.Join(", ", repairedRules);
            if (repairedSafe)
            {
                Log("OK   Created repaired host save copy with safe critical rules: " + repairedPath + " | rules: " + repairedRuleText);
                return true;
            }

            Log("WARN Workflow created a repaired host save copy, but critical rule checks still do not pass: " + repairedPath);
            return false;
        }

        private void InvalidateHostSaveAnalysisCache()
        {
            cachedBestHostSaveCandidate = null;
            cachedBestHostSaveCandidateKey = "";
        }

        private void InvalidateHostSuitabilityCache()
        {
            cachedHostSuitability = null;
            cachedHostSuitabilityKey = "";
            cachedHostSuitabilityUtc = DateTime.MinValue;
        }

        private void PrepareWorkflowSaveSurgeryBaseline()
        {
            try
            {
                if (String.IsNullOrWhiteSpace(workflowSelectedSavePath) || !File.Exists(workflowSelectedSavePath))
                    return;

                OosDeepInsight insight = AnalyzeLatestOosDeepInsight();
                string baselinePath = BuildSaveSurgeryBaselinePath(workflowSelectedSavePath);
                if (!File.Exists(baselinePath)
                    || !String.Equals(FileHashOrMissing(baselinePath), FileHashOrMissing(workflowSelectedSavePath), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(baselinePath))
                        BackupForRestore(baselinePath, "Before CK3MPS refreshes surgery baseline save: " + baselinePath);
                    else
                        RecordCreatedFileForRestore(baselinePath, "CK3MPS surgery baseline save: " + baselinePath);

                    File.Copy(workflowSelectedSavePath, baselinePath, true);
                }

                string reportPath = StabilizerFile("ck3_stabilizer_save_surgery_report.txt");
                WriteTextFileIfMeaningfullyChanged(
                    reportPath,
                    BuildSaveSurgeryReportText(workflowSelectedSavePath, baselinePath, insight),
                    "FILE Save surgery baseline report written: ",
                    "INFO Save surgery baseline report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Save surgery baseline could not be prepared: " + ex.Message);
            }
        }

        private string BuildSaveSurgeryBaselinePath(string sourcePath)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            return Path.Combine(dir, name + "_ck3mps_surgery" + ext);
        }

        private string BuildSaveSurgeryReportText(string sourcePath, string baselinePath, OosDeepInsight insight)
        {
            string saveText;
            string sourceKind;
            bool readable = TryReadSaveAnalysisText(sourcePath, out saveText, out sourceKind);

            int characterMarkers = 0;
            int armyMarkers = 0;
            int aiMarkers = 0;
            if (readable)
            {
                foreach (string sample in insight.CharacterSamples)
                    if (!String.IsNullOrWhiteSpace(sample) && saveText.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                        characterMarkers++;
                foreach (string sample in insight.ArmySamples)
                    if (!String.IsNullOrWhiteSpace(sample) && saveText.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                        armyMarkers++;
                foreach (string sample in insight.AiSamples)
                    if (!String.IsNullOrWhiteSpace(sample) && saveText.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                        aiMarkers++;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 save surgery baseline");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Selected save: " + NullText(sourcePath));
            sb.AppendLine("Baseline copy: " + NullText(baselinePath));
            sb.AppendLine("Selected save hash: " + FileHashOrMissing(sourcePath));
            sb.AppendLine("Baseline hash: " + FileHashOrMissing(baselinePath));
            sb.AppendLine("Readable: " + YesNo(readable));
            sb.AppendLine("Read source: " + NullText(sourceKind));
            sb.AppendLine();
            sb.AppendLine("Deep OOS context");
            sb.AppendLine("- Latest OOS type: " + NullText(insight.OosType));
            sb.AppendLine("- Recovery path: " + insight.RecoveryPath);
            sb.AppendLine("- Session contamination: " + insight.SessionContaminationLevel + " (" + insight.SessionContaminationScore + "/100)");
            sb.AppendLine("- Character samples: " + (insight.CharacterSamples.Count == 0 ? "(none)" : String.Join(", ", insight.CharacterSamples.ToArray())));
            sb.AppendLine("- Modifier samples: " + (insight.ModifierSamples.Count == 0 ? "(none)" : String.Join(", ", insight.ModifierSamples.ToArray())));
            sb.AppendLine("- Army samples: " + (insight.ArmySamples.Count == 0 ? "(none)" : String.Join(", ", insight.ArmySamples.ToArray())));
            sb.AppendLine("- AI samples: " + (insight.AiSamples.Count == 0 ? "(none)" : String.Join(", ", insight.AiSamples.ToArray())));
            sb.AppendLine();
            sb.AppendLine("Baseline markers found inside selected save");
            sb.AppendLine("- Character marker matches: " + characterMarkers);
            sb.AppendLine("- Army marker matches: " + armyMarkers);
            sb.AppendLine("- AI marker matches: " + aiMarkers);
            sb.AppendLine();
            sb.AppendLine("What CK3MPS does now");
            sb.AppendLine("- Creates a frozen baseline copy before any future manual or external save surgery.");
            sb.AppendLine("- Repairs critical safe-start rules when possible.");
            sb.AppendLine("- Reports AI/character/army/modifier markers that likely belong to the incident.");
            sb.AppendLine();
            sb.AppendLine("What CK3MPS does not auto-rewrite yet");
            sb.AppendLine("- It does not automatically rewrite divergent AI/character/army state inside the live save.");
            sb.AppendLine("- Use this baseline together with rollback/rehost guidance if the session is already contaminated.");
            return sb.ToString();
        }

        private bool TryCreateSafeHostSaveCopy(string sourcePath, out string repairedPath, out List<string> repairedRules, out string failureReason)
        {
            repairedPath = "";
            repairedRules = new List<string>();
            failureReason = "";
            if (String.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                failureReason = "source save is missing.";
                return false;
            }

            byte[] sourceBytes;
            try
            {
                sourceBytes = File.ReadAllBytes(sourcePath);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }

            byte[] updatedBytes = null;
            List<string> appliedRules = new List<string>();
            bool changed;
            try
            {
                changed = TryRepairSaveBytes(sourceBytes, out updatedBytes, out appliedRules);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }

            if (!changed || updatedBytes == null || appliedRules.Count == 0)
            {
                failureReason = "no repairable critical rule tokens were found in the save.";
                return false;
            }

            repairedPath = BuildRepairedHostSavePath(sourcePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "");
                if (File.Exists(repairedPath))
                    BackupForRestore(repairedPath, "Before CK3MPS refreshes repaired host save copy: " + repairedPath);
                else
                    RecordCreatedFileForRestore(repairedPath, "CK3MPS repaired host save copy: " + repairedPath);

                File.WriteAllBytes(repairedPath, updatedBytes);
                repairedRules.AddRange(appliedRules);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        private string BuildRepairedHostSavePath(string sourcePath)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            return Path.Combine(dir, name + "_ck3mps_safe" + ext);
        }

        private bool TryRepairSaveBytes(byte[] sourceBytes, out byte[] updatedBytes, out List<string> appliedRules)
        {
            updatedBytes = sourceBytes;
            appliedRules = new List<string>();
            if (sourceBytes == null || sourceBytes.Length == 0)
                return false;

            if (LooksLikeZipArchive(sourceBytes, 0))
            {
                byte[] zipUpdated;
                if (!TryRewriteZipArchiveBytes(sourceBytes, out zipUpdated, out appliedRules))
                    return false;
                updatedBytes = zipUpdated;
                return true;
            }

            int zipOffset = FindEmbeddedZipOffset(sourceBytes);
            if (zipOffset > 0)
            {
                byte[] zipBytes = new byte[sourceBytes.Length - zipOffset];
                Buffer.BlockCopy(sourceBytes, zipOffset, zipBytes, 0, zipBytes.Length);
                byte[] updatedZipBytes;
                List<string> zipRules = new List<string>();
                bool zipChanged = TryRewriteZipArchiveBytes(zipBytes, out updatedZipBytes, out zipRules);
                if (!zipChanged)
                    return false;

                updatedBytes = new byte[zipOffset + updatedZipBytes.Length];
                Buffer.BlockCopy(sourceBytes, 0, updatedBytes, 0, zipOffset);
                Buffer.BlockCopy(updatedZipBytes, 0, updatedBytes, zipOffset, updatedZipBytes.Length);
                MergeAppliedRules(appliedRules, zipRules);
                return appliedRules.Count > 0;
            }

            string updatedText = ApplyCriticalRuleRepairsToText(Encoding.UTF8.GetString(sourceBytes), out appliedRules);
            if (appliedRules.Count == 0)
                return false;

            updatedBytes = Encoding.UTF8.GetBytes(updatedText);
            return true;
        }

        private void MergeAppliedRules(List<string> target, List<string> source)
        {
            if (source == null)
                return;

            foreach (string item in source)
                if (!target.Any(delegate (string existing) { return String.Equals(existing, item, StringComparison.OrdinalIgnoreCase); }))
                    target.Add(item);
        }

        private bool TryRewriteZipArchiveBytes(byte[] zipBytes, out byte[] updatedZipBytes, out List<string> appliedRules)
        {
            updatedZipBytes = zipBytes;
            appliedRules = new List<string>();
            if (zipBytes == null || zipBytes.Length == 0)
                return false;

            using (MemoryStream input = new MemoryStream(zipBytes, false))
            using (ZipArchive sourceArchive = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                bool changed = false;
                using (MemoryStream output = new MemoryStream())
                {
                    using (ZipArchive destinationArchive = new ZipArchive(output, ZipArchiveMode.Create, true))
                    {
                        foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                        {
                            ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                            if (String.Equals(sourceEntry.Name, "meta", StringComparison.OrdinalIgnoreCase)
                                || String.Equals(sourceEntry.Name, "gamestate", StringComparison.OrdinalIgnoreCase))
                            {
                                using (StreamReader reader = new StreamReader(sourceEntry.Open(), Encoding.UTF8, true))
                                {
                                    string originalText = reader.ReadToEnd();
                                    List<string> entryRules;
                                    string updatedText = ApplyCriticalRuleRepairsToText(originalText, out entryRules);
                                    if (entryRules.Count > 0)
                                    {
                                        changed = true;
                                        MergeAppliedRules(appliedRules, entryRules);
                                    }

                                    using (StreamWriter writer = new StreamWriter(destinationEntry.Open(), Utf8NoBom))
                                        writer.Write(updatedText);
                                }
                            }
                            else
                            {
                                using (Stream sourceStream = sourceEntry.Open())
                                using (Stream destinationStream = destinationEntry.Open())
                                    sourceStream.CopyTo(destinationStream);
                            }
                        }
                    }

                    if (!changed)
                        return false;

                    updatedZipBytes = output.ToArray();
                    return true;
                }
            }
        }

        private string ApplyCriticalRuleRepairsToText(string text, out List<string> appliedRules)
        {
            appliedRules = new List<string>();
            string settingsBody;
            int openIndex;
            int closeIndex;
            if (!TryLocateGameRuleSettingsBody(text, out settingsBody, out openIndex, out closeIndex))
                return text;

            List<string> tokens = ExtractGameRuleSettingsTokens(text);
            if (tokens.Count == 0)
                return text;

            bool changed = false;
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                bool hasSafeToken = tokens.Any(delegate (string token) { return String.Equals(token, definition.SafeToken, StringComparison.OrdinalIgnoreCase); });
                bool hadRuleToken = false;
                bool definitionChanged = false;
                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    bool matchesDefinition = definition.SettingsTokens.Any(delegate (string option)
                    {
                        return String.Equals(tokens[i], option, StringComparison.OrdinalIgnoreCase);
                    });
                    if (!matchesDefinition)
                        continue;

                    hadRuleToken = true;
                    if (!String.Equals(tokens[i], definition.SafeToken, StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.RemoveAt(i);
                        changed = true;
                        definitionChanged = true;
                    }
                }

                if ((hadRuleToken || ExtractRuleValue(text, definition.Keys).Length > 0) && !hasSafeToken)
                {
                    tokens.Add(definition.SafeToken);
                    changed = true;
                    definitionChanged = true;
                }

                if (definitionChanged)
                    appliedRules.Add(definition.DisplayName);
            }

            if (!changed)
                return text;

            string updatedBody = " " + String.Join(" ", tokens) + " ";
            return text.Substring(0, openIndex + 1) + updatedBody + text.Substring(closeIndex);
        }

        private bool LooksLikeZipArchive(byte[] bytes, int offset)
        {
            return bytes != null
                && bytes.Length >= offset + 4
                && bytes[offset] == (byte)'P'
                && bytes[offset + 1] == (byte)'K'
                && bytes[offset + 2] == 3
                && bytes[offset + 3] == 4;
        }

        private int FindEmbeddedZipOffset(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8)
                return -1;

            for (int i = 4; i <= bytes.Length - 4; i++)
                if (LooksLikeZipArchive(bytes, i))
                    return i;
            return -1;
        }

        private string ExtractRuleValue(string text, string[] keys)
        {
            if (String.IsNullOrEmpty(text))
                return "";

            foreach (string key in keys)
            {
                Match inline = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"");
                if (inline.Success)
                    return inline.Groups[1].Value.Trim();

                inline = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*([^\\s\\r\\n{}]+)");
                if (inline.Success)
                    return inline.Groups[1].Value.Trim();

                string block = SaveRuleUtilities.ExtractBraceBlock(text, key);
                if (!String.IsNullOrEmpty(block))
                {
                    Match selected = Regex.Match(block, "(?im)^\\s*(value|selected|option|setting)\\s*=\\s*\"?([^\\r\\n\"{}]+)\"?");
                    if (selected.Success)
                        return selected.Groups[2].Value.Trim();

                    Match firstToken = Regex.Match(block, "\"([^\"]+)\"");
                    if (firstToken.Success)
                        return firstToken.Groups[1].Value.Trim();
                }
            }

            return "";
        }

        private string ExtractSaveStructuredValue(string text, string key)
        {
            if (String.IsNullOrEmpty(text))
                return "";

            Match quoted = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"");
            if (quoted.Success)
                return quoted.Groups[1].Value.Trim();

            Match raw = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*([^\\s\\r\\n{}]+)");
            return raw.Success ? raw.Groups[1].Value.Trim() : "";
        }

        private void WriteHostSavePreparationReport()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_host_save_preparation.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildHostSavePreparationReportText(),
                    "FILE Host save preparation report written: ",
                    "INFO Host save preparation report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Host save preparation report could not be written: " + ex.Message);
            }
        }

        private string BuildHostSavePreparationReportText()
        {
            HostSaveCandidateResult best = AnalyzeWorkflowHostSaveCandidate();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 host save preparation");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Verdict: " + best.Verdict);
            sb.AppendLine("Score: " + best.Score + "/100");
            sb.AppendLine("Host save: " + NullText(Path.GetFileName(best.Save.Path)));
            sb.AppendLine("Path: " + NullText(best.Save.Path));
            sb.AppendLine("Hash: " + FileHashOrMissing(best.Save.Path));
            sb.AppendLine("Readable: " + YesNo(best.Save.Readable));
            sb.AppendLine("Read source: " + NullText(best.Save.SourceKind));
            sb.AppendLine("Version: " + NullText(best.Save.Version));
            sb.AppendLine("Version matches installed CK3: " + YesNo(best.Save.VersionMatchesInstalled));
            sb.AppendLine("Save date: " + NullText(best.Save.Date));
            sb.AppendLine("Player: " + NullText(best.Save.Player));
            sb.AppendLine("Title: " + NullText(best.Save.Title));
            sb.AppendLine("Suspicious save name: " + YesNo(best.Save.SuspiciousName));
            sb.AppendLine();
            sb.AppendLine("Critical in-game rules");
            foreach (SaveRuleCheckResult rule in best.Save.Rules)
            {
                string state = !rule.Found ? "UNKNOWN" : (rule.Safe ? "SAFE" : "UNSAFE");
                sb.AppendLine("- " + rule.DisplayName + ": " + state + " | actual=" + NullText(rule.Actual) + " | expected=" + rule.Expected);
            }
            sb.AppendLine();
            sb.AppendLine("Strengths");
            if (best.Strengths.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string line in best.Strengths)
                    sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("Issues");
            if (best.Issues.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string line in best.Issues)
                    sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("Recommended host flow");
            sb.AppendLine("- Use Load Game, not Resume/Continue.");
            sb.AppendLine("- Host from the save above only if the score and critical rules are acceptable.");
            sb.AppendLine("- Create a fresh lobby, wait for every player, then unpause.");
            return sb.ToString();
        }

        private void WriteHostSuitabilityReport()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_host_suitability.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildHostSuitabilityReportText(),
                    "FILE Host suitability report written: ",
                    "INFO Host suitability report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Host suitability report could not be written: " + ex.Message);
            }
        }

        private string BuildHostSuitabilityReportText()
        {
            HostSuitabilityResult result = AnalyzeHostSuitability();
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 host suitability");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Verdict: " + result.Level);
            sb.AppendLine("Suitable for hosting now: " + YesNo(result.Suitable));
            sb.AppendLine("Score: " + result.Score + "/100");
            sb.AppendLine();
            sb.AppendLine("Local computer and network factors");
            sb.AppendLine("- Active gateway routes: " + profile.GatewayAdapters);
            sb.AppendLine("- Wi-Fi routes: " + profile.WifiRoutes);
            sb.AppendLine("- VPN routes: " + profile.VpnRoutes);
            sb.AppendLine("- Mobile routes: " + profile.MobileRoutes);
            sb.AppendLine("- Packet loss: " + profile.PacketLossPercent + "%");
            sb.AppendLine("- Max jitter: " + profile.MaxJitterMs + "ms");
            sb.AppendLine("- Proxy detected: " + YesNo(profile.ProxyDetected));
            sb.AppendLine("- Windows power saver: " + YesNo(PowerSaverPlanActive()));
            sb.AppendLine("- Network services healthy: " + YesNo(RequiredWindowsNetworkServicesOk()));
            sb.AppendLine("- Steam/Paradox services reachable: " + YesNo(OnlineServicesOk()));
            sb.AppendLine("- Steam branch: " + NullText(DetectSteamBranch()));
            sb.AppendLine("- Stable launch options: " + YesNo(HasNoAsync() && !HasRiskyLaunchOptions()));
            sb.AppendLine("- No active mods: " + YesNo(NoActiveMods()));
            sb.AppendLine("- No disabled DLCs: " + YesNo(NoDisabledDlcs()));
            sb.AppendLine();
            sb.AppendLine("Strengths");
            if (result.Strengths.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string line in result.Strengths)
                    sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("Risks");
            if (result.Risks.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string line in result.Risks)
                    sb.AppendLine("- " + line);
            return sb.ToString();
        }

        private HostSuitabilityResult AnalyzeHostSuitability()
        {
            string cacheKey = BuildHostSuitabilityCacheKey();
            if (cachedHostSuitability != null
                && String.Equals(cachedHostSuitabilityKey, cacheKey, StringComparison.Ordinal)
                && (DateTime.UtcNow - cachedHostSuitabilityUtc).TotalSeconds <= 30)
                return cachedHostSuitability;

            HostSuitabilityResult result = new HostSuitabilityResult();
            result.Score = 100;
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);

            if (!HasAnyActiveNetworkRoute())
            {
                result.Score -= 40;
                result.Risks.Add("No active IPv4 gateway route is available.");
            }
            else
            {
                result.Strengths.Add("At least one active network route is available.");
            }

            if (!RequiredWindowsNetworkServicesOk())
            {
                result.Score -= 12;
                result.Risks.Add("Core Windows networking services are not fully healthy.");
            }
            else
            {
                result.Strengths.Add("Core Windows networking services are healthy.");
            }

            if (!OnlineServicesOk())
            {
                result.Score -= 18;
                result.Risks.Add("Steam or Paradox services are not reliably reachable.");
            }
            else
            {
                result.Strengths.Add("Steam or Paradox services are reachable.");
            }

            if (profile.HasMultipleGateways)
            {
                result.Score -= 12;
                result.Risks.Add("Multiple simultaneous gateway routes detected.");
            }
            else if (profile.GatewayAdapters == 1)
            {
                result.Strengths.Add("Single active gateway route detected.");
            }

            if (profile.HasWifi)
            {
                result.Score -= 10;
                result.Risks.Add("Wi-Fi is active on the route; Ethernet is better for hosting.");
            }
            else if (profile.PhysicalRoutes > 0)
            {
                result.Strengths.Add("Physical non-Wi-Fi route is available.");
            }

            if (profile.HasVpn)
            {
                result.Score -= 14;
                result.Risks.Add("VPN or virtual route is active.");
            }
            if (profile.HasMobile)
            {
                result.Score -= 18;
                result.Risks.Add("Mobile or tethering route is active.");
            }
            if (profile.HasPppoe)
            {
                result.Score -= 5;
                result.Risks.Add("PPPoE route detected; keep provider MTU stable.");
            }
            if (profile.HasLowSpeed)
            {
                result.Score -= 8;
                result.Risks.Add("Low-speed active route detected.");
            }
            if (profile.ProxyDetected)
            {
                result.Score -= 10;
                result.Risks.Add("Windows proxy is active.");
            }
            if (profile.PacketLossPercent > 0)
            {
                result.Score -= Math.Min(18, profile.PacketLossPercent * 4);
                result.Risks.Add("Packet loss detected: " + profile.PacketLossPercent + "%.");
            }
            if (profile.MaxJitterMs > 45)
            {
                result.Score -= Math.Min(16, (int)((profile.MaxJitterMs - 45) / 5) + 4);
                result.Risks.Add("High jitter detected: " + profile.MaxJitterMs + "ms.");
            }

            if (PowerSaverPlanActive())
            {
                result.Score -= 8;
                result.Risks.Add("Windows power saver plan is active.");
            }
            else
            {
                result.Strengths.Add("Windows power saver plan is not active.");
            }

            if (!String.Equals(DetectSteamBranch(), "public", StringComparison.OrdinalIgnoreCase))
            {
                result.Score -= 10;
                result.Risks.Add("Steam branch is not public/default.");
            }
            else
            {
                result.Strengths.Add("Steam branch is public/default.");
            }

            if (!HasNoAsync() || HasRiskyLaunchOptions())
            {
                result.Score -= 10;
                result.Risks.Add("Launch options are not on the stable MP profile.");
            }
            else
            {
                result.Strengths.Add("Launch options are stable for multiplayer.");
            }

            if (!NoActiveMods())
            {
                result.Score -= 12;
                result.Risks.Add("Active mods are still enabled.");
            }
            if (!NoDisabledDlcs())
            {
                result.Score -= 8;
                result.Risks.Add("Disabled DLC mismatch is still possible.");
            }

            HostSaveCandidateResult hostSave = AnalyzeWorkflowHostSaveCandidate();
            if (hostSave.Score < 70)
            {
                result.Score -= 8;
                result.Risks.Add("Current workflow host save is not strongly recommended.");
            }
            else
            {
                result.Strengths.Add("A workable workflow host save exists.");
            }

            if (result.Score < 0)
                result.Score = 0;

            if (result.Score >= 85)
            {
                result.Level = "EXCELLENT";
                result.Suitable = true;
            }
            else if (result.Score >= 70)
            {
                result.Level = "GOOD";
                result.Suitable = true;
            }
            else if (result.Score >= 55)
            {
                result.Level = "CAUTION";
                result.Suitable = false;
            }
            else
            {
                result.Level = "POOR";
                result.Suitable = false;
            }

            cachedHostSuitability = result;
            cachedHostSuitabilityKey = cacheKey;
            cachedHostSuitabilityUtc = DateTime.UtcNow;
            return result;
        }

        private string BuildHostSuitabilityCacheKey()
        {
            return String.Join("|",
                NullText(ck3Docs),
                NullText(stabilizerRoot),
                DetectSteamBranch(),
                YesNo(HasNoAsync()),
                YesNo(HasRiskyLaunchOptions()),
                YesNo(NoActiveMods()),
                YesNo(NoDisabledDlcs()),
                YesNo(StableCriticalSettingsOk()),
                BuildEffectiveWorkflowHostSaveCacheKey());
        }

        private string BuildEffectiveWorkflowHostSaveCacheKey()
        {
            try
            {
                FileInfo info = new FileInfo(workflowSelectedSavePath ?? "");
                return "manual|" + NullText(workflowSelectedSavePath) + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch
            {
                return "manual|" + NullText(workflowSelectedSavePath);
            }
        }

        private void WriteRehostPack()
        {
            EnsureStabilizerRoot();
            WriteHostSuitabilityReport();
            WriteHostSavePreparationReport();
            WriteCleanSaveLaunchNote();
            WritePreSessionPlan();
            WriteRuntimeVerificationReport();
            WriteSessionVerdictReport();
            WriteMultiplayerParityManifest();
            WriteOosRiskScoreReport();
            AnalyzeLatestOosReport();
            WriteOosEvidencePack();
            WriteWorkflowStatusReport();

            string exportDir = Path.Combine(stabilizerRoot, "rehost_pack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(exportDir);

            CopyIfExists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"), Path.Combine(exportDir, "latest_oos_summary.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_latest_oos_deep_report.txt"), Path.Combine(exportDir, "latest_oos_deep_report.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_oos_history.txt"), Path.Combine(exportDir, "oos_history.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_session_contamination_score.txt"), Path.Combine(exportDir, "session_contamination_score.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_recovery_runbook.txt"), Path.Combine(exportDir, "recovery_runbook.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_incident_state.txt"), Path.Combine(exportDir, "incident_state.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"), Path.Combine(exportDir, "mp_parity_manifest.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt"), Path.Combine(exportDir, "oos_risk_score.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_host_suitability.txt"), Path.Combine(exportDir, "host_suitability.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_host_save_preparation.txt"), Path.Combine(exportDir, "host_save_preparation.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_save_surgery_report.txt"), Path.Combine(exportDir, "save_surgery_report.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_workflow_status.txt"), Path.Combine(exportDir, "workflow_status.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_runtime_verification.txt"), Path.Combine(exportDir, "runtime_verification.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_pre_session_plan.txt"), Path.Combine(exportDir, "pre_session_plan.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_clean_save_note.txt"), Path.Combine(exportDir, "clean_save_note.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt"), Path.Combine(exportDir, "evidence_pack_index.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_session_verdict.txt"), Path.Combine(exportDir, "session_verdict.txt"));

            File.WriteAllText(Path.Combine(exportDir, "rehost_pack_index.txt"), BuildRehostPackIndexText(exportDir), Encoding.UTF8);
            workflowLastRehostPackPath = exportDir;
            workflowIncidentStatus = "rehost_pack_created";
            SaveAppConfig();
            RecordIncidentHistoryEvent("rehost_pack", AnalyzeOosIncidentState(), "Rehost pack exported");
            Log("FILE Rehost pack exported: " + exportDir);
            if (!IsAutomationTestRun())
                Process.Start("explorer.exe", exportDir);
        }

        private string BuildRehostPackIndexText(string exportDir)
        {
            HostSuitabilityResult host = AnalyzeHostSuitability();
            HostSaveCandidateResult save = AnalyzeWorkflowHostSaveCandidate();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 rehost pack");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Folder: " + exportDir);
            sb.AppendLine();
            sb.AppendLine("Summary");
            sb.AppendLine("- Host suitability: " + host.Level + " (" + host.Score + "/100)");
            sb.AppendLine("- Host save: " + NullText(Path.GetFileName(save.Save.Path)));
            sb.AppendLine("- Host save verdict: " + save.Verdict + " (" + save.Score + "/100)");
            sb.AppendLine("- Local parity fingerprint: " + BuildLocalParityFingerprint());
            sb.AppendLine("- Steam branch: " + NullText(DetectSteamBranch()));
            sb.AppendLine("- Latest OOS metadata: " + NullText(FindLatestOosMetadataFile()));
            sb.AppendLine();
            sb.AppendLine("Files");
            foreach (string name in Directory.GetFiles(exportDir))
                sb.AppendLine("- " + Path.GetFileName(name));
            return sb.ToString();
        }
    }
}
