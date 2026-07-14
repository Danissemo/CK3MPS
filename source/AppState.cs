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
        private const int ExpectedStepCount = 29;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly CheckedListBox steps = new CheckedListBox();
        private readonly FlowLayoutPanel checklistPanel = new FlowLayoutPanel();
        private readonly ToolTip stepToolTip = new ToolTip();
        private readonly List<StepGroupUi> stepGroups = new List<StepGroupUi>();
        private readonly List<StepRowUi> stepRows = new List<StepRowUi>();
        private bool updatingChecklistUi;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly RichTextBox logBox = new RichTextBox();
        private readonly TabControl mainTabs = new TabControl();
        private readonly TabPage mainPage = new TabPage("Main");
        private readonly TabPage pathsPage = new TabPage("Paths");
        private readonly TabPage logPage = new TabPage("Log");
        private readonly TabPage reportsPage = new TabPage("Reports");
        private readonly TabPage restorePage = new TabPage("Restore");
        private readonly TabPage advancedPage = new TabPage("Advanced");
        private readonly ComboBox presetBox = new ComboBox();
        private readonly ComboBox graphicsProfileBox = new ComboBox();
        private readonly Button stabilizeButton = new Button();
        private readonly Button checkButton = new Button();
        private readonly Button openFolderButton = new Button();
        private readonly Button openReportsButton = new Button();
        private readonly Button exportSupportButton = new Button();
        private readonly Button refreshHistoryButton = new Button();
        private readonly Button updateButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button selectNoneButton = new Button();
        private readonly TextBox gamePathBox = new TextBox();
        private readonly TextBox settingsPathBox = new TextBox();
        private readonly Button gamePathBrowseButton = new Button();
        private readonly Button settingsPathBrowseButton = new Button();
        private readonly Button resetPathsButton = new Button();
        private readonly Label gamePathStatusLabel = new Label();
        private readonly Label settingsPathStatusLabel = new Label();
        private readonly TextBox historyBox = new TextBox();
        private readonly ListBox restoreListBox = new ListBox();
        private readonly TextBox restoreDetailsBox = new TextBox();
        private readonly Button restoreSelectedButton = new Button();
        private readonly Button refreshRestoreButton = new Button();
        private readonly Button openQuarantineButton = new Button();
        private readonly CheckBox updateOnStartupBox = new CheckBox();
        private readonly CheckBox portableModeBox = new CheckBox();
        private readonly ComboBox logVerbosityBox = new ComboBox();
        private readonly ProgressBar updateDownloadProgress = new ProgressBar();
        private readonly Label statusLabel = new Label();
        private readonly Timer settingsGuardTimer = new Timer();

        private string ck3Docs;
        private readonly string stabilizerRoot;
        private string steamRoot;
        private string ck3Install;
        private string ck3Bin;
        private string appManifest;
        private string localConfig;
        private string sharedConfig;

        private string lastQuarantine;
        private DateTime lastSettingsGuardRepairUtc = DateTime.MinValue;
        private bool settingsGuardActive;
        private int lastReadinessFailures;
        private bool updateCheckOnStartup = true;
        private bool portableMode;
        private string logVerbosity = "Normal";

        private sealed class StepGroupUi
        {
            public string Title;
            public readonly int[] StepIndices;
            public readonly Panel Header = new Panel();
            public readonly CheckBox CheckBox = new CheckBox();
            public readonly Button ToggleButton = new Button();
            public readonly Label TitleLabel = new Label();
            public readonly List<StepRowUi> Rows = new List<StepRowUi>();
            public bool Expanded = true;

            public StepGroupUi(string title, int[] stepIndices)
            {
                Title = title;
                StepIndices = stepIndices;
            }
        }

        private sealed class StepRowUi
        {
            public int Index;
            public string Title;
            public string HelpText;
            public readonly Panel RowPanel = new Panel();
            public readonly CheckBox CheckBox = new CheckBox();
            public readonly Label TitleLabel = new Label();
            public readonly Button HelpButton = new Button();
        }

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
            Height = 760;
            MinimumSize = new Size(900, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            ck3Docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "Crusader Kings III");
            stabilizerRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "CK3MPS");
            AutoDetectPaths();
            LoadAppConfig();

            BuildUi();
            UpdateSettingsUi();
            UpdatePathStatusIndicators();
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
            Log((Directory.Exists(ck3Docs) ? "OK   " : "FAIL ") + "CK3 settings folder: " + ck3Docs);
            Log("Steam: " + NullText(steamRoot));
            Log((!String.IsNullOrEmpty(ck3Install) && Directory.Exists(ck3Install) ? "OK   " : "WARN ") + "CK3 game folder: " + NullText(ck3Install));
            Log("Launch options file: " + NullText(localConfig));
            Shown += delegate
            {
                RefreshHistoryView();
                RefreshRestoreList();
                CheckForUpdatesOnStartup();
            };
        }
    }
}



