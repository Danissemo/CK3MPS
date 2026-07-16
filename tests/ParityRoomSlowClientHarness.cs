using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

internal static class ParityRoomSlowClientHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            object form = Activator.CreateInstance(mainFormType, true);
            try
            {
                Type sessionType = mainFormType.GetNestedType("ParityRoomSession", BindingFlags.NonPublic);
                MethodInfo startHost = mainFormType.GetMethod("StartParityRoomHost", instanceFlags);
                MethodInfo stopHost = mainFormType.GetMethod("StopParityRoomHost", instanceFlags);
                MethodInfo sign = mainFormType.GetMethod("ComputeParityRoomSignature", instanceFlags);
                MethodInfo build = mainFormType.GetMethod("BuildParityRoomPayloadText", instanceFlags);
                MethodInfo protect = mainFormType.GetMethod("ProtectParityRoomPayload", instanceFlags);
                MethodInfo unprotect = mainFormType.GetMethod("UnprotectParityRoomPayload", instanceFlags);
                if (sessionType == null || startHost == null || stopHost == null || sign == null || build == null || protect == null || unprotect == null)
                    throw new InvalidOperationException("Parity room host members were not found.");

                object session = Activator.CreateInstance(sessionType, true);
                startHost.Invoke(form, new object[] { session, null });

                int port = GetFieldValue<int>(sessionType, session, "JoinPort");
                string roomCode = GetFieldValue<string>(sessionType, session, "RoomCode");
                string sharedSecret = GetFieldValue<string>(sessionType, session, "SharedSecret");

                using (TcpClient slowClient = new TcpClient())
                {
                    slowClient.Connect("127.0.0.1", port);
                    NetworkStream slowStream = slowClient.GetStream();
                    byte[] fakeLengthPrefix = BitConverter.GetBytes(128);
                    slowStream.Write(fakeLengthPrefix, 0, 2);
                    slowStream.Flush();

                    string createdUtc = DateTime.UtcNow.ToString("o");
                    string nonce = "nonce-" + Guid.NewGuid().ToString("N");
                    string signature = Convert.ToString(sign.Invoke(form, new object[]
                    {
                        sharedSecret,
                        roomCode,
                        createdUtc,
                        nonce,
                        "fast-player",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        ""
                    })) ?? "";

                    string payload = Convert.ToString(build.Invoke(form, new object[]
                    {
                        roomCode,
                        createdUtc,
                        nonce,
                        "fast-player",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        signature
                    })) ?? "";
                    byte[] packet = (byte[])protect.Invoke(form, new object[] { payload, sharedSecret });

                    Stopwatch sw = Stopwatch.StartNew();
                    string reply = SendPacket("127.0.0.1", port, packet, 3000, form, unprotect, sharedSecret);
                    sw.Stop();

                    Assert(reply.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0, "fast parity client should still be accepted while a slow client is connected. Reply was: " + reply);
                    Assert(sw.ElapsedMilliseconds < 5000, "slow client must not block parity-room accept loop until socket timeout");
                }

                Thread.Sleep(250);
                stopHost.Invoke(form, new object[] { session });
                return 0;
            }
            finally
            {
                IDisposable disposable = form as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
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

    private static string SendPacket(string host, int port, byte[] packet, int timeoutMs, object form, MethodInfo unprotect, string sharedSecret)
    {
        using (TcpClient client = new TcpClient())
        {
            client.ReceiveTimeout = timeoutMs;
            client.SendTimeout = timeoutMs;
            client.Connect(host, port);
            using (NetworkStream stream = client.GetStream())
            {
                byte[] payloadBytes = packet ?? new byte[0];
                byte[] lengthBytes = BitConverter.GetBytes(payloadBytes.Length);
                stream.Write(lengthBytes, 0, lengthBytes.Length);
                if (payloadBytes.Length > 0)
                    stream.Write(payloadBytes, 0, payloadBytes.Length);
                stream.Flush();
                client.Client.Shutdown(SocketShutdown.Send);
                byte[] responseLengthBytes = ReadExact(stream, 4);
                int responseLength = BitConverter.ToInt32(responseLengthBytes, 0);
                if (responseLength <= 0 || responseLength > 1024 * 1024)
                    throw new InvalidOperationException("Protected parity response length is invalid: " + responseLength);
                byte[] responsePacket = ReadExact(stream, responseLength);
                return Convert.ToString(unprotect.Invoke(form, new object[] { responsePacket, sharedSecret })) ?? "";
            }
        }
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
                throw new EndOfStreamException("Parity response ended early.");
            offset += read;
        }
        return buffer;
    }

    private static T GetFieldValue<T>(Type type, object instance, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(instance);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
