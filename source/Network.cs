using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void FlushDnsCache()
        {
            RunCommand("ipconfig.exe", "/flushdns", false);
        }

        private void RunNetworkDiagnostics()
        {
            string path = StabilizerFile("ck3_stabilizer_network_diagnostics.txt");
            string snapshot = BuildNetworkDiagnosticsSnapshotText();
            WriteTextFileIfMeaningfullyChanged(
                path,
                snapshot,
                "FILE Network diagnostics snapshot written: ",
                "INFO Network diagnostics snapshot already up to date: ",
                true);
            LogTextSnapshot("Adaptive network diagnostics", snapshot);
        }

        private string BuildNetworkDiagnosticsSnapshotText()
        {
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS adaptive network diagnostics");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("ROUTE SUMMARY");
            sb.AppendLine("INFO up_adapters=" + profile.UpAdapters + " gateway_adapters=" + profile.GatewayAdapters + " ipv6_gateways=" + profile.Ipv6GatewayAdapters + " physical_routes=" + profile.PhysicalRoutes + " vpn_routes=" + profile.VpnRoutes + " wifi_routes=" + profile.WifiRoutes + " pppoe_routes=" + profile.PppoeRoutes + " mobile_routes=" + profile.MobileRoutes + " low_speed_routes=" + profile.LowSpeedRoutes);
            sb.AppendLine("INFO private_ipv4=" + profile.PrivateIpv4Addresses + " cgnat_ipv4=" + profile.CgnatAddresses + " local_dns=" + profile.LocalDnsServers + " public_dns=" + profile.PublicDnsServers + " proxy=" + (profile.ProxyDetected ? "yes" : "no"));
            foreach (string line in BuildAdaptiveNetworkPlanLines(profile))
                sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("QUALITY BASELINE");
            sb.AppendLine("INFO packet_loss=" + profile.PacketLossPercent + "% avg_ping=" + profile.AveragePingMs + "ms max_jitter=" + profile.MaxJitterMs + "ms");
            sb.AppendLine();
            sb.AppendLine("TCP/IP PROFILE");
            string tcp = RunCommandQuiet("netsh.exe", "interface tcp show global");
            foreach (string line in BuildTcpGlobalSummaryLines(tcp))
                sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("MTU PROFILE");
            string mtu = RunCommandQuiet("netsh.exe", "interface ipv4 show subinterfaces");
            foreach (string line in SplitCommandOutputLines(mtu))
                sb.AppendLine("INFO " + line);
            return sb.ToString();
        }

        private NetworkRouteProfile AnalyzeNetworkRouteProfile(bool logAdapters)
        {
            NetworkRouteProfile profile = new NetworkRouteProfile();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                profile.UpAdapters++;
                string type = ni.NetworkInterfaceType.ToString();
                long speedMbps = ni.Speed > 0 ? ni.Speed / 1000000 : 0;
                bool virtualAdapter = IsVirtualAdapter(ni);
                bool pppoeAdapter = IsPppoeAdapter(ni);
                bool mobileAdapter = IsMobileAdapter(ni);
                bool hasGateway = AdapterHasIpv4Gateway(ni);
                bool hasIpv6Gateway = AdapterHasIpv6Gateway(ni);
                bool wireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                if (hasIpv6Gateway)
                    profile.Ipv6GatewayAdapters++;
                AddAdapterAddressSignals(profile, ni);
                AddAdapterDnsSignals(profile, ni);
                if (hasGateway)
                {
                    profile.GatewayAdapters++;
                    profile.RouteNames.Add(ni.Name + " [" + ni.NetworkInterfaceType + (virtualAdapter ? ", virtual" : "") + (pppoeAdapter ? ", PPPoE" : "") + (mobileAdapter ? ", mobile" : "") + "]");
                    if (virtualAdapter)
                        profile.VpnRoutes++;
                    else if (pppoeAdapter)
                        profile.PppoeRoutes++;
                    else if (mobileAdapter)
                        profile.MobileRoutes++;
                    else
                        profile.PhysicalRoutes++;
                    if (wireless)
                        profile.WifiRoutes++;
                    if (speedMbps > 0 && speedMbps < 100)
                        profile.LowSpeedRoutes++;
                }

                if (logAdapters)
                {
                    Log("ADAPTER " + ni.Name);
                    Log("  type=" + type + " speed=" + speedMbps + "Mbps route=" + (hasGateway ? "yes" : "no") + " ipv6_route=" + (hasIpv6Gateway ? "yes" : "no") + " virtual=" + (virtualAdapter ? "yes" : "no") + " pppoe=" + (pppoeAdapter ? "yes" : "no") + " mobile=" + (mobileAdapter ? "yes" : "no"));
                    LogAdapterIpProfile(ni);

                    if (virtualAdapter)
                        Log("WARN  VPN/virtual adapter is active: use it only when every player uses the same route.");
                    if (pppoeAdapter && hasGateway)
                        Log("INFO PPPoE route detected. Stabilizer will not force MTU/offload changes for this adapter.");
                    if (mobileAdapter && hasGateway)
                        Log("WARN  Mobile/tethering route detected. Expect higher jitter and possible CGNAT.");
                    if (wireless && hasGateway)
                        Log("WARN  Wi-Fi route detected: stable CK3 hosting is usually better on Ethernet.");
                    if (speedMbps > 0 && speedMbps < 100 && hasGateway)
                        Log("WARN  Low link speed on active route.");

                    try
                    {
                        foreach (GatewayIPAddressInformation gw in ni.GetIPProperties().GatewayAddresses)
                        {
                            string address = gw.Address.ToString();
                            if (!String.IsNullOrEmpty(address) && address != "0.0.0.0" && !address.Contains(":"))
                                PingAndLog("gateway " + address, address);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("WARN  Gateway check failed for " + ni.Name + ": " + ex.Message);
                    }
                }
            }

            profile.ProxyDetected = ProxyDetected();
            ApplyPingQualityProbe(profile);
            return profile;
        }

        private bool IsVirtualAdapter(NetworkInterface ni)
        {
            string lower = (ni.Name + " " + ni.Description).ToLowerInvariant();
            return lower.Contains("vpn")
                || lower.Contains("wireguard")
                || lower.Contains("tap")
                || lower.Contains("tun")
                || lower.Contains("tailscale")
                || lower.Contains("zerotier")
                || lower.Contains("hamachi")
                || lower.Contains("radmin")
                || lower.Contains("virtual")
                || lower.Contains("hyper-v")
                || lower.Contains("vmware")
                || lower.Contains("virtualbox");
        }

        private bool IsPppoeAdapter(NetworkInterface ni)
        {
            string lower = (ni.Name + " " + ni.Description).ToLowerInvariant();
            return ni.NetworkInterfaceType == NetworkInterfaceType.Ppp
                || lower.Contains("pppoe")
                || lower.Contains("wan miniport")
                || lower.Contains("broadband")
                || lower.Contains("ras async");
        }

        private bool IsMobileAdapter(NetworkInterface ni)
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
                || lower.Contains("rndis")
                || lower.Contains("usb ethernet");
        }

        private bool AdapterHasIpv6Gateway(NetworkInterface ni)
        {
            try
            {
                foreach (GatewayIPAddressInformation gw in ni.GetIPProperties().GatewayAddresses)
                {
                    string address = gw.Address.ToString();
                    if (!String.IsNullOrEmpty(address) && address.Contains(":") && address != "::")
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void AddAdapterAddressSignals(NetworkRouteProfile profile, NetworkInterface ni)
        {
            try
            {
                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    string value = ip.Address.ToString();
                    if (value.Contains(":"))
                        continue;
                    if (IsCgnatIpv4(value))
                        profile.CgnatAddresses++;
                    if (IsPrivateIpv4(value))
                        profile.PrivateIpv4Addresses++;
                }
            }
            catch { }
        }

        private void AddAdapterDnsSignals(NetworkRouteProfile profile, NetworkInterface ni)
        {
            try
            {
                foreach (System.Net.IPAddress dns in ni.GetIPProperties().DnsAddresses)
                {
                    string value = dns.ToString();
                    if (value.Contains(":"))
                        continue;
                    if (IsPrivateIpv4(value) || value.StartsWith("127.", StringComparison.Ordinal))
                        profile.LocalDnsServers++;
                    else
                        profile.PublicDnsServers++;
                }
            }
            catch { }
        }

        private bool IsCgnatIpv4(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
                return false;
            int a;
            int b;
            if (!Int32.TryParse(parts[0], out a) || !Int32.TryParse(parts[1], out b))
                return false;
            return a == 100 && b >= 64 && b <= 127;
        }

        private bool IsPrivateIpv4(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
                return false;
            int a;
            int b;
            if (!Int32.TryParse(parts[0], out a) || !Int32.TryParse(parts[1], out b))
                return false;
            return a == 10
                || (a == 172 && b >= 16 && b <= 31)
                || (a == 192 && b == 168)
                || (a == 169 && b == 254)
                || IsCgnatIpv4(ip);
        }

        private bool ProxyDetected()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    if (key != null)
                    {
                        object enabled = key.GetValue("ProxyEnable");
                        object server = key.GetValue("ProxyServer");
                        if (enabled != null && Convert.ToInt32(enabled) != 0 && server != null && server.ToString().Length > 0)
                            return true;
                    }
                }
            }
            catch { }

            string winHttp = RunCommandQuiet("netsh.exe", "winhttp show proxy");
            return winHttp.IndexOf("Direct access", StringComparison.OrdinalIgnoreCase) < 0
                && winHttp.IndexOf("no proxy", StringComparison.OrdinalIgnoreCase) < 0
                && winHttp.IndexOf("Proxy Server", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyPingQualityProbe(NetworkRouteProfile profile)
        {
            List<long> times = new List<long>();
            int sent = 8;
            int ok = 0;
            try
            {
                using (Ping ping = new Ping())
                {
                    for (int i = 0; i < sent; i++)
                    {
                        PingReply reply = ping.Send("1.1.1.1", 900);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            ok++;
                            times.Add(reply.RoundtripTime);
                        }
                    }
                }
            }
            catch { }

            profile.PacketLossPercent = sent == 0 ? 0 : (int)Math.Round(((sent - ok) * 100.0) / sent);
            if (times.Count == 0)
                return;

            long total = 0;
            foreach (long t in times)
                total += t;
            profile.AveragePingMs = total / times.Count;

            long maxJitter = 0;
            for (int i = 1; i < times.Count; i++)
            {
                long diff = Math.Abs(times[i] - times[i - 1]);
                if (diff > maxJitter)
                    maxJitter = diff;
            }
            profile.MaxJitterMs = maxJitter;
        }

        private void LogAdaptiveNetworkPlan(NetworkRouteProfile profile)
        {
            if (profile.GatewayAdapters == 0)
                Log("FAIL No active IPv4 gateway found.");
            if (profile.HasIpv6OnlyOrDsLiteSignal)
                Log("WARN IPv6-only/DS-Lite-like route detected. CK3 may work, but Steam/Paradox relay/NAT behavior can differ by ISP.");
            if (profile.HasMultipleGateways)
                Log("WARN Multiple active routes found. Stabilizer will not rewrite routes; use one intentional route during CK3.");
            if (profile.HasVpn)
                Log("WARN VPN/virtual route detected. All players should intentionally use the same route policy.");
            if (profile.HasWifi)
                Log("WARN Wi-Fi route detected. Stabilizer applies power stability only; Ethernet is still preferred for hosting.");
            if (profile.HasPppoe)
                Log("INFO PPPoE route detected. Stabilizer keeps provider MTU/offload untouched and uses conservative TCP settings.");
            if (profile.HasMobile)
                Log("WARN Mobile/tethering route detected. Expect higher jitter, CGNAT, and changing NAT state.");
            if (profile.HasCgnatSignal)
                Log("WARN CGNAT-like 100.64.0.0/10 address detected. Prefer Steam/Paradox relay and avoid direct-port assumptions.");
            if (profile.HasDnsFilteringSignal)
                Log("INFO Local DNS/filtering detected. If Paradox/Steam auth fails, test without DNS filter/AdGuard/Pi-hole temporarily.");
            if (profile.ProxyDetected)
                Log("WARN Windows proxy detected. Proxy/VPN policy can affect Paradox auth and Steam networking.");
            if (profile.HasJitterOrLoss)
                Log("WARN Packet loss/jitter detected: loss=" + profile.PacketLossPercent + "% max_jitter=" + profile.MaxJitterMs + "ms.");
            if (profile.HasLowSpeed)
                Log("WARN Low link speed route detected. Check cable/Wi-Fi signal/router before serious MP.");
            if (profile.GatewayAdapters == 1 && !profile.HasWifi && !profile.HasVpn && !profile.HasPppoe && !profile.HasMobile && !profile.HasLowSpeed && !profile.ProxyDetected && !profile.HasJitterOrLoss)
                Log("OK   Network route profile: single stable physical route.");
        }

        private bool AdapterHasIpv4Gateway(NetworkInterface ni)
        {
            try
            {
                foreach (GatewayIPAddressInformation gw in ni.GetIPProperties().GatewayAddresses)
                {
                    string address = gw.Address.ToString();
                    if (!String.IsNullOrEmpty(address) && address != "0.0.0.0" && !address.Contains(":"))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void LogAdapterIpProfile(NetworkInterface ni)
        {
            try
            {
                IPInterfaceProperties props = ni.GetIPProperties();
                StringBuilder dns = new StringBuilder();
                foreach (System.Net.IPAddress ip in props.DnsAddresses)
                {
                    string value = ip.ToString();
                    if (value.Contains(":"))
                        continue;
                    if (dns.Length > 0)
                        dns.Append(", ");
                    dns.Append(value);
                }
                Log("  dns=" + (dns.Length == 0 ? "(none)" : dns.ToString()));

                foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                {
                    string value = ua.Address.ToString();
                    if (!value.Contains(":"))
                        Log("  ipv4=" + value);
                }
            }
            catch (Exception ex)
            {
                Log("WARN  Could not read adapter IP profile: " + ex.Message);
            }
        }

        private void LogTcpGlobalSummary(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                Log("WARN  TCP global profile was not returned by Windows.");
                return;
            }

            LogTcpLineIfPresent(text, "Receive-Side Scaling");
            LogTcpLineIfPresent(text, "Receive Window Auto-Tuning");
            LogTcpLineIfPresent(text, "ECN Capability");
            LogTcpLineIfPresent(text, "RFC 1323 Timestamps");
            LogTcpLineIfPresent(text, "Initial RTO");

            string rssLine = FindTcpLine(text, "Receive-Side Scaling");
            if (rssLine.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                Log("WARN  RSS may be disabled. Stabilize can set it back to enabled.");
        }

        private string FindTcpLine(string text, string key)
        {
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return line;
            }
            return "";
        }

        private void LogTcpLineIfPresent(string text, string key)
        {
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    Log("  " + line);
            }
        }

        private void EnsureFirewallRules()
        {
            string exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");
            if (!File.Exists(exe))
            {
                Log("ck3.exe not found, firewall step skipped.");
                return;
            }

            if (!IsAdministrator())
            {
                Log("Not running as administrator. Firewall rules were not changed.");
                Log("Run this program as administrator once if CK3 hosting/joining still has connection issues.");
                return;
            }

            string quotedExe = "\"" + exe + "\"";
            StringBuilder firewallBefore = new StringBuilder();
            firewallBefore.AppendLine(RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3 Stabilizer - CK3 Inbound\""));
            firewallBefore.AppendLine(RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3 Stabilizer - CK3 Outbound\""));
            firewallBefore.AppendLine(RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3MPS - CK3 Inbound\""));
            firewallBefore.AppendLine(RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3MPS - CK3 Outbound\""));
            RecordSystemSnapshot("Firewall rules before CK3MPS firewall step", "netsh advfirewall firewall show CK3MPS/legacy CK3 rules", firewallBefore.ToString());
            EnsureFirewallRuleMatches("CK3MPS - CK3 Inbound", "in", quotedExe);
            EnsureFirewallRuleMatches("CK3MPS - CK3 Outbound", "out", quotedExe);
            DeleteFirewallRuleIfPresent("CK3 Stabilizer - CK3 Inbound");
            DeleteFirewallRuleIfPresent("CK3 Stabilizer - CK3 Outbound");
        }

        private void EnsureFirewallRuleMatches(string ruleName, string direction, string quotedExe)
        {
            string current = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"" + ruleName + "\"");
            if (FirewallRuleOutputLooksPresent(current) && FirewallRuleMatches(current, quotedExe, direction))
            {
                Log("OK   Firewall rule already matches: " + ruleName);
                return;
            }

            if (FirewallRuleOutputLooksPresent(current))
            {
                RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"" + ruleName + "\"", true);
                Log("INFO Firewall rule refreshed: " + ruleName);
            }

            RunCommand("netsh.exe", "advfirewall firewall add rule name=\"" + ruleName + "\" dir=" + direction + " action=allow program=" + quotedExe + " enable=yes profile=any", true);
        }

        private void DeleteFirewallRuleIfPresent(string ruleName)
        {
            string current = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"" + ruleName + "\"");
            if (!FirewallRuleOutputLooksPresent(current))
                return;
            RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"" + ruleName + "\"", true);
        }

        private bool FirewallRuleMatches(string output, string quotedExe, string direction)
        {
            if (!FirewallRuleOutputLooksPresent(output))
                return false;

            string normalizedExe = quotedExe.Trim('"');
            return output.IndexOf(normalizedExe, StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("Direction:", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf(direction.Equals("in", StringComparison.OrdinalIgnoreCase) ? "In" : "Out", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("Action:", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CheckOverlaysAndVpn()
        {
            string path = StabilizerFile("ck3_stabilizer_overlay_scan.txt");
            string snapshot = BuildOverlayAndVpnSnapshotText();
            WriteTextFileIfMeaningfullyChanged(
                path,
                snapshot,
                "FILE Overlay/background snapshot written: ",
                "INFO Overlay/background snapshot already up to date: ",
                true);
            LogTextSnapshot("Overlay, VPN and background scan", snapshot);
        }

        private string BuildOverlayAndVpnSnapshotText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS overlay and background scan");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("PROCESS OVERLAYS");

            List<string> found = new List<string>();
            string[] watch = new[]
            {
                "Discord", "obs", "GameBar", "GameBarFTServer", "NVIDIA Share", "NVIDIA Overlay",
                "RTSS", "MSIAfterburner", "Overwolf", "Medal", "SteelSeries", "Razer", "Nahimic"
            };

            foreach (Process p in Process.GetProcesses())
            {
                string name = "";
                try { name = p.ProcessName; } catch { continue; }
                foreach (string needle in watch)
                {
                    if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found.Add(name);
                        break;
                    }
                }
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0)
                sb.AppendLine("INFO No watched overlay/background apps are running.");
            else
                foreach (string name in found)
                    sb.AppendLine("WARN Overlay/background app running: " + name + ". If OOS persists, disable its overlay for CK3.");

            sb.AppendLine();
            sb.AppendLine("WINDOWS SERVICE HYGIENE");
            foreach (string line in BuildWindowsServiceHygieneLines())
                sb.AppendLine(line);

            string power = RunCommandQuiet("powercfg.exe", "/getactivescheme");
            sb.AppendLine();
            sb.AppendLine("POWER PLAN");
            foreach (string line in SplitCommandOutputLines(power))
                sb.AppendLine("INFO " + line);
            if (power.IndexOf("power saver", StringComparison.OrdinalIgnoreCase) >= 0 || power.IndexOf("econom", StringComparison.OrdinalIgnoreCase) >= 0)
                sb.AppendLine("WARN Power Saver plan detected. Use Balanced/High performance for hosting.");
            return sb.ToString();
        }

        private void CheckWindowsServiceHygiene()
        {
            Log("INFO Windows service hygiene scan");
            LogServiceState("Steam Client Service", "Steam Client Service", false);
            LogServiceState("Windows Firewall", "MpsSvc", true);
            LogServiceState("Base Filtering Engine", "BFE", true);
            LogServiceState("DNS Client", "Dnscache", true);
            LogServiceState("Network Location Awareness", "NlaSvc", true);
            LogServiceState("Windows Time", "W32Time", true);
            LogServiceState("VPN Remote Access", "RasMan", false);
            LogServiceState("VPN SSTP", "SstpSvc", false);
            LogServiceState("WinHTTP Auto Proxy", "WinHttpAutoProxySvc", false);
            LogServiceState("EA Background Service", "EABackgroundService", false);
            LogServiceState("ASUS fan control", "AsusFanControlService", false);
        }

        private List<string> BuildWindowsServiceHygieneLines()
        {
            List<string> lines = new List<string>();
            lines.Add(BuildServiceStateLine("Steam Client Service", "Steam Client Service", false));
            lines.Add(BuildServiceStateLine("Windows Firewall", "MpsSvc", true));
            lines.Add(BuildServiceStateLine("Base Filtering Engine", "BFE", true));
            lines.Add(BuildServiceStateLine("DNS Client", "Dnscache", true));
            lines.Add(BuildServiceStateLine("Network Location Awareness", "NlaSvc", true));
            lines.Add(BuildServiceStateLine("Windows Time", "W32Time", true));
            lines.Add(BuildServiceStateLine("VPN Remote Access", "RasMan", false));
            lines.Add(BuildServiceStateLine("VPN SSTP", "SstpSvc", false));
            lines.Add(BuildServiceStateLine("WinHTTP Auto Proxy", "WinHttpAutoProxySvc", false));
            lines.Add(BuildServiceStateLine("EA Background Service", "EABackgroundService", false));
            lines.Add(BuildServiceStateLine("ASUS fan control", "AsusFanControlService", false));
            return lines;
        }

        private string BuildServiceStateLine(string label, string serviceName, bool shouldRun)
        {
            string output = RunCommandQuiet("sc.exe", "query \"" + serviceName + "\"");
            if (String.IsNullOrEmpty(output) || output.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
                return "INFO " + label + ": not installed";

            bool running = output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            if (shouldRun && !running)
                return "WARN " + label + ": not running";
            return "INFO " + label + ": " + (running ? "running" : "not running");
        }

        private bool RequiredWindowsNetworkServicesOk()
        {
            return ServiceRunningQuiet("MpsSvc")
                && ServiceRunningQuiet("BFE")
                && ServiceRunningQuiet("Dnscache")
                && ServiceRunningQuiet("NlaSvc");
        }

        private bool PowerSaverPlanActive()
        {
            string power = RunCommandQuiet("powercfg.exe", "/getactivescheme");
            return power.IndexOf("power saver", StringComparison.OrdinalIgnoreCase) >= 0
                || power.IndexOf("econom", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool WindowsAppsAndServicesOk()
        {
            return RequiredWindowsNetworkServicesOk() && !PowerSaverPlanActive();
        }

        private void LogServiceState(string label, string serviceName, bool shouldRun)
        {
            string output = RunCommandQuiet("sc.exe", "query \"" + serviceName + "\"");
            if (String.IsNullOrEmpty(output) || output.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log("INFO " + label + ": not installed");
                return;
            }

            bool running = output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            if (shouldRun && !running)
                Log("WARN " + label + ": not running");
            else
                Log("INFO " + label + ": " + (running ? "running" : "not running"));
        }

        private void CheckOnlineServices()
        {
            string path = StabilizerFile("ck3_stabilizer_online_services.txt");
            string snapshot = BuildOnlineServicesSnapshotText();
            WriteTextFileIfMeaningfullyChanged(
                path,
                snapshot,
                "FILE Online services snapshot written: ",
                "INFO Online services snapshot already up to date: ",
                true);
            LogTextSnapshot("Online services reachability", snapshot);
        }

        private string BuildOnlineServicesSnapshotText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS online services reachability");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine(BuildTcpStatusLine("Paradox API", "api.paradox-interactive.com", 443));
            sb.AppendLine(BuildTcpStatusLine("Paradox accounts", "accounts.paradoxplaza.com", 443));
            sb.AppendLine(BuildTcpStatusLine("Steam store", "store.steampowered.com", 443));
            sb.AppendLine(BuildTcpStatusLine("Steam community", "steamcommunity.com", 443));
            return sb.ToString();
        }

        private string BuildTcpStatusLine(string label, string host, int port)
        {
            return "INFO TCP " + label + " " + host + ":" + port + " " + (TcpOk(host, port) ? "OK" : "FAILED");
        }

        private IEnumerable<string> BuildAdaptiveNetworkPlanLines(NetworkRouteProfile profile)
        {
            List<string> lines = new List<string>();
            if (profile.HasMultipleGateways)
                lines.Add("WARN Multiple active gateway routes detected. Disable unused Ethernet/Wi-Fi/VPN routes during CK3 if possible.");
            if (profile.HasWifi)
                lines.Add("WARN Wi-Fi route detected. Hosting is usually more stable on Ethernet.");
            if (profile.HasVpn)
                lines.Add("WARN VPN/virtual route detected. Every player should intentionally use the same route policy.");
            if (profile.HasPppoe)
                lines.Add("INFO PPPoE route detected. CK3MPS keeps MTU/offload changes conservative.");
            if (profile.HasMobile)
                lines.Add("WARN Mobile/tethering route detected. Expect higher jitter and possible CGNAT.");
            if (profile.HasLowSpeed)
                lines.Add("WARN Low-speed route detected. Keep heavy downloads/streams off during CK3.");
            if (profile.ProxyDetected)
                lines.Add("WARN Windows proxy is active. Disable it for testing if auth or lobby access is unstable.");
            if (profile.HasDnsFilteringSignal)
                lines.Add("INFO Local/filtering DNS is present. This is fine if intentional, but can affect reachability diagnostics.");
            if (profile.HasIpv6OnlyOrDsLiteSignal)
                lines.Add("INFO IPv6-only / DS-Lite signal detected. CK3MPS avoids aggressive MTU changes here.");
            if (lines.Count == 0)
                lines.Add("INFO Single clean route profile detected.");
            return lines;
        }

        private IEnumerable<string> BuildTcpGlobalSummaryLines(string tcp)
        {
            List<string> lines = new List<string>();
            foreach (string line in SplitCommandOutputLines(tcp))
                lines.Add("INFO " + line);
            if (lines.Count == 0)
                lines.Add("WARN TCP global settings could not be read.");
            return lines;
        }

        private IEnumerable<string> SplitCommandOutputLines(string text)
        {
            List<string> lines = new List<string>();
            foreach (string line in (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                lines.Add(line.Trim());
            return lines;
        }

        private void CheckSaveHygiene()
        {
            string continuePath = Path.Combine(ck3Docs, "continue_game.json");
            if (File.Exists(continuePath))
            {
                string text = File.ReadAllText(continuePath);
                Log("continue_game.json:");
                foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    Log("  " + line.Trim());

                string installedVersion = DetectInstalledVersion();
                string activeSaveVersion = DetectActiveSaveVersion();
                if (!String.IsNullOrEmpty(installedVersion) && !String.IsNullOrEmpty(activeSaveVersion)
                    && !String.Equals(installedVersion, activeSaveVersion, StringComparison.OrdinalIgnoreCase))
                    Log("Warning: active continue save version differs from installed CK3 version.");
            }
            else
            {
                Log("continue_game.json not found.");
            }

            string saveDir = Path.Combine(ck3Docs, "save games");
            if (Directory.Exists(saveDir))
            {
                FileInfo[] saves = new DirectoryInfo(saveDir).GetFiles("*.ck3");
                Array.Sort(saves, delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                Log("Local CK3 saves: " + saves.Length);
                int limit = Math.Min(5, saves.Length);
                for (int i = 0; i < limit; i++)
                    Log("  Save " + (i + 1) + ": " + saves[i].Name + " | " + saves[i].LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

                foreach (FileInfo save in saves)
                {
                    if (IsSuspiciousSaveName(save.Name))
                        Log("Warning: recovery/patched/desync save still visible in save list: " + save.Name);
                }

                Log("INFO Suspicious save-name count: " + CountSuspiciousSaveNames());
            }

            Log("INFO Steam Cloud remote save files: " + CountSteamCloudSaveFiles());
            Log("INFO Steam Cloud remote suspicious saves: " + CountSuspiciousSteamCloudSaveNames());
            Log("INFO Steam Cloud remote fingerprint: " + BuildSteamCloudSaveFingerprint());
        }

    }
}



