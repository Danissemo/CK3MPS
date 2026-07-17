using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private static readonly bool NetworkRecommendationsRuntimeHooksInstalled = InstallNetworkRecommendationsRuntimeHooks();
        private Button networkRecommendationsButton;
        private string lastLiveLogWarnDedupeKey = "";
        private DateTime lastLiveLogWarnDedupeUtc = DateTime.MinValue;
        private bool readinessParentConsistencyApplied;

        private static bool InstallNetworkRecommendationsRuntimeHooks()
        {
            Application.Idle += delegate
            {
                foreach (Form form in Application.OpenForms)
                {
                    MainForm main = form as MainForm;
                    if (main == null)
                        continue;
                    main.ConfigureNetworkRecommendationsRuntimeFix();
                    main.ApplyParentReadinessConsistencyWhenIdle();
                    main.DedupeVisibleLiveLogWarningsWhenIdle();
                    main.DedupeLiveLogWarningsWhenIdle();
                }
            };
            return true;
        }

        private void ConfigureNetworkRecommendationsRuntimeFix()
        {
            if (mainPage == null || stabilizeButton == null)
                return;

            if (networkRecommendationsButton == null)
            {
                networkRecommendationsButton = new Button();
                networkRecommendationsButton.Text = "Network Recommendations";
                networkRecommendationsButton.Click += delegate { ShowNetworkRecommendations(); };
                mainPage.Controls.Add(networkRecommendationsButton);
            }

            networkRecommendationsButton.Size = new System.Drawing.Size(190, stabilizeButton.Height);
            networkRecommendationsButton.Location = new System.Drawing.Point(stabilizeButton.Right + 8, stabilizeButton.Top);
            networkRecommendationsButton.Anchor = stabilizeButton.Anchor;
            networkRecommendationsButton.Enabled = !busyUi && HasReusableFreshCheckOnlyScan();
            networkRecommendationsButton.BringToFront();
        }

        private void ShowNetworkRecommendations()
        {
            try
            {
                if (!HasReusableFreshCheckOnlyScan())
                {
                    MessageBox.Show("Run Scan Settings first. Network Recommendations are based on the latest Scan Settings result.", "CK3MPS Network Recommendations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string text = BuildNetworkRecommendationsText();
                string path = StabilizerFile("ck3_stabilizer_network_recommendations.txt");
                if (!readOnlyScanMode)
                    SafeAtomicFile.WriteAllText(path, text, Utf8NoBom);
                Log("INFO Network Recommendations opened: " + path);
                DialogResult result = MessageBox.Show(text + "\r\n\r\nOpen the Main tab so you can adjust the Network/Windows checklist or switch to the Network only preset?", "CK3MPS Network Recommendations", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                {
                    mainTabs.SelectedTab = mainPage;
                    presetBox.Focus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Network Recommendations failed.\r\n\r\n" + ex.Message, "CK3MPS Network Recommendations", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Network Recommendations failed: " + ex.Message);
            }
        }

        private string BuildNetworkRecommendationsText()
        {
            NetworkRecommendationProfile profile = AnalyzeNetworkRecommendationsStrict();
            List<string> scanSignals = ExtractNetworkSignalsFromLatestScan();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS Network Recommendations");
            sb.AppendLine("Based on latest Scan Settings result");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Scan Settings network signals");
            if (scanSignals.Count == 0)
                sb.AppendLine("- No network WARN lines were found in the latest Scan Settings report.");
            foreach (string line in scanSignals)
                sb.AppendLine("- " + StripStatusPrefix(line));
            sb.AppendLine();
            sb.AppendLine("Detected active routes now");
            sb.AppendLine("- Gateway routes: " + profile.GatewayRoutes);
            sb.AppendLine("- Ethernet/physical routes: " + profile.PhysicalRoutes);
            sb.AppendLine("- Wi-Fi routes: " + profile.WifiRoutes);
            sb.AppendLine("- VPN/virtual routes: " + profile.VpnRoutes);
            sb.AppendLine("- Strict mobile/tethering routes: " + profile.StrictMobileRoutes);
            sb.AppendLine("- Possible mobile false-positive routes: " + profile.PossibleMobileFalsePositiveRoutes);
            sb.AppendLine("- Low-speed routes: " + profile.LowSpeedRoutes);
            sb.AppendLine();
            sb.AppendLine("Adapters with active IPv4 gateway");
            if (profile.AdapterLines.Count == 0)
                sb.AppendLine("- No active gateway adapters were detected.");
            foreach (string line in profile.AdapterLines)
                sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("Concrete actions");
            if (profile.GatewayRoutes == 0)
            {
                sb.AppendLine("- Connect to the internet before hosting or joining CK3 MP.");
            }
            else
            {
                if (profile.GatewayRoutes > 1)
                    sb.AppendLine("- Leave only one intentional route enabled during CK3. Prefer Ethernet; disable unused Wi-Fi/VPN/mobile adapters before hosting.");
                if (profile.VpnRoutes > 0)
                    sb.AppendLine("- Disable VPN/virtual routes for testing, or make every player intentionally use the same VPN/route policy.");
                if (profile.StrictMobileRoutes > 0)
                    sb.AppendLine("- Do not host from phone tethering/4G/5G/WWAN/RNDIS if avoidable. Use a normal home router or Ethernet instead.");
                if (profile.PossibleMobileFalsePositiveRoutes > 0 && profile.StrictMobileRoutes == 0)
                    sb.AppendLine("- The old mobile/tethering warning is probably a false positive: only generic USB Ethernet was found, without phone/WWAN/RNDIS/LTE/5G markers.");
                if (profile.WifiRoutes > 0 && profile.PhysicalRoutes == 0)
                    sb.AppendLine("- Wi-Fi can work, but Ethernet is preferred for the host. Keep signal strong and avoid downloads/streams.");
                if (profile.LowSpeedRoutes > 0)
                    sb.AppendLine("- Check cable/router/adapter speed; low link speed can add jitter and packet loss.");
                if (profile.GatewayRoutes == 1 && profile.PhysicalRoutes == 1 && profile.VpnRoutes == 0 && profile.StrictMobileRoutes == 0 && profile.WifiRoutes == 0)
                    sb.AppendLine("- Network route looks good for CK3 hosting: one physical Ethernet route and no VPN/mobile route.");
            }
            sb.AppendLine();
            sb.AppendLine("Where to change things");
            sb.AppendLine("- In CK3MPS: Main tab -> Windows and Network Settings, or preset: Network only.");
            sb.AppendLine("- In Windows: Settings -> Network & internet -> Advanced network settings -> disable unused Wi-Fi/VPN/mobile adapters.");
            sb.AppendLine("- For Discord: Discord Settings -> Game Overlay -> disable overlay for CK3 if OOS persists.");
            return sb.ToString();
        }

        private List<string> ExtractNetworkSignalsFromLatestScan()
        {
            List<string> lines = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in (lastCheckOnlyReportText ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0)
                    continue;
                if (line.IndexOf("route", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("tether", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("jitter", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string key = CollapseWhitespace(line);
                if (seen.Add(key))
                    lines.Add(line);
            }
            return lines;
        }

        private NetworkRecommendationProfile AnalyzeNetworkRecommendationsStrict()
        {
            NetworkRecommendationProfile profile = new NetworkRecommendationProfile();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (!AdapterHasIpv4Gateway(ni))
                    continue;

                string combined = (ni.Name + " " + ni.Description).Trim();
                string lower = combined.ToLowerInvariant();
                long speedMbps = ni.Speed > 0 ? ni.Speed / 1000000 : 0;
                bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                bool vpn = IsVirtualAdapter(ni);
                bool strictMobile = IsStrictMobileOrTetherAdapter(ni);
                bool genericUsbEthernet = lower.Contains("usb ethernet") && !strictMobile;
                bool lowSpeed = speedMbps > 0 && speedMbps < 100;

                profile.GatewayRoutes++;
                if (vpn)
                    profile.VpnRoutes++;
                else if (strictMobile)
                    profile.StrictMobileRoutes++;
                else if (wifi)
                    profile.WifiRoutes++;
                else
                    profile.PhysicalRoutes++;
                if (genericUsbEthernet)
                    profile.PossibleMobileFalsePositiveRoutes++;
                if (lowSpeed)
                    profile.LowSpeedRoutes++;

                profile.AdapterLines.Add(ni.Name + " [" + ni.NetworkInterfaceType + ", " + speedMbps + "Mbps" + (vpn ? ", VPN/virtual" : "") + (wifi ? ", Wi-Fi" : "") + (strictMobile ? ", strict mobile/tethering" : "") + (genericUsbEthernet ? ", generic USB Ethernet - possible false positive" : "") + "] " + ni.Description);
            }
            profile.AdapterLines.Sort(StringComparer.OrdinalIgnoreCase);
            return profile;
        }

        private bool IsStrictMobileOrTetherAdapter(NetworkInterface ni)
        {
            string lower = (ni.Name + " " + ni.Description).ToLowerInvariant();
            int type = (int)ni.NetworkInterfaceType;
            return type == 243
                || type == 244
                || lower.Contains("wwan")
                || lower.Contains("lte")
                || lower.Contains("5g")
                || lower.Contains("4g")
                || lower.Contains("mobile")
                || lower.Contains("cellular")
                || lower.Contains("tether")
                || lower.Contains("android")
                || lower.Contains("iphone")
                || lower.Contains("rndis");
        }

        private void ApplyParentReadinessConsistencyWhenIdle()
        {
            try
            {
                if (busyUi || readinessParentConsistencyApplied)
                    return;
                string[] snapshot = SnapshotRunLogLines();
                if (snapshot == null || snapshot.Length == 0)
                    return;

                HashSet<string> failedSections = DetectSectionsWithSubFails(snapshot);
                if (failedSections.Count == 0)
                    return;

                int corrections = 0;
                lock (runLogSync)
                {
                    for (int i = 0; i < runLogLines.Count; i++)
                    {
                        string line = runLogLines[i] ?? "";
                        if (!StartsWithStatus(line, "OK"))
                            continue;
                        string message = NormalizeStatusMessage(line);
                        if (failedSections.Contains(message))
                        {
                            runLogLines[i] = ReplaceLeadingStatus(line, "FAIL ");
                            corrections++;
                        }
                    }
                }

                if (corrections <= 0)
                    return;

                readinessParentConsistencyApplied = true;
                lastReadinessFailures = Math.Max(lastReadinessFailures, corrections);
                ReplaceVisibleLogText(delegate(string text)
                {
                    string updated = text;
                    foreach (string section in failedSections)
                        updated = ReplaceVisibleOkStatusForSection(updated, section);
                    updated = updated.Replace("OK   | Final readiness summary | all checklist checks passed", "FAIL | Final readiness summary | parent checklist failed checks: " + corrections);
                    updated = updated.Replace("RESULT| READY.", "RESULT| NOT READY. Failed parent checklist checks: " + corrections);
                    updated = updated.Replace("RESULT READY.", "RESULT NOT READY. Failed parent checklist checks: " + corrections);
                    return updated;
                });
                string[] correctedSnapshot = SnapshotRunLogLines();
                scanSettingsExportReportText = BuildCheckOnlyReportText(lastReadinessFailures, correctedSnapshot);
                lastCheckOnlyReportText = scanSettingsExportReportText;
            }
            catch
            {
            }
        }

        private HashSet<string> DetectSectionsWithSubFails(string[] lines)
        {
            HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentSection = "";
            bool previousDivider = false;
            foreach (string raw in lines ?? new string[0])
            {
                string line = StripUiPrefix(raw ?? "").Trim();
                if (IsDividerLine(line))
                {
                    previousDivider = true;
                    continue;
                }
                if (previousDivider && line.Length > 0 && !IsStatusLine(line))
                {
                    currentSection = NormalizeSectionName(line);
                    previousDivider = false;
                    continue;
                }
                previousDivider = false;
                if (StartsWithStatus(line, "FAIL") && currentSection.Length > 0)
                    failed.Add(currentSection);
            }
            return failed;
        }

        private void DedupeVisibleLiveLogWarningsWhenIdle()
        {
            try
            {
                if (logBox == null || busyUi || readOnlyScanMode)
                    return;
                ReplaceVisibleLogText(DedupeAndFilterWarnText);
            }
            catch
            {
            }
        }

        private void DedupeLiveLogWarningsWhenIdle()
        {
            try
            {
                if (readOnlyScanMode || busyUi || String.IsNullOrEmpty(liveLogFilePath) || !File.Exists(liveLogFilePath))
                    return;
                DateTime writeUtc = File.GetLastWriteTimeUtc(liveLogFilePath);
                if ((DateTime.UtcNow - lastLiveLogWarnDedupeUtc).TotalSeconds < 2 && String.Equals(lastLiveLogWarnDedupeKey, liveLogFilePath + "|" + writeUtc.Ticks, StringComparison.Ordinal))
                    return;

                string original = ReadTextShared(liveLogFilePath);
                if (String.IsNullOrEmpty(original))
                    return;

                string deduped = DedupeAndFilterWarnText(original);
                lastLiveLogWarnDedupeKey = liveLogFilePath + "|" + writeUtc.Ticks;
                lastLiveLogWarnDedupeUtc = DateTime.UtcNow;
                if (String.Equals(original, deduped, StringComparison.Ordinal))
                    return;

                SafeAtomicFile.WriteAllText(liveLogFilePath, deduped, Utf8NoBom);
                lastLiveLogWarnDedupeKey = liveLogFilePath + "|" + File.GetLastWriteTimeUtc(liveLogFilePath).Ticks;
            }
            catch
            {
            }
        }

        private string DedupeAndFilterWarnText(string text)
        {
            NetworkRecommendationProfile profile = AnalyzeNetworkRecommendationsStrict();
            string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            HashSet<string> seenWarns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StringBuilder sb = new StringBuilder();
            bool changed = false;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.IndexOf("Mobile/tethering route detected", StringComparison.OrdinalIgnoreCase) >= 0 && profile.StrictMobileRoutes == 0)
                    {
                        changed = true;
                        continue;
                    }

                    string key = CollapseWhitespace(trimmed);
                    if (seenWarns.Contains(key))
                    {
                        changed = true;
                        continue;
                    }
                    seenWarns.Add(key);
                }
                sb.AppendLine(line);
            }
            return changed ? sb.ToString() : text;
        }

        private void ReplaceVisibleLogText(Func<string, string> transform)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { ReplaceVisibleLogText(transform); });
                return;
            }
            string original = logBox.Text;
            string updated = transform(original);
            if (String.Equals(original, updated, StringComparison.Ordinal))
                return;
            int selectionStart = logBox.SelectionStart;
            logBox.Text = updated;
            logBox.SelectionStart = Math.Min(selectionStart, logBox.TextLength);
            ScrollLogToBottom(false);
        }

        private string ReplaceVisibleOkStatusForSection(string text, string section)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n' }, StringSplitOptions.None);
            StringBuilder sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (StartsWithStatus(line, "OK") && String.Equals(NormalizeStatusMessage(line), section, StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(ReplaceLeadingStatus(line, "FAIL "));
                else
                    sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private string StripStatusPrefix(string line)
        {
            string trimmed = (line ?? "").Trim();
            int pipe = trimmed.IndexOf('|');
            if (pipe >= 0)
                return trimmed.Substring(pipe + 1).Trim();
            if (trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return trimmed.Length > 5 ? trimmed.Substring(5).Trim() : trimmed;
            return trimmed;
        }

        private bool IsStatusLine(string line)
        {
            return StartsWithStatus(line, "OK") || StartsWithStatus(line, "FAIL") || StartsWithStatus(line, "WARN") || StartsWithStatus(line, "INFO") || StartsWithStatus(line, "RESULT");
        }

        private bool StartsWithStatus(string line, string status)
        {
            string stripped = StripUiPrefix(line ?? "").TrimStart();
            return stripped.StartsWith(status, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeStatusMessage(string line)
        {
            return NormalizeSectionName(StripStatusPrefix(StripUiPrefix(line ?? "")));
        }

        private string NormalizeSectionName(string value)
        {
            string text = CollapseWhitespace(value ?? "").Trim().TrimEnd('.');
            return text;
        }

        private string StripUiPrefix(string line)
        {
            string text = line ?? "";
            int pipe = text.IndexOf('|');
            if (pipe > 0)
            {
                string before = text.Substring(0, pipe).Trim();
                if (before.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                    || before.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                    || before.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                    || before.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                    || before.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase))
                    return text;
            }
            return text;
        }

        private string ReplaceLeadingStatus(string line, string replacementStatus)
        {
            string text = line ?? "";
            int index = text.IndexOf("OK", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return text;
            return text.Substring(0, index) + replacementStatus + text.Substring(index + 2);
        }

        private bool IsDividerLine(string line)
        {
            string text = (line ?? "").Trim();
            if (text.Length < 8)
                return false;
            foreach (char c in text)
                if (c != '-')
                    return false;
            return true;
        }

        private string CollapseWhitespace(string value)
        {
            StringBuilder sb = new StringBuilder();
            bool wasSpace = false;
            foreach (char c in value ?? "")
            {
                if (Char.IsWhiteSpace(c))
                {
                    if (!wasSpace)
                        sb.Append(' ');
                    wasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    wasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private sealed class NetworkRecommendationProfile
        {
            public int GatewayRoutes;
            public int PhysicalRoutes;
            public int WifiRoutes;
            public int VpnRoutes;
            public int StrictMobileRoutes;
            public int PossibleMobileFalsePositiveRoutes;
            public int LowSpeedRoutes;
            public readonly List<string> AdapterLines = new List<string>();
        }
    }
}