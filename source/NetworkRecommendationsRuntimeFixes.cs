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
        private bool networkRecommendationsUiApplied;
        private string lastLiveLogWarnDedupeKey = "";
        private DateTime lastLiveLogWarnDedupeUtc = DateTime.MinValue;

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
                    main.DedupeLiveLogWarningsWhenIdle();
                }
            };
            return true;
        }

        private void ConfigureNetworkRecommendationsRuntimeFix()
        {
            if (networkRecommendationsUiApplied)
                return;
            if (mainPage == null || openReportsButton == null)
                return;

            networkRecommendationsUiApplied = true;
            networkRecommendationsButton = new Button();
            networkRecommendationsButton.Text = "Network Recommendations";
            networkRecommendationsButton.Size = new System.Drawing.Size(184, openReportsButton.Height);
            networkRecommendationsButton.Location = new System.Drawing.Point(openReportsButton.Right + 8, openReportsButton.Top);
            networkRecommendationsButton.Anchor = openReportsButton.Anchor;
            networkRecommendationsButton.Enabled = !busyUi;
            networkRecommendationsButton.Click += delegate { ShowNetworkRecommendations(); };
            mainPage.Controls.Add(networkRecommendationsButton);
            networkRecommendationsButton.BringToFront();
        }

        private void ShowNetworkRecommendations()
        {
            try
            {
                string text = BuildNetworkRecommendationsText();
                string path = StabilizerFile("ck3_stabilizer_network_recommendations.txt");
                if (!readOnlyScanMode)
                    SafeAtomicFile.WriteAllText(path, text, Utf8NoBom);
                Log("INFO Network Recommendations opened" + (readOnlyScanMode ? " (scan mode: report not written)." : ": " + path));
                MessageBox.Show(text, "CK3MPS Network Recommendations", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS Network Recommendations");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Detected active routes");
            sb.AppendLine("- Gateway routes: " + profile.GatewayRoutes);
            sb.AppendLine("- Ethernet/physical routes: " + profile.PhysicalRoutes);
            sb.AppendLine("- Wi-Fi routes: " + profile.WifiRoutes);
            sb.AppendLine("- VPN/virtual routes: " + profile.VpnRoutes);
            sb.AppendLine("- Strict mobile/tethering routes: " + profile.StrictMobileRoutes);
            sb.AppendLine("- Possible mobile false-positive routes: " + profile.PossibleMobileFalsePositiveRoutes);
            sb.AppendLine("- Low-speed routes: " + profile.LowSpeedRoutes);
            sb.AppendLine();
            sb.AppendLine("Adapters");
            if (profile.AdapterLines.Count == 0)
                sb.AppendLine("- No active gateway adapters were detected.");
            foreach (string line in profile.AdapterLines)
                sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("Recommended actions");
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
                    sb.AppendLine("- Mobile/tethering warning may be a false positive. A generic USB Ethernet adapter was found, but no strict phone/WWAN/RNDIS/LTE/5G marker was detected.");
                if (profile.WifiRoutes > 0 && profile.PhysicalRoutes == 0)
                    sb.AppendLine("- Wi-Fi can work, but Ethernet is preferred for the host. Keep signal strong and avoid downloads/streams.");
                if (profile.LowSpeedRoutes > 0)
                    sb.AppendLine("- Check cable/router/adapter speed; low link speed can add jitter and packet loss.");
                if (profile.GatewayRoutes == 1 && profile.PhysicalRoutes == 1 && profile.VpnRoutes == 0 && profile.StrictMobileRoutes == 0 && profile.WifiRoutes == 0)
                    sb.AppendLine("- Network route looks good for CK3 hosting: one physical Ethernet route and no VPN/mobile route.");
            }
            sb.AppendLine();
            sb.AppendLine("What CK3MPS should treat as mobile/tethering");
            sb.AppendLine("- Strong markers: WWAN, LTE, 4G, 5G, Cellular, Mobile, Tether, Android, iPhone, RNDIS.");
            sb.AppendLine("- Generic USB Ethernet alone is not enough to classify a route as mobile/tethering.");
            return sb.ToString();
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

                string deduped = DedupeRepeatedWarnLines(original);
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

        private string DedupeRepeatedWarnLines(string text)
        {
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
                    string key = RegexCollapseWhitespace(trimmed);
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

        private string RegexCollapseWhitespace(string value)
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
