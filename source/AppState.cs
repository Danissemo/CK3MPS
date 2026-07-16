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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using Timer = System.Windows.Forms.Timer;

namespace CK3MPS
{
    internal sealed partial class MainForm : Form
    {
        private const string AppVersion = "v0.31";
        private const int ExpectedStepCount = StepCatalog.Count;
        private const long MaxSaveAnalysisFileBytes = 128L * 1024L * 1024L;
        private const int MaxSaveAnalysisPrefixBytes = 8 * 1024 * 1024;
        private const int MaxEmbeddedZipAnalysisBytes = 32 * 1024 * 1024;
        private const int MaxOosTextReadBytes = 4 * 1024 * 1024;
        private const int MaxWatcherFiles = 256;
        private const int MaxSteamUserDataUsers = 128;
        private const int MaxSteamUserDataMatches = 32;
        private const int MaxBoundedTraversalDirectories = 1024;
        private const int MaxBoundedTraversalDepth = 6;
        private const int MaxBoundedTraversalElapsedMs = 3000;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly CheckedListBox steps = new CheckedListBox();
        private readonly Panel checklistPanel = new Panel();
        private readonly Panel checklistContentPanel = new Panel();
        private readonly VScrollBar checklistScrollBar = new VScrollBar();
        private readonly ToolTip stepToolTip = new ToolTip();
        private readonly List<StepGroupUi> stepGroups = new List<StepGroupUi>();
        private readonly List<StepRowUi> stepRows = new List<StepRowUi>();
        private bool updatingChecklistUi;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly RichTextBox logBox = new RichTextBox();
        private readonly TabControl mainTabs = new TabControl();
        private readonly TabPage mainPage = new TabPage("Main");
        private readonly TabPage pathsPage = new TabPage("Paths");
        private readonly TabPage workflowPage = new TabPage("Workflow");
        private readonly TabPage reportsPage = new TabPage("Reports");
        private readonly TabPage restorePage = new TabPage("Restore");
        private readonly TabPage advancedPage = new TabPage("Advanced");
        private readonly ComboBox presetBox = new ComboBox();
        private readonly ComboBox graphicsProfileBox = new ComboBox();
        private readonly Button stabilizeButton = new Button();
        private readonly Button checkButton = new Button();
        private readonly Button exportScanReportButton = new Button();
        private readonly Button openFolderButton = new Button();
        private readonly Button openReportsButton = new Button();
        private readonly Button exportSupportButton = new Button();
        private readonly Button refreshHistoryButton = new Button();
        private readonly Button clearReportsButton = new Button();
        private readonly Button workflowEvaluateButton = new Button();
        private readonly Button workflowApplySafeStartButton = new Button();
        private readonly Button workflowCreateRehostPackButton = new Button();
        private readonly Button workflowResetButton = new Button();
        private readonly Button workflowRepairSaveButton = new Button();
        private readonly Button workflowParityRoomButton = new Button();
        private readonly Button workflowMoreButton = new Button();
        private readonly Button workflowOpenSaveFolderButton = new Button();
        private readonly Button workflowOpenOosFolderButton = new Button();
        private readonly Button workflowCompareParityButton = new Button();
        private readonly Button workflowDuplicateSaveButton = new Button();
        private readonly Button workflowArchiveIncidentButton = new Button();
        private readonly ContextMenuStrip workflowMoreMenu = new ContextMenuStrip();
        private readonly Button updateButton = new Button();
        private readonly CheckedListBox restorePointsListBox = new CheckedListBox();
        private readonly Button deleteSelectedRestorePointsButton = new Button();
        private readonly Label restorePointsLabel = new Label();
        private readonly Button clearOtherLogsButton = new Button();
        private readonly Button clearQuarantineButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button selectNoneButton = new Button();
        private readonly Button previewButton = new Button();
        private readonly Label liveLogLabel = new Label();
        private readonly Label graphicsSectionLabel = new Label();
        private readonly Label graphicsHintLabel = new Label();
        private readonly TextBox gamePathBox = new TextBox();
        private readonly TextBox settingsPathBox = new TextBox();
        private readonly Button gamePathBrowseButton = new Button();
        private readonly Button settingsPathBrowseButton = new Button();
        private readonly Button openGamePathButton = new Button();
        private readonly Button openSettingsPathButton = new Button();
        private readonly Button resetPathsButton = new Button();
        private readonly Label gamePathStatusLabel = new Label();
        private readonly Label settingsPathStatusLabel = new Label();
        private readonly Label pathDetailsLabel = new Label();
        private readonly RichTextBox historyBox = new RichTextBox();
        private readonly RichTextBox workflowSummaryBox = new RichTextBox();
        private readonly ListBox workflowStepsListBox = new ListBox();
        private readonly Panel workflowHeaderPanel = new Panel();
        private readonly Panel workflowStatusPanel = new Panel();
        private readonly Panel workflowStatusAccentPanel = new Panel();
        private readonly Panel workflowStepsPanel = new Panel();
        private readonly Panel workflowSummaryPanel = new Panel();
        private readonly ComboBox workflowModeBox = new ComboBox();
        private readonly ComboBox workflowSaveBox = new ComboBox();
        private readonly ComboBox workflowRecoveryPathBox = new ComboBox();
        private readonly Button workflowSaveBrowseButton = new Button();
        private readonly Button workflowSaveDeleteButton = new Button();
        private readonly ProgressBar workflowProgressBar = new ProgressBar();
        private readonly TextBox workflowNotesBox = new TextBox();
        private readonly ComboBox restoreRunBox = new ComboBox();
        private readonly CheckedListBox restoreListBox = new CheckedListBox();
        private readonly TextBox restoreDetailsBox = new TextBox();
        private readonly Button restoreSelectedButton = new Button();
        private readonly Button restoreDefaultButton = new Button();
        private readonly Button deleteRestoreButton = new Button();
        private readonly Button refreshRestoreButton = new Button();
        private readonly Button openQuarantineButton = new Button();
        private readonly Label restoreRunLabel = new Label();
        private readonly Label restoreSortLabel = new Label();
        private readonly ComboBox restoreSortBox = new ComboBox();
        private readonly ComboBox restoreSortDirectionBox = new ComboBox();
        private readonly CheckBox restoreSelectAllBox = new CheckBox();
        private readonly CheckBox updateOnStartupBox = new CheckBox();
        private readonly CheckBox portableModeBox = new CheckBox();
        private readonly CheckBox settingsGuardAutoRepairBox = new CheckBox();
        private readonly ComboBox logVerbosityBox = new ComboBox();
        private readonly ProgressBar updateDownloadProgress = new ProgressBar();
        private readonly GroupBox advancedGeneralGroup = new GroupBox();
        private readonly GroupBox advancedMaintenanceGroup = new GroupBox();
        private readonly GroupBox advancedRestoreGroup = new GroupBox();
        private readonly Label advancedLogVerbosityLabel = new Label();
        private readonly Label advancedHintLabel = new Label();
        private readonly Label workflowModeLabel = new Label();
        private readonly Label workflowSaveLabel = new Label();
        private readonly Label workflowRecoveryPathLabel = new Label();
        private readonly Label workflowActionsLabel = new Label();
        private readonly Label workflowNotesLabel = new Label();
        private readonly Label workflowIncidentLabel = new Label();
        private readonly Label workflowVerdictLabel = new Label();
        private readonly Label workflowStepsLabel = new Label();
        private readonly Label workflowSummaryLabel = new Label();
        private readonly Label workflowHintLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Timer settingsGuardTimer = new Timer();
        private readonly Timer workflowRenderTimer = new Timer();
        private readonly Timer oosWatcherTimer = new Timer();
        private FileSystemWatcher oosWatcherFolderWatcher;
        private FileSystemWatcher oosWatcherLogsWatcher;
        private bool updatingSettingsUi;

        private string ck3Docs;
        private readonly string nonPortableStabilizerRoot;
        private readonly string portableStabilizerRoot;
        private string stabilizerRoot;
        private string steamRoot;
        private string ck3Install;
        private string ck3Bin;
        private string appManifest;
        private string localConfig;
        private string sharedConfig;

        private string lastQuarantine;
        private string currentRestoreRunId;
        private bool updatingRestoreUi;
        private bool updatingRestoreSelectionUi;
        private bool restorePointsLoading;
        private DateTime lastSettingsGuardRepairUtc = DateTime.MinValue;
        private bool settingsGuardActive;
        private int lastReadinessFailures;
        private bool updateCheckOnStartup = true;
        private bool portableMode;
        private bool portableModeChangeInProgress;
        private bool settingsGuardAutoRepairEnabled;
        private string logVerbosity = "Normal";
        private bool gamePathOverrideActive;
        private bool settingsPathOverrideActive;
        private string liveLogFilePath;
        private bool liveLogWritesEnabled;
        private readonly StringBuilder liveLogBuffer = new StringBuilder();
        private readonly List<PendingUiLogLine> pendingUiLogLines = new List<PendingUiLogLine>();
        private readonly object uiLogSync = new object();
        private readonly object runLogSync = new object();
        private readonly List<string> runLogLines = new List<string>();
        private bool uiLogFlushScheduled;
        private bool uiLogFlushInProgress;
        private int uiLogLinesSinceLastScroll;
        private bool busyUi;
        private bool executionSnapshotActive;
        private bool readOnlyScanMode;
        private string executionPreset = "";
        private string executionGraphicsProfile = "";
        private readonly bool[] executionStepChecks = new bool[ExpectedStepCount];
        private readonly HashSet<string> checkedRestoreEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool hasFreshCheckOnlyScan;
        private string freshCheckOnlyScanKey = "";
        private SessionScanSnapshot sessionScanSnapshot;
        private string lastCheckOnlyReportText = "";
        private string[] lastReadOnlyMutationAttempts = new string[0];
        private int historyRefreshRequestId;
        private int deferredFinalizeGeneration;
        private bool applyButtonHintVisible;
        private bool updatingWorkflowUi;
        private bool workflowUiInitialized;
        private bool workflowRefreshPending;
        private int workflowLoadGeneration;
        private int workflowRenderIndex;
        private string workflowRenderVerdict = "";
        private string workflowRenderSummary = "";
        private List<WorkflowStepState> workflowRenderStates = new List<WorkflowStepState>();
        private string currentWorkflowScenario = "Start Session";
        private string workflowSelectedSavePath = "";
        private string workflowRecoveryPath = "Full rehost";
        private string workflowIncidentId = "";
        private string workflowIncidentStatus = "";
        private string workflowIncidentNotes = "";
        private string workflowLastRehostPackPath = "";
        private string workflowLastArchivedIncidentPath = "";
        private string workflowLastOosMetadataPath = "";
        private string oosWatcherLastSignature = "";
        private DateTime oosWatcherLastHandledUtc = DateTime.MinValue;
        private DateTime oosWatcherLastQueuedUtc = DateTime.MinValue;
        private DateTime oosWatcherLastWarningUtc = DateTime.MinValue;
        private string currentIncidentStateSignature = "";
        private readonly object oosWatcherSync = new object();
        private CancellationTokenSource oosWatcherCancelSource;
        private Task oosWatcherTask;
        private bool oosWatcherPending;
        private bool oosWatcherStopping;
        private string oosWatcherPendingReason = "";
        private int oosWatcherProcessCount;
        private readonly List<WorkflowStepState> workflowStepStates = new List<WorkflowStepState>();
        private readonly Dictionary<string, WorkflowScenarioSnapshot> workflowScenarioSnapshots = new Dictionary<string, WorkflowScenarioSnapshot>(StringComparer.OrdinalIgnoreCase);
        private HostSaveCandidateResult cachedBestHostSaveCandidate;
        private string cachedBestHostSaveCandidateKey = "";
        private HostSuitabilityResult cachedHostSuitability;
        private string cachedHostSuitabilityKey = "";
        private DateTime cachedHostSuitabilityUtc = DateTime.MinValue;

        private sealed class StepGroupUi
        {
            public string Title;
            public readonly int[] StepIndices;
            public readonly Panel Header = new Panel();
            public readonly CheckBox CheckBox = new CheckBox();
            public readonly Button ToggleButton = new Button();
            public readonly Label TitleLabel = new Label();
            public readonly List<StepRowUi> Rows = new List<StepRowUi>();
            public bool Expanded = false;

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
            public readonly Label HelpButton = new Label();
        }

        private sealed class PendingUiLogLine
        {
            public readonly string Text;
            public readonly Color Color;

            public PendingUiLogLine(string text, Color color)
            {
                Text = text ?? "";
                Color = color;
            }
        }

        private sealed class StepPlanSnapshot
        {
            public int Index;
            public bool Selected;
            public bool ShouldRun;
            public bool ChangesState;
            public bool NeedsQuarantine;
            public string SkipReason = "";
            public List<string> PreviewDetails = new List<string>();
        }

        private sealed class WorkflowStepState
        {
            public string Id = "";
            public string Label = "";
            public string Detail = "";
            public string Timing = "";
            public bool Required;
            public bool Manual;
            public bool AutoManaged;
            public bool Passed;
            public bool Blocked;
            public bool UserDone;
        }

        private sealed class WorkflowScenarioSnapshot
        {
            public string Scenario = "";
            public string Verdict = "";
            public string Summary = "";
            public List<WorkflowStepState> States = new List<WorkflowStepState>();
        }

        private sealed class ParityManifestRecord
        {
            public string Path = "";
            public string PlayerLabel = "";
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ParityRoomPeer
        {
            public string PeerId = "";
            public string PlayerLabel = "";
            public string Endpoint = "";
            public string ManifestText = "";
            public string OosSummaryText = "";
            public string OosMetadataText = "";
            public string OosDeepReportText = "";
            public string OosRecoveryRunbookText = "";
            public string OosContaminationText = "";
            public string OosSaveDumpText = "";
            public string OosModifierDumpText = "";
            public string OosErrorLogText = "";
            public ParityManifestRecord ParsedManifest;
            public string ParsedRecoveryPath = "";
            public string ParsedContamination = "";
            public string ParsedHotjoin = "";
            public DateTime ReceivedUtc = DateTime.UtcNow;
        }

        private sealed class ParityRoomSession
        {
            public const int MaxPayloadBytes = 1024 * 1024;
            public const int MaxFieldChars = 262144;
            public const int SocketTimeoutMs = 10000;
            public const int MaxReplayNonces = 256;
            public const int MaxReplayAgeMinutes = 15;
            public const int MaxConcurrentClients = 8;
            public const int MaxPeers = 32;
            public const int MaxRequestsPerMinute = 60;
            public const string ProtocolVersion = "1";
            public const string MessageType = "parity_update";
            public bool Hosting;
            public bool Joined;
            public string JoinHost = "";
            public int JoinPort;
            public string RoomCode = "";
            public string SharedSecret = "";
            public string LocalPlayerLabel = "";
            public string LocalPeerId = Guid.NewGuid().ToString("N");
            public TcpListener Listener;
            public System.Threading.CancellationTokenSource CancelSource;
            public readonly SemaphoreSlim ClientSlots = new SemaphoreSlim(MaxConcurrentClients, MaxConcurrentClients);
            public Task AcceptLoopTask;
            public readonly List<Task> ActiveClientTasks = new List<Task>();
            public readonly List<TcpClient> ActiveClients = new List<TcpClient>();
            public readonly Dictionary<string, Queue<DateTime>> RequestTimesByEndpoint = new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<ParityRoomPeer> Peers = new List<ParityRoomPeer>();
            public readonly object Sync = new object();
            public string LocalManifestText = "";
            public ParityManifestRecord LocalManifest;
            public OosDeepInsight LocalInsight = new OosDeepInsight();
            public string LocalOosSummaryText = "";
            public string LocalOosMetadataText = "";
            public string LocalOosDeepReportText = "";
            public string LocalOosRunbookText = "";
            public string LocalOosContaminationText = "";
            public string LocalOosSaveDumpText = "";
            public string LocalOosModifierDumpText = "";
            public string LocalOosErrorLogText = "";
            public bool RawOosShareConsented;
            public bool OosReportsShareConsented;
            public bool RawOosDumpsShareConsented;
            public readonly HashSet<string> SeenPayloadNonces = new HashSet<string>(StringComparer.Ordinal);
            public readonly Queue<string> SeenPayloadNonceOrder = new Queue<string>();
            public string LastComparisonSignature = "";
            public string LastComparisonDifferencesText = "";
            public string LastComparisonActionsText = "";
            public bool LastComparisonSafeToStart;
            public readonly List<ParityDifferenceRow> LastComparisonRows = new List<ParityDifferenceRow>();
        }

        private sealed class ParityDifferenceRow
        {
            public string Player = "";
            public string Area = "";
            public string HostValue = "";
            public string PlayerValue = "";
            public string Status = "";
        }

        private sealed class ParityRoomPlayerRow
        {
            public string Player = "";
            public string Parity = "";
            public string Oos = "";
            public string Path = "";
            public string Risk = "";
            public string Tone = "";
        }

        private sealed class ParityRoomViewState
        {
            public string InfoText = "";
            public string ActionsText = "";
            public bool SafeToStart;
            public readonly List<ParityDifferenceRow> DifferenceRows = new List<ParityDifferenceRow>();
            public readonly List<ParityRoomPlayerRow> PlayerRows = new List<ParityRoomPlayerRow>();
        }

        private sealed class WorkflowSaveOption
        {
            public string Path = "";
            public string Display = "";

            public override string ToString()
            {
                return String.IsNullOrEmpty(Display) ? Path : Display;
            }
        }

        private sealed class SaveRuleCheckResult
        {
            public string Id = "";
            public string DisplayName = "";
            public string Expected = "";
            public string Actual = "";
            public string Evidence = "";
            public bool Found;
            public bool Safe;
            public bool Critical = true;
        }

        private sealed class SaveAnalysisResult
        {
            public string Path = "";
            public string SourceKind = "";
            public string SaveName = "";
            public string Version = "";
            public string Date = "";
            public string Player = "";
            public string Title = "";
            public bool Readable;
            public bool VersionMatchesInstalled;
            public bool SuspiciousName;
            public readonly List<SaveRuleCheckResult> Rules = new List<SaveRuleCheckResult>();
        }

        private sealed class HostSaveCandidateResult
        {
            public SaveAnalysisResult Save = new SaveAnalysisResult();
            public int Score;
            public string Verdict = "";
            public readonly List<string> Issues = new List<string>();
            public readonly List<string> Strengths = new List<string>();
        }

        private sealed class HostSuitabilityResult
        {
            public int Score;
            public string Level = "";
            public bool Suitable;
            public readonly List<string> Strengths = new List<string>();
            public readonly List<string> Risks = new List<string>();
        }

        private sealed class OosDeepInsight
        {
            public string MetadataPath = "";
            public string FolderPath = "";
            public string SaveDumpPath = "";
            public string ModifierDumpPath = "";
            public string ErrorLogPath = "";
            public string OosType = "";
            public string Date = "";
            public string RecoveryPath = "Full rehost";
            public string RecoveryReason = "";
            public string SessionContaminationLevel = "LOW";
            public int SessionContaminationScore;
            public int RandomSeed;
            public int RandomCount;
            public int CharacterMentions;
            public int ModifierMentions;
            public int ArmyMentions;
            public int AiMentions;
            public int FailedContextSwitchCount;
            public int NullTargetCount;
            public int RelationErrorCount;
            public int ScriptErrorCount;
            public int RepeatedStateDivergenceCount;
            public bool HotjoinForbidden;
            public bool WatcherRecoveryState;
            public readonly List<string> CharacterSamples = new List<string>();
            public readonly List<string> ModifierSamples = new List<string>();
            public readonly List<string> ArmySamples = new List<string>();
            public readonly List<string> AiSamples = new List<string>();
            public readonly List<string> Findings = new List<string>();
            public readonly List<string> Runbook = new List<string>();
        }

        private sealed class OosIncidentEvent
        {
            public string MetadataPath = "";
            public string OosType = "";
            public string Date = "";
            public DateTime TimestampUtc = DateTime.MinValue;
            public string RecoveryPath = "";
            public string ContaminationLevel = "";
            public int ContaminationScore;
            public bool HotjoinForbidden;
        }

        private sealed class OosIncidentState
        {
            public string IncidentId = "";
            public string Stage = "Ready";
            public string Confidence = "Low";
            public string SelectedBaselineSave = "";
            public string RecommendedPath = "Full rehost";
            public string RecommendedParityFingerprint = "";
            public bool HotjoinAllowed = true;
            public bool StartAllowed = true;
            public int EscalationLevel;
            public int ContinuationRiskScore;
            public int PriorAttempts;
            public int HostSuitabilityScore;
            public int SaveSuitabilityScore;
            public readonly List<string> Observed = new List<string>();
            public readonly List<string> Interpreted = new List<string>();
            public readonly List<string> RequiredActions = new List<string>();
            public readonly List<string> BlockedActions = new List<string>();
            public readonly List<string> AllowedActions = new List<string>();
            public readonly List<string> PlayerResponsibilities = new List<string>();
            public readonly List<OosIncidentEvent> Events = new List<OosIncidentEvent>();
        }

        private sealed class SessionScanSnapshot
        {
            public string ScanKey = "";
            public int SelectedChecklistCount;
            public int PlannedStepCount;
            public bool PathValidationRequired;
            public bool QuarantineRequired;
            public readonly StepPlanSnapshot[] Steps = new StepPlanSnapshot[ExpectedStepCount];
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
            nonPortableStabilizerRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "CK3MPS");
            portableStabilizerRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CK3MPS_Data");
            stabilizerRoot = nonPortableStabilizerRoot;
            EnableSmoothUiDrawing();
            AutoDetectPaths();
            LoadAppConfig();
            RefreshStabilizerRoot();

            BuildUi();
            ConfigureStepToolTipBehavior();
            UpdateSettingsUi();
            UpdatePathStatusIndicators();
            ResetLiveLogFilePath();
            settingsGuardTimer.Interval = 10000;
            settingsGuardTimer.Tick += delegate { RunSettingsGuardTick(); };
            oosWatcherTimer.Interval = 5000;
            oosWatcherTimer.Tick += delegate { ScheduleOosWatcherScan("timer"); };
            FillSteps();
            ValidateStepConfiguration();
            presetBox.SelectedItem = "Recommended";
            if (presetBox.SelectedItem == null)
                ApplyPreset("Recommended");
            graphicsProfileBox.SelectedItem = "Stability Low";
            if (graphicsProfileBox.SelectedItem == null && graphicsProfileBox.Items.Count > 0)
                graphicsProfileBox.SelectedIndex = 0;
            restoreSortBox.SelectedItem = "Created";
            restoreSortDirectionBox.SelectedItem = "Newest first";
            LogSection("Detected paths");
            LogVerbose("State root: " + stabilizerRoot);
            LogVerbose("Portable mode: " + YesNo(portableMode));
            LogVerbose("Settings file: " + AppConfigFile());
            Log((Directory.Exists(ck3Docs) ? "OK   " : "FAIL ") + "CK3 settings folder: " + ck3Docs);
            Log("Steam: " + NullText(steamRoot));
            Log((!String.IsNullOrEmpty(ck3Install) && Directory.Exists(ck3Install) ? "OK   " : "WARN ") + "CK3 game folder: " + NullText(ck3Install));
            Log("Launch options file: " + NullText(localConfig));
            Shown += delegate
            {
                RefreshHistoryView();
                RefreshRestoreList();
                CheckForUpdatesOnStartup();
                StartOosWatcherServices();
            };
            FormClosed += delegate { CancelWorkflowScenarioRefresh(); StopOosWatcherServices(1500); };
        }

        private static bool IsAutomationTestRun()
        {
            string value = Environment.GetEnvironmentVariable("CK3MPS_SKIP_ELEVATION");
            if (String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                return true;

            value = Environment.GetEnvironmentVariable("CK3MPS_TEST_MODE");
            return String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}




