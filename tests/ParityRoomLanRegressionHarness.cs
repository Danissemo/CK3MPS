using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class ParityRoomLanRegressionHarness
{
    private sealed class HarnessContext : IDisposable
    {
        public object Form;
        public Type MainFormType;
        public Type SessionType;
        public MethodInfo StartHost;
        public MethodInfo StopHost;
        public MethodInfo Sign;
        public MethodInfo Build;
        public MethodInfo Protect;
        public MethodInfo Unprotect;

        public void Dispose()
        {
            IDisposable disposable = Form as IDisposable;
            if (disposable != null)
                disposable.Dispose();
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");
            using (HarnessContext context = CreateContext(assemblyPath))
            {
                TestLanAndLoopbackBinding(context);
                TestWrongRoomCode(context);
                TestWrongSharedSecret(context);
                TestReplayAndTamper(context);
                TestPayloadSizeLimit(context);
                TestPeerLimit(context);
                TestConcurrentClientLimit(context);
                TestListenerStopReleasesPort(context);
            }
            Console.WriteLine("Parity LAN/security regression tests passed.");
            return 0;
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine(ex.InnerException ?? ex);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static HarnessContext CreateContext(string assemblyPath)
    {
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
        BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type sessionType = mainFormType.GetNestedType("ParityRoomSession", BindingFlags.NonPublic);
        HarnessContext context = new HarnessContext();
        context.MainFormType = mainFormType;
        context.SessionType = sessionType;
        context.Form = Activator.CreateInstance(mainFormType, true);
        context.StartHost = mainFormType.GetMethod("StartParityRoomHost", instanceFlags);
        context.StopHost = mainFormType.GetMethod("StopParityRoomHost", instanceFlags);
        context.Sign = mainFormType.GetMethod("ComputeParityRoomSignature", instanceFlags);
        context.Build = mainFormType.GetMethod("BuildParityRoomPayloadText", instanceFlags);
        context.Protect = mainFormType.GetMethod("ProtectParityRoomPayload", instanceFlags);
        context.Unprotect = mainFormType.GetMethod("UnprotectParityRoomPayload", instanceFlags);
        if (sessionType == null || context.StartHost == null || context.StopHost == null || context.Sign == null
            || context.Build == null || context.Protect == null || context.Unprotect == null)
            throw new InvalidOperationException("Parity room regression members were not found.");
        return context;
    }

    private static object StartSession(HarnessContext context)
    {
        object session = Activator.CreateInstance(context.SessionType, true);
        context.StartHost.Invoke(context.Form, new object[] { session, null });
        int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
        Assert(port > 0 && port <= 65535, "host should expose a valid local port");
        return session;
    }

    private static void TestLanAndLoopbackBinding(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            object listener = GetField(context.SessionType, "Listener").GetValue(session);
            Assert(listener != null, "parity host listener should be present");
            Type listenerType = listener.GetType();
            PropertyInfo currentAddressProperty = listenerType.GetProperty("CurrentConnectionAddress", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo loopbackField = listenerType.GetField("loopbackListener", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lanField = listenerType.GetField("lanListener", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo rewriteLabels = listenerType.GetMethod("RewriteAddressLabels", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(currentAddressProperty != null && loopbackField != null && lanField != null && rewriteLabels != null,
                "secure listener diagnostics should be available for regression checks");

            string connectionAddress = Convert.ToString(currentAddressProperty.GetValue(null, null)) ?? "";
            IPAddress parsedAddress;
            Assert(IPAddress.TryParse(connectionAddress, out parsedAddress), "displayed parity address should be a valid IPv4 address");
            Assert(parsedAddress.AddressFamily == AddressFamily.InterNetwork, "displayed parity address should be IPv4");
            Assert(!parsedAddress.Equals(IPAddress.Any), "0.0.0.0 must never be displayed as the connection address");
            Assert(IPAddress.IsLoopback(parsedAddress) || IsPrivateLanAddress(parsedAddress),
                "parity host must expose only loopback or a private LAN address");

            object loopbackListener = loopbackField.GetValue(listener);
            Assert(loopbackListener != null, "loopback listener must remain active while hosting");
            IPEndPoint loopbackEndpoint = ((System.Net.Sockets.TcpListener)loopbackListener).LocalEndpoint as IPEndPoint;
            Assert(loopbackEndpoint != null && IPAddress.IsLoopback(loopbackEndpoint.Address), "loopback listener must bind 127.0.0.1 explicitly");

            object lanListener = lanField.GetValue(listener);
            if (!IPAddress.IsLoopback(parsedAddress))
            {
                Assert(lanListener != null, "private LAN address requires a corresponding LAN listener");
                IPEndPoint lanEndpoint = ((System.Net.Sockets.TcpListener)lanListener).LocalEndpoint as IPEndPoint;
                Assert(lanEndpoint != null && lanEndpoint.Address.Equals(parsedAddress), "LAN listener must bind the displayed private IPv4 address");
                Assert(!lanEndpoint.Address.Equals(IPAddress.Any), "LAN listener must never bind IPAddress.Any");
            }

            Panel panel = new Panel();
            Label label = new Label();
            label.Text = "Room mode: hosting | Host: 127.0.0.1 | Port: 12345 | Code: 111111 | Secret: hidden";
            panel.Controls.Add(label);
            rewriteLabels.Invoke(null, new object[] { panel.Controls });
            Assert(label.Text.IndexOf("Host: " + connectionAddress + " | Port:", StringComparison.Ordinal) >= 0,
                "parity room UI should show the selected LAN address instead of a hard-coded loopback address");
            Assert(label.Text.IndexOf("0.0.0.0", StringComparison.Ordinal) < 0, "parity room UI must not show 0.0.0.0");

            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string roomCode = GetFieldValue<string>(context.SessionType, session, "RoomCode");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            byte[] validPacket = BuildPacket(context, roomCode, secret, "loopback-player", "loopback-manifest", NewNonce());
            string reply = SendPacketAndDecrypt(context, IPAddress.Loopback.ToString(), port, validPacket, secret);
            Assert(reply.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0, "loopback connection should remain functional");
            Assert(GetPeerCount(context, session) == 1, "valid loopback payload should create one peer");
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestWrongRoomCode(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            byte[] packet = BuildPacket(context, "wrong-room-code", secret, "wrong-code-player", "private-manifest", NewNonce());
            string reply = SendPacketAndDecrypt(context, "127.0.0.1", port, packet, secret);
            Assert(reply.IndexOf("bad room code", StringComparison.OrdinalIgnoreCase) >= 0, "wrong room code must be rejected explicitly");
            Assert(GetPeerCount(context, session) == 0, "wrong room code must not expose or store peer data");
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestWrongSharedSecret(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string roomCode = GetFieldValue<string>(context.SessionType, session, "RoomCode");
            string correctSecret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            string wrongSecret = "wrong-secret-" + Guid.NewGuid().ToString("N");
            string marker = "secret-manifest-must-not-leak";
            byte[] packet = BuildPacket(context, roomCode, wrongSecret, "wrong-secret-player", marker, NewNonce());
            byte[] rawReply = SendPacketRaw("127.0.0.1", port, packet);
            string rawText = Encoding.UTF8.GetString(rawReply ?? new byte[0]);
            Assert(rawText.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0, "wrong-secret response must not reveal payload content");
            Assert(rawText.IndexOf("wrong-secret-player", StringComparison.OrdinalIgnoreCase) < 0, "wrong-secret response must not reveal player data");
            Assert(GetPeerCount(context, session) == 0, "wrong shared secret must not store peer data");

            if (rawReply != null && rawReply.Length > 0)
            {
                string decrypted = Convert.ToString(context.Unprotect.Invoke(context.Form, new object[] { rawReply, correctSecret })) ?? "";
                Assert(decrypted.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0, "host should return only an authenticated generic error");
            }
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestReplayAndTamper(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string roomCode = GetFieldValue<string>(context.SessionType, session, "RoomCode");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            byte[] packet = BuildPacket(context, roomCode, secret, "replay-player", "manifest", NewNonce());
            string first = SendPacketAndDecrypt(context, "127.0.0.1", port, packet, secret);
            Assert(first.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0, "first nonce use should succeed");
            string replay = SendPacketAndDecrypt(context, "127.0.0.1", port, packet, secret);
            Assert(replay.IndexOf("replayed", StringComparison.OrdinalIgnoreCase) >= 0, "repeated nonce must be rejected");
            Assert(GetPeerCount(context, session) == 1, "replay must not create another peer");

            byte[] tampered = (byte[])packet.Clone();
            tampered[Math.Max(1, tampered.Length / 2)] ^= 0x4A;
            byte[] rawReply = SendPacketRaw("127.0.0.1", port, tampered);
            string rawText = Encoding.UTF8.GetString(rawReply ?? new byte[0]);
            Assert(rawText.IndexOf("manifest", StringComparison.OrdinalIgnoreCase) < 0, "tamper response must not reveal decrypted data");
            Assert(GetPeerCount(context, session) == 1, "tampered transport packet must not change peer state");
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestPayloadSizeLimit(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            int maxPayload = GetConstant<int>(context.SessionType, "MaxPayloadBytes");
            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            using (TcpClient client = new TcpClient())
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                client.Connect(IPAddress.Loopback, port);
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] oversizedLength = BitConverter.GetBytes(maxPayload + 1);
                    stream.Write(oversizedLength, 0, oversizedLength.Length);
                    stream.Flush();
                    client.Client.Shutdown(SocketShutdown.Send);
                    byte[] replyPacket = ReadFramedPacket(stream);
                    string reply = Convert.ToString(context.Unprotect.Invoke(context.Form, new object[] { replyPacket, secret })) ?? "";
                    Assert(reply.IndexOf("too large", StringComparison.OrdinalIgnoreCase) >= 0, "oversized payload must be rejected before allocation/read");
                }
            }
            Assert(GetPeerCount(context, session) == 0, "oversized payload must not create a peer");
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestPeerLimit(HarnessContext context)
    {
        object session = StartSession(context);
        try
        {
            int maxPeers = GetConstant<int>(context.SessionType, "MaxPeers");
            FieldInfo peersField = GetField(context.SessionType, "Peers");
            IList peers = peersField.GetValue(session) as IList;
            Type peerType = context.MainFormType.GetNestedType("ParityRoomPeer", BindingFlags.NonPublic);
            Assert(peers != null && peerType != null, "peer list should be available for the limit regression test");
            for (int i = 0; i < maxPeers; i++)
                peers.Add(Activator.CreateInstance(peerType, true));

            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string roomCode = GetFieldValue<string>(context.SessionType, session, "RoomCode");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            byte[] packet = BuildPacket(context, roomCode, secret, "overflow-peer", "manifest", NewNonce());
            string reply = SendPacketAndDecrypt(context, "127.0.0.1", port, packet, secret);
            Assert(reply.IndexOf("peer limit", StringComparison.OrdinalIgnoreCase) >= 0, "peer count above the configured limit must be rejected");
            Assert(peers.Count == maxPeers, "peer limit rejection must leave the existing peer list unchanged");
        }
        finally
        {
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestConcurrentClientLimit(HarnessContext context)
    {
        object session = StartSession(context);
        List<TcpClient> slowClients = new List<TcpClient>();
        try
        {
            int maxClients = GetConstant<int>(context.SessionType, "MaxConcurrentClients");
            int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
            string secret = GetFieldValue<string>(context.SessionType, session, "SharedSecret");
            for (int i = 0; i < maxClients; i++)
            {
                TcpClient slow = new TcpClient();
                slow.ReceiveTimeout = 5000;
                slow.SendTimeout = 5000;
                slow.Connect(IPAddress.Loopback, port);
                NetworkStream stream = slow.GetStream();
                stream.Write(new byte[] { 1, 0 }, 0, 2);
                stream.Flush();
                slowClients.Add(slow);
            }

            WaitForCollectionCount(context.SessionType, session, "ActiveClients", maxClients, 5000);
            using (TcpClient overflow = new TcpClient())
            {
                overflow.ReceiveTimeout = 5000;
                overflow.SendTimeout = 5000;
                overflow.Connect(IPAddress.Loopback, port);
                using (NetworkStream stream = overflow.GetStream())
                {
                    byte[] replyPacket = ReadFramedPacket(stream);
                    string reply = Convert.ToString(context.Unprotect.Invoke(context.Form, new object[] { replyPacket, secret })) ?? "";
                    Assert(reply.IndexOf("room busy", StringComparison.OrdinalIgnoreCase) >= 0, "client above the concurrent limit must receive room busy");
                }
            }
        }
        finally
        {
            foreach (TcpClient client in slowClients)
            {
                try { client.Close(); } catch { }
            }
            context.StopHost.Invoke(context.Form, new object[] { session });
        }
    }

    private static void TestListenerStopReleasesPort(HarnessContext context)
    {
        object session = StartSession(context);
        int port = GetFieldValue<int>(context.SessionType, session, "JoinPort");
        context.StopHost.Invoke(context.Form, new object[] { session });
        using (System.Net.Sockets.TcpListener replacement = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port))
        {
            replacement.Start();
            Assert(((IPEndPoint)replacement.LocalEndpoint).Port == port, "stopping the parity host should release its loopback port");
            replacement.Stop();
        }
    }

    private static byte[] BuildPacket(HarnessContext context, string roomCode, string secret, string player, string manifest, string nonce)
    {
        string createdUtc = DateTime.UtcNow.ToString("o");
        string signature = Convert.ToString(context.Sign.Invoke(context.Form, new object[]
        {
            secret, roomCode, createdUtc, nonce, player, manifest,
            "", "", "", "", "", "", "", ""
        })) ?? "";
        string payload = Convert.ToString(context.Build.Invoke(context.Form, new object[]
        {
            roomCode, createdUtc, nonce, player, manifest,
            "", "", "", "", "", "", "", "", signature
        })) ?? "";
        return (byte[])context.Protect.Invoke(context.Form, new object[] { payload, secret });
    }

    private static string SendPacketAndDecrypt(HarnessContext context, string host, int port, byte[] packet, string secret)
    {
        byte[] response = SendPacketRaw(host, port, packet);
        return Convert.ToString(context.Unprotect.Invoke(context.Form, new object[] { response, secret })) ?? "";
    }

    private static byte[] SendPacketRaw(string host, int port, byte[] packet)
    {
        using (TcpClient client = new TcpClient())
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            client.Connect(host, port);
            using (NetworkStream stream = client.GetStream())
            {
                byte[] payload = packet ?? new byte[0];
                byte[] length = BitConverter.GetBytes(payload.Length);
                stream.Write(length, 0, length.Length);
                if (payload.Length > 0)
                    stream.Write(payload, 0, payload.Length);
                stream.Flush();
                client.Client.Shutdown(SocketShutdown.Send);
                return ReadFramedPacket(stream);
            }
        }
    }

    private static byte[] ReadFramedPacket(Stream stream)
    {
        byte[] lengthBytes = ReadExact(stream, 4);
        int length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > 2 * 1024 * 1024)
            throw new InvalidOperationException("Parity response length is invalid: " + length);
        return ReadExact(stream, length);
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
                throw new EndOfStreamException("Parity connection ended before the framed response was complete.");
            offset += read;
        }
        return buffer;
    }

    private static int GetPeerCount(HarnessContext context, object session)
    {
        IList peers = GetField(context.SessionType, "Peers").GetValue(session) as IList;
        return peers == null ? -1 : peers.Count;
    }

    private static void WaitForCollectionCount(Type sessionType, object session, string fieldName, int expected, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ICollection collection = GetField(sessionType, fieldName).GetValue(session) as ICollection;
            if (collection != null && collection.Count >= expected)
                return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException(fieldName + " did not reach " + expected + " entries before timeout.");
    }

    private static FieldInfo GetField(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        return field;
    }

    private static T GetFieldValue<T>(Type type, object instance, string fieldName)
    {
        return (T)GetField(type, fieldName).GetValue(instance);
    }

    private static T GetConstant<T>(Type type, string fieldName)
    {
        FieldInfo field = GetField(type, fieldName);
        return (T)field.GetRawConstantValue();
    }

    private static string NewNonce()
    {
        return "nonce-" + Guid.NewGuid().ToString("N");
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
