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
    internal sealed partial class MainForm : Form
    {
        private const string AppVersion = "v0.1-beta";
        private const int ExpectedStepCount = 28;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly CheckedListBox steps = new CheckedListBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly TextBox logBox = new TextBox();
        private readonly ComboBox presetBox = new ComboBox();
        private readonly ComboBox graphicsProfileBox = new ComboBox();
        private readonly Button stabilizeButton = new Button();
        private readonly Button checkButton = new Button();
        private readonly Button openFolderButton = new Button();
        private readonly Button openReportsButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button selectNoneButton = new Button();
        private readonly Label statusLabel = new Label();
        private readonly Timer settingsGuardTimer = new Timer();

        private readonly string ck3Docs;
        private readonly string stabilizerRoot;
        private readonly string steamRoot;
        private readonly string ck3Install;
        private readonly string ck3Bin;
        private readonly string appManifest;
        private readonly string localConfig;
        private readonly string sharedConfig;

        private string lastQuarantine;
        private DateTime lastSettingsGuardRepairUtc = DateTime.MinValue;
        private bool settingsGuardActive;
        private int lastReadinessFailures;

        private readonly string[] suspectBinaryFiles = new[]
        {
            "Emulator64.dll",
            "LinkNeverDie_Com_64.dll",
            "Switcher Spacewar.exe",
            "SWconfig.ini",
            "steam_appid.txt",
            "steam_api64_org_game.dll",
            "steam_api64_org_launcher.dll"
        };

        private sealed class NetworkRouteProfile
        {
            // Snapshot of the active route shape. Network fixes must adapt to the user's route
            // instead of forcing one global TCP/IP profile on Ethernet, Wi-Fi, VPN, PPPoE, or mobile.
            public int UpAdapters;
            public int GatewayAdapters;
            public int Ipv6GatewayAdapters;
            public int PhysicalRoutes;
            public int VpnRoutes;
            public int WifiRoutes;
            public int PppoeRoutes;
            public int MobileRoutes;
            public int LowSpeedRoutes;
            public int LocalDnsServers;
            public int PublicDnsServers;
            public int CgnatAddresses;
            public int PrivateIpv4Addresses;
            public int PacketLossPercent;
            public long AveragePingMs;
            public long MaxJitterMs;
            public bool ProxyDetected;
            public readonly List<string> RouteNames = new List<string>();

            public bool HasMultipleGateways { get { return GatewayAdapters > 1; } }
            public bool HasVpn { get { return VpnRoutes > 0; } }
            public bool HasWifi { get { return WifiRoutes > 0; } }
            public bool HasPppoe { get { return PppoeRoutes > 0; } }
            public bool HasMobile { get { return MobileRoutes > 0; } }
            public bool HasLowSpeed { get { return LowSpeedRoutes > 0; } }
            public bool HasIpv6OnlyOrDsLiteSignal { get { return GatewayAdapters == 0 && Ipv6GatewayAdapters > 0; } }
            public bool HasCgnatSignal { get { return CgnatAddresses > 0; } }
            public bool HasDnsFilteringSignal { get { return LocalDnsServers > 0; } }
            public bool HasJitterOrLoss { get { return PacketLossPercent > 0 || MaxJitterMs > 45; } }
        }

        public MainForm()
        {
            Text = "CK3MPS " + AppVersion;
            Width = 980;
            Height = 700;
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            ck3Docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "Crusader Kings III");
            stabilizerRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "CK3MPS");
            steamRoot = DetectSteamRoot();
            appManifest = DetectManifest();
            ck3Install = DetectInstallPath();
            ck3Bin = String.IsNullOrEmpty(ck3Install) ? "" : Path.Combine(ck3Install, "binaries");
            localConfig = DetectLocalConfig();
            sharedConfig = DetectSharedConfig();

            BuildUi();
            EnsureStabilizerRoot();
            MigrateLegacyStabilizerState();
            MoveLegacyStabilizerArtifacts();
            settingsGuardTimer.Interval = 10000;
            settingsGuardTimer.Tick += delegate { RunSettingsGuardTick(); };
            FillSteps();
            ValidateStepConfiguration();
            presetBox.SelectedItem = "Recommended";
            if (presetBox.SelectedItem == null)
                ApplyPreset("Recommended");
            graphicsProfileBox.SelectedItem = "Stability Low";
            if (graphicsProfileBox.SelectedItem == null && graphicsProfileBox.Items.Count > 0)
                graphicsProfileBox.SelectedIndex = 0;
            LogSection("Detected paths");
            Log("CK3 Documents: " + ck3Docs);
            Log("Steam: " + NullText(steamRoot));
            Log("CK3 install: " + NullText(ck3Install));
            Log("Launch options file: " + NullText(localConfig));
        }
    }
}



