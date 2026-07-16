using System;
using System.Collections.Generic;
using System.Reflection;

internal static class ParityRoomSecurityHarness
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
                MethodInfo buildPayload = mainFormType.GetMethod("BuildParityRoomPayload", instanceFlags, null, new[] { sessionType, typeof(string), typeof(bool), typeof(bool) }, null);
                MethodInfo parsePayload = mainFormType.GetMethod("ParseParityRoomPayload", instanceFlags);
                MethodInfo validatePayload = mainFormType.GetMethod("ValidateAndRememberParityRoomPayload", instanceFlags);
                MethodInfo protectPayload = mainFormType.GetMethod("ProtectParityRoomPayload", instanceFlags);
                MethodInfo unprotectPayload = mainFormType.GetMethod("UnprotectParityRoomPayload", instanceFlags);
                if (sessionType == null || buildPayload == null || parsePayload == null || validatePayload == null || protectPayload == null || unprotectPayload == null)
                    throw new InvalidOperationException("Parity room security members were not found.");

                object session = Activator.CreateInstance(sessionType, true);
                SetField(sessionType, session, "RoomCode", "123456");
                SetField(sessionType, session, "SharedSecret", "shared-secret-value");
                SetField(sessionType, session, "LocalManifestText", "manifest-data");
                SetField(sessionType, session, "LocalOosSummaryText", "oos-summary");
                SetField(sessionType, session, "LocalOosMetadataText", "oos-metadata");
                SetField(sessionType, session, "LocalOosDeepReportText", "oos-deep");
                SetField(sessionType, session, "LocalOosRunbookText", "oos-runbook");
                SetField(sessionType, session, "LocalOosContaminationText", "oos-contamination");
                SetField(sessionType, session, "LocalOosSaveDumpText", "save-dump");
                SetField(sessionType, session, "LocalOosModifierDumpText", "modifier-dump");
                SetField(sessionType, session, "LocalOosErrorLogText", "error-log");

                string payload = Convert.ToString(buildPayload.Invoke(form, new object[] { session, "Tester", true, true })) ?? "";
                byte[] protectedPacket = (byte[])protectPayload.Invoke(form, new object[] { payload, "shared-secret-value" });
                string wireText = System.Text.Encoding.UTF8.GetString(protectedPacket);
                Assert(wireText.IndexOf("manifest-data", StringComparison.OrdinalIgnoreCase) < 0, "encrypted parity packet should not expose manifest plaintext on the wire");
                Assert(wireText.IndexOf("Tester", StringComparison.OrdinalIgnoreCase) < 0, "encrypted parity packet should not expose player plaintext on the wire");
                string unprotected = Convert.ToString(unprotectPayload.Invoke(form, new object[] { protectedPacket, "shared-secret-value" })) ?? "";
                Assert(unprotected == payload, "encrypted parity packet should decrypt back to the original payload");

                Dictionary<string, string> parsed = ParseDictionary(parsePayload.Invoke(form, new object[] { payload }));

                object[] validateArgs = new object[] { session, parsed, null };
                bool accepted = Convert.ToBoolean(validatePayload.Invoke(form, validateArgs));
                Assert(accepted, "signed parity payload should validate on first receive");
                Assert(String.IsNullOrEmpty(Convert.ToString(validateArgs[2]) ?? ""), "accepted parity payload should not report an error");

                object[] replayArgs = new object[] { session, parsed, null };
                bool replayAccepted = Convert.ToBoolean(validatePayload.Invoke(form, replayArgs));
                Assert(!replayAccepted, "replayed parity payload should be rejected");
                Assert((Convert.ToString(replayArgs[2]) ?? "").IndexOf("replayed", StringComparison.OrdinalIgnoreCase) >= 0, "replay rejection should explain the nonce replay");

                object tamperedSession = Activator.CreateInstance(sessionType, true);
                SetField(sessionType, tamperedSession, "RoomCode", "123456");
                SetField(sessionType, tamperedSession, "SharedSecret", "shared-secret-value");
                Dictionary<string, string> tampered = new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                tampered["manifest"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("tampered"));
                object[] tamperedArgs = new object[] { tamperedSession, tampered, null };
                bool tamperedAccepted = Convert.ToBoolean(validatePayload.Invoke(form, tamperedArgs));
                Assert(!tamperedAccepted, "tampered parity payload should fail signature validation");
                Assert((Convert.ToString(tamperedArgs[2]) ?? "").IndexOf("signature", StringComparison.OrdinalIgnoreCase) >= 0, "tampered parity payload should report a signature failure");

                byte[] tamperedPacket = (byte[])protectedPacket.Clone();
                tamperedPacket[20] ^= 0x5A;
                bool transportRejected = false;
                try
                {
                    unprotectPayload.Invoke(form, new object[] { tamperedPacket, "shared-secret-value" });
                }
                catch (TargetInvocationException ex)
                {
                    InvalidOperationException inner = ex.InnerException as InvalidOperationException;
                    transportRejected = inner != null && inner.Message.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                Assert(transportRejected, "tampered encrypted parity packet should fail transport authentication");
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

    private static Dictionary<string, string> ParseDictionary(object parsed)
    {
        Dictionary<string, string> fields = parsed as Dictionary<string, string>;
        if (fields == null)
            throw new InvalidOperationException("Parsed payload was not a string dictionary.");
        return fields;
    }

    private static void SetField(Type type, object instance, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(instance, value);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
