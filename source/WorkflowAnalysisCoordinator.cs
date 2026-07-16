using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class WorkflowAnalysisSnapshot
        {
            public HostSuitabilityResult Host = new HostSuitabilityResult();
            public HostSaveCandidateResult Save = new HostSaveCandidateResult();
            public OosDeepInsight Oos = new OosDeepInsight();
            public OosIncidentState Incident = new OosIncidentState();
            public DateTime CapturedUtc;
        }

        private readonly AsyncLocal<WorkflowAnalysisSnapshot> workflowAnalysisContext = new AsyncLocal<WorkflowAnalysisSnapshot>();
        private readonly object workflowRefreshCancellationSync = new object();
        private CancellationTokenSource workflowRefreshCancellation;
        private int workflowRefreshOwnerGeneration = -1;
        private string workflowRefreshOwnerScenario = "";
        private bool workflowRefreshShuttingDown;

        private WorkflowAnalysisSnapshot CaptureWorkflowAnalysisSnapshot(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkflowAnalysisSnapshot snapshot = new WorkflowAnalysisSnapshot();
            snapshot.Host = AnalyzeHostSuitability();
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Save = AnalyzeWorkflowHostSaveCandidate();
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Oos = AnalyzeLatestOosDeepInsight();
            cancellationToken.ThrowIfCancellationRequested();

            WorkflowAnalysisSnapshot previous = workflowAnalysisContext.Value;
            workflowAnalysisContext.Value = snapshot;
            try
            {
                snapshot.Incident = AnalyzeOosIncidentState();
            }
            finally
            {
                workflowAnalysisContext.Value = previous;
            }
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        private WorkflowAnalysisSnapshot CurrentWorkflowAnalysis()
        {
            WorkflowAnalysisSnapshot snapshot = workflowAnalysisContext.Value;
            return snapshot ?? CaptureWorkflowAnalysisSnapshot(CancellationToken.None);
        }

        private WorkflowScenarioSnapshot BuildWorkflowScenarioSnapshotCore(string scenario, CancellationToken cancellationToken)
        {
            WorkflowAnalysisSnapshot analysis = CaptureWorkflowAnalysisSnapshot(cancellationToken);
            WorkflowAnalysisSnapshot previous = workflowAnalysisContext.Value;
            workflowAnalysisContext.Value = analysis;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WorkflowScenarioSnapshot snapshot = new WorkflowScenarioSnapshot();
                snapshot.Scenario = scenario;
                BuildWorkflowScenarioSteps(scenario, snapshot.States);
                cancellationToken.ThrowIfCancellationRequested();
                snapshot.Verdict = BuildWorkflowVerdictLine(scenario, snapshot.States);
                snapshot.Summary = BuildWorkflowScenarioSummaryText(scenario, snapshot.States);
                cancellationToken.ThrowIfCancellationRequested();
                return snapshot;
            }
            finally
            {
                workflowAnalysisContext.Value = previous;
            }
        }

        private CancellationToken BeginWorkflowRefreshCancellation()
        {
            lock (workflowRefreshCancellationSync)
            {
                if (workflowRefreshCancellation != null)
                {
                    try { workflowRefreshCancellation.Cancel(); } catch { }
                    workflowRefreshCancellation.Dispose();
                }

                if (workflowRefreshShuttingDown)
                {
                    workflowRefreshCancellation = null;
                    workflowRefreshOwnerGeneration = -1;
                    workflowRefreshOwnerScenario = "";
                    return new CancellationToken(true);
                }

                workflowRefreshCancellation = new CancellationTokenSource();
                workflowRefreshOwnerGeneration = workflowLoadGeneration;
                workflowRefreshOwnerScenario = currentWorkflowScenario ?? "";
                return workflowRefreshCancellation.Token;
            }
        }

        private void CancelWorkflowScenarioRefresh()
        {
            lock (workflowRefreshCancellationSync)
            {
                workflowLoadGeneration++;
                workflowRefreshOwnerGeneration = -1;
                workflowRefreshOwnerScenario = "";
                if (workflowRefreshCancellation != null)
                {
                    try { workflowRefreshCancellation.Cancel(); } catch { }
                    workflowRefreshCancellation.Dispose();
                    workflowRefreshCancellation = null;
                }
            }

            workflowRefreshPending = false;
            try
            {
                workflowRenderTimer.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool WorkflowRefreshStillCurrent(int generation, string scenario, CancellationToken cancellationToken)
        {
            lock (workflowRefreshCancellationSync)
            {
                if (workflowRefreshShuttingDown
                    || workflowRefreshCancellation == null
                    || cancellationToken.IsCancellationRequested
                    || !workflowRefreshCancellation.Token.Equals(cancellationToken))
                    return false;

                return generation == workflowLoadGeneration
                    && generation == workflowRefreshOwnerGeneration
                    && String.Equals(scenario, currentWorkflowScenario, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(scenario, workflowRefreshOwnerScenario, StringComparison.OrdinalIgnoreCase);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
                return;

            lock (workflowRefreshCancellationSync)
                workflowRefreshShuttingDown = true;
            CancelWorkflowScenarioRefresh();
        }

        // The rest of the application keeps using the TcpListener surface it already had,
        // but this implementation deliberately binds only loopback and one private LAN
        // interface. It never opens IPAddress.Any, and it rejects routed clients outside
        // the selected interface's subnet before the parity protocol sees their payload.
        private sealed class TcpListener
        {
            private sealed class LanBinding
            {
                public IPAddress Address;
                public IPAddress Mask;
                public int Score;
            }

            private readonly object sync = new object();
            private readonly int requestedPort;
            private System.Net.Sockets.TcpListener loopbackListener;
            private System.Net.Sockets.TcpListener lanListener;
            private IPAddress lanAddress;
            private IPAddress lanMask;
            private int boundPort;
            private bool started;
            private bool stopped;
            private System.Windows.Forms.Timer addressDisplayTimer;
            private static volatile string currentConnectionAddress = "127.0.0.1";

            public TcpListener(IPAddress requestedAddress, int port)
            {
                requestedPort = port;
            }

            public EndPoint LocalEndpoint
            {
                get
                {
                    lock (sync)
                    {
                        IPAddress address = lanAddress ?? IPAddress.Loopback;
                        return new IPEndPoint(address, boundPort);
                    }
                }
            }

            internal static string CurrentConnectionAddress
            {
                get { return String.IsNullOrWhiteSpace(currentConnectionAddress) ? "127.0.0.1" : currentConnectionAddress; }
            }

            public void Start()
            {
                lock (sync)
                {
                    if (started && !stopped)
                        return;

                    stopped = false;
                    LanBinding binding = FindBestLanBinding();
                    int port = requestedPort;
                    if (binding != null)
                    {
                        lanListener = new System.Net.Sockets.TcpListener(binding.Address, port);
                        lanListener.Start();
                        port = ((IPEndPoint)lanListener.LocalEndpoint).Port;
                        lanAddress = binding.Address;
                        lanMask = binding.Mask;
                    }

                    try
                    {
                        loopbackListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                        loopbackListener.Start();
                    }
                    catch
                    {
                        if (lanListener != null)
                        {
                            try { lanListener.Stop(); } catch { }
                            lanListener = null;
                        }
                        lanAddress = null;
                        lanMask = null;
                        throw;
                    }

                    boundPort = ((IPEndPoint)loopbackListener.LocalEndpoint).Port;
                    currentConnectionAddress = lanAddress == null ? IPAddress.Loopback.ToString() : lanAddress.ToString();
                    started = true;
                    NetworkChange.NetworkAddressChanged += NetworkAddressChanged;
                    StartAddressDisplayTimer();
                }
            }

            public TcpClient AcceptTcpClient()
            {
                while (true)
                {
                    System.Net.Sockets.TcpListener loopback;
                    System.Net.Sockets.TcpListener lan;
                    lock (sync)
                    {
                        if (stopped || !started)
                            throw new ObjectDisposedException("TcpListener");
                        loopback = loopbackListener;
                        lan = lanListener;
                    }

                    TcpClient accepted;
                    if (TryAccept(loopback, out accepted) || TryAccept(lan, out accepted))
                    {
                        if (IsAllowedRemote(accepted))
                            return accepted;
                        try { accepted.Close(); } catch { }
                    }
                    Thread.Sleep(15);
                }
            }

            public void Stop()
            {
                lock (sync)
                {
                    if (stopped)
                        return;

                    stopped = true;
                    started = false;
                    NetworkChange.NetworkAddressChanged -= NetworkAddressChanged;
                    StopAddressDisplayTimer();
                    if (lanListener != null)
                    {
                        try { lanListener.Stop(); } catch { }
                        lanListener = null;
                    }
                    if (loopbackListener != null)
                    {
                        try { loopbackListener.Stop(); } catch { }
                        loopbackListener = null;
                    }
                    lanAddress = null;
                    lanMask = null;
                    boundPort = 0;
                    currentConnectionAddress = IPAddress.Loopback.ToString();
                }
            }

            private static bool TryAccept(System.Net.Sockets.TcpListener listener, out TcpClient client)
            {
                client = null;
                if (listener == null)
                    return false;
                try
                {
                    if (!listener.Pending())
                        return false;
                    client = listener.AcceptTcpClient();
                    return client != null;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            private bool IsAllowedRemote(TcpClient client)
            {
                if (client == null || client.Client == null)
                    return false;
                IPEndPoint remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote == null)
                    return false;

                IPAddress address = remote.Address;
                if (address.IsIPv4MappedToIPv6)
                    address = address.MapToIPv4();
                if (IPAddress.IsLoopback(address))
                    return true;
                if (address.AddressFamily != AddressFamily.InterNetwork)
                    return false;

                IPAddress local;
                IPAddress mask;
                lock (sync)
                {
                    local = lanAddress;
                    mask = lanMask;
                }
                return local != null && mask != null && IsSameSubnet(local, address, mask);
            }

            private void NetworkAddressChanged(object sender, EventArgs e)
            {
                lock (sync)
                {
                    if (stopped || !started || boundPort <= 0)
                        return;

                    LanBinding binding = FindBestLanBinding();
                    if (binding != null && lanAddress != null && binding.Address.Equals(lanAddress) && binding.Mask.Equals(lanMask))
                        return;

                    if (lanListener != null)
                    {
                        try { lanListener.Stop(); } catch { }
                        lanListener = null;
                    }
                    lanAddress = null;
                    lanMask = null;
                    currentConnectionAddress = IPAddress.Loopback.ToString();

                    if (binding == null)
                        return;
                    try
                    {
                        System.Net.Sockets.TcpListener replacement = new System.Net.Sockets.TcpListener(binding.Address, boundPort);
                        replacement.Start();
                        lanListener = replacement;
                        lanAddress = binding.Address;
                        lanMask = binding.Mask;
                        currentConnectionAddress = binding.Address.ToString();
                    }
                    catch
                    {
                        // Loopback remains active. A later address-change event or room
                        // recreation will retry the LAN binding without exposing Any.
                    }
                }
            }

            private static LanBinding FindBestLanBinding()
            {
                LanBinding best = null;
                try
                {
                    foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter == null || adapter.OperationalStatus != OperationalStatus.Up)
                            continue;
                        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                            || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                            || adapter.NetworkInterfaceType == NetworkInterfaceType.Ppp)
                            continue;

                        IPInterfaceProperties properties;
                        try { properties = adapter.GetIPProperties(); }
                        catch { continue; }
                        bool hasGateway = properties.GatewayAddresses != null && properties.GatewayAddresses.Count > 0;
                        foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                        {
                            IPAddress address = unicast.Address;
                            if (address == null || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                                continue;
                            if (!IsPrivateLanAddress(address))
                                continue;
                            IPAddress mask = unicast.IPv4Mask;
                            if (mask == null || mask.Equals(IPAddress.Any))
                                continue;

                            int score = hasGateway ? 100 : 0;
                            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                                || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                || adapter.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                                || adapter.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx
                                || adapter.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT)
                                score += 20;
                            if (!IsLinkLocal(address))
                                score += 10;

                            if (best == null || score > best.Score)
                            {
                                best = new LanBinding();
                                best.Address = address;
                                best.Mask = mask;
                                best.Score = score;
                            }
                        }
                    }
                }
                catch
                {
                    return null;
                }
                return best;
            }

            private static bool IsPrivateLanAddress(IPAddress address)
            {
                byte[] bytes = address == null ? new byte[0] : address.GetAddressBytes();
                if (bytes.Length != 4)
                    return false;
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254);
            }

            private static bool IsLinkLocal(IPAddress address)
            {
                byte[] bytes = address == null ? new byte[0] : address.GetAddressBytes();
                return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
            }

            private static bool IsSameSubnet(IPAddress local, IPAddress remote, IPAddress mask)
            {
                byte[] localBytes = local.GetAddressBytes();
                byte[] remoteBytes = remote.GetAddressBytes();
                byte[] maskBytes = mask.GetAddressBytes();
                if (localBytes.Length != 4 || remoteBytes.Length != 4 || maskBytes.Length != 4)
                    return false;
                for (int i = 0; i < 4; i++)
                {
                    if ((localBytes[i] & maskBytes[i]) != (remoteBytes[i] & maskBytes[i]))
                        return false;
                }
                return true;
            }

            private void StartAddressDisplayTimer()
            {
                if (!Application.MessageLoop || addressDisplayTimer != null)
                    return;
                addressDisplayTimer = new System.Windows.Forms.Timer();
                addressDisplayTimer.Interval = 150;
                addressDisplayTimer.Tick += AddressDisplayTimerTick;
                addressDisplayTimer.Start();
            }

            private void StopAddressDisplayTimer()
            {
                if (addressDisplayTimer == null)
                    return;
                try { addressDisplayTimer.Stop(); } catch { }
                try { addressDisplayTimer.Tick -= AddressDisplayTimerTick; } catch { }
                try { addressDisplayTimer.Dispose(); } catch { }
                addressDisplayTimer = null;
            }

            private void AddressDisplayTimerTick(object sender, EventArgs e)
            {
                try
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        if (form == null || form.IsDisposed || !String.Equals(form.Text, "CK3MPS parity room", StringComparison.OrdinalIgnoreCase))
                            continue;
                        RewriteAddressLabels(form.Controls);
                    }
                }
                catch
                {
                }
            }

            private static void RewriteAddressLabels(Control.ControlCollection controls)
            {
                if (controls == null)
                    return;
                const string prefix = "Room mode: hosting | Host: ";
                const string marker = " | Port: ";
                foreach (Control control in controls)
                {
                    string text = control.Text ?? "";
                    if (text.StartsWith(prefix, StringComparison.Ordinal) && text.IndexOf(marker, prefix.Length, StringComparison.Ordinal) >= 0)
                    {
                        int markerIndex = text.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
                        control.Text = prefix + CurrentConnectionAddress + text.Substring(markerIndex);
                    }
                    if (control.HasChildren)
                        RewriteAddressLabels(control.Controls);
                }
            }
        }
    }
}
