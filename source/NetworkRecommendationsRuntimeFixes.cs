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
            networkRecommendationsButton.Enabled = !busyUi && HasReusableFreshCheckOnlyScan() && !String.IsNullOrWhiteSpace(lastCheckOnlyReportText);
            networkRecommendationsButton.BringToFront();
        }

        private void ShowNetworkRecommendations()
        {
            try
            {
                if (!HasReusableFreshCheckOnlyScan() || String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                {
                    MessageBox.Show("Run Scan Settings first. Network Recommendations are based on the latest Scan Settings result.", "CK3MPS Network Recommendations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string text = BuildNetworkRecommendationsText();
                string path = StabilizerFile("ck3_stabilizer_network_recommendations.txt");
                if (!readOnlyScanMode)
                    SafeAtomicFile.WriteAllText(path, text, Utf8NoBom);
                Log("INFO Network Recommendations opened: " + path);

                DialogResult result = MessageBox.Show(
                    text + "\r\n\r\nSwitch CK3MPS to the Network only preset now?",
                    "CK3MPS Network Recommendations",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                    ApplyNetworkRecommendationsQuickAction();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Network Recommendations failed.\r\n\r\n" + ex.Message, "CK3MPS Network Recommendations", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Network Recommendations failed: " + ex.Message);
            }
        }

        private void ApplyNetworkRecommendationsQuickAction()
        {
            mainTabs.SelectedTab = mainPage;
            if (presetBox.Items.Contains("Network only"))
                presetBox.SelectedItem = "Network only";
            ApplyPreset("Network only");
            SetStatusText("Network only preset selected. Review Windows and Network Settings, then run Apply Settings if needed.");
            stabilizeButton.Focus();
        }

        private string BuildNetworkRecommendationsText()
        {
            NetworkRecommendationProfile profile = AnalyzeNetworkRecommendationsStrict();
            List<string> actions = BuildConcreteNetworkActions(profile);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Network Recommendations");
            sb.AppendLine();
            if (actions.Count == 0)
            {
                sb.AppendLine("No required network changes were detected in the latest Scan Settings result.");
                sb.AppendLine("Current route looks acceptable for CK3 MP hosting.");
            }
            else
            {
                int index = 1;
                foreach (string action in actions)
                {
                    sb.AppendLine(index + ". " + action);
                    index++;
                }
            }
            sb.AppendLine();
            sb.AppendLine("Detected route: " + BuildShortRouteSummary(profile));
            sb.AppendLine("Button action: Yes = select Network only preset on Main tab.");
            return sb.ToString();
        }

        private List<string> BuildConcreteNetworkActions(NetworkRecommendationProfile profile)
        {
            List<string> actions = new List<string>();
            string scanText = lastCheckOnlyReportText ?? "";
            bool scanWarnedMultipleRoutes = ContainsAny(scanText, "Multiple active gateway routes", "Multiple active routes");
            bool scanWarnedVpn = ContainsAny(scanText, "VPN/virtual route", "VPN/virtual adapter");
            bool scanWarnedWifi = ContainsAny(scanText, "Wi-Fi route");
            bool scanWarnedMobile = ContainsAny(scanText, "Mobile/tethering route");
            bool scanWarnedProxy = ContainsAny(scanText, "Windows proxy");
            bool scanWarnedDiscord = ContainsAny(scanText, "Overlay/background app running: Discord");
            bool scanWarnedJitter = ContainsAny(scanText, "jitter", "Packet loss");

            if (scanWarnedMultipleRoutes || profile.GatewayRoutes > 1)
                actions.Add("Leave only one active internet route before CK3. Prefer Ethernet; disable unused Wi-Fi/VPN adapters in Windows Network settings.");
            if (scanWarnedVpn || profile.VpnRoutes > 0)
                actions.Add("Disable VPN/virtual adapters for testing, unless every player intentionally uses the same VPN/route policy.");
            if (scanWarnedMobile && profile.StrictMobileRoutes == 0)
                actions.Add("Ignore the old mobile/tethering warning: strict scan sees no phone/WWAN/RNDIS/LTE/4G/5G route. This is treated as a false positive.");
            else if (profile.StrictMobileRoutes > 0)
                actions.Add("Do not host from phone tethering/4G/5G/WWAN/RNDIS. Use normal home Ethernet/router instead.");
            if (scanWarnedWifi || profile.WifiRoutes > 0)
                actions.Add("For hosting, use Ethernet instead of Wi-Fi if possible.");
            if (scanWarnedProxy)
                actions.Add("Disable Windows proxy while testing Steam/Paradox lobby/auth issues.");
            if (scanWarnedDiscord)
                actions.Add("Disable Discord Game Overlay for CK3 if OOS persists. You can keep Discord voice chat running.");
            if (scanWarnedJitter || profile.LowSpeedRoutes > 0)
                actions.Add("Stop downloads/streams and check cable/router speed before serious MP.");

            return actions;
        }

        private string BuildShortRouteSummary(NetworkRecommendationProfile profile)
        {
            if (profile.AdapterLines.Count == 0)
                return "no active IPv4 gateway adapter";
            return "gateways=" + profile.GatewayRoutes
                + ", ethernet=" + profile.PhysicalRoutes
                + ", wifi=" + profile.WifiRoutes
                + ", vpn=" + profile.VpnRoutes
                + ", strict_mobile=" + profile.StrictMobileRoutes
                + "; " + String.Join("; ", profile.AdapterLines.ToArray());
        }

        private bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
                if ((text ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
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
                if (lowSpeed)
                    profile.LowSpeedRoutes++;

                profile.AdapterLines.Add(ni.Name + " [" + ni.NetworkInterfaceType + ", " + speedMbps + "Mbps" + (vpn ? ", VPN/virtual" : "") + (wifi ? ", Wi-Fi" : "") + (strictMobile ? ", mobile/tethering" : "") + "]");
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

        private sealed class NetworkRecommendationProfile
        {
            public int GatewayRoutes;
            public int PhysicalRoutes;
            public int WifiRoutes;
            public int VpnRoutes;
            public int StrictMobileRoutes;
            public int LowSpeedRoutes;
            public readonly List<string> AdapterLines = new List<string>();
        }
    }
}
