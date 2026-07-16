using System;
using System.Reflection;

internal static class ParityRoomConsentHarness
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
                MethodInfo confirm = mainFormType.GetMethod("TryConfirmParityRoomOosShare", instanceFlags);
                if (sessionType == null || confirm == null)
                    throw new InvalidOperationException("Parity room consent members were not found.");

                object deniedSession = Activator.CreateInstance(sessionType, true);
                Environment.SetEnvironmentVariable("CK3MPS_TEST_RAW_OOS_CONSENT", "deny");
                bool denied = Convert.ToBoolean(confirm.Invoke(form, new[] { deniedSession }));
                Assert(!denied, "raw OOS parity-room share should stop without explicit consent");
                Assert(!GetFieldValue<bool>(sessionType, deniedSession, "RawOosShareConsented"), "denied consent should not mark the room session as approved");
                Assert(!GetFieldValue<bool>(sessionType, deniedSession, "RawOosDumpsShareConsented"), "raw dump sharing must remain off after denied consent");

                object allowedSession = Activator.CreateInstance(sessionType, true);
                Environment.SetEnvironmentVariable("CK3MPS_TEST_RAW_OOS_CONSENT", "allow");
                bool allowed = Convert.ToBoolean(confirm.Invoke(form, new[] { allowedSession }));
                Assert(allowed, "raw OOS parity-room share should continue after explicit consent");
                Assert(GetFieldValue<bool>(sessionType, allowedSession, "RawOosShareConsented"), "approved consent should be remembered for the room session");
                Assert(GetFieldValue<bool>(sessionType, allowedSession, "OosReportsShareConsented"), "forced explicit consent should allow OOS reports");
                Assert(GetFieldValue<bool>(sessionType, allowedSession, "RawOosDumpsShareConsented"), "forced explicit consent should allow raw dumps for the security harness");

                Environment.SetEnvironmentVariable("CK3MPS_TEST_RAW_OOS_CONSENT", "deny");
                bool stillAllowed = Convert.ToBoolean(confirm.Invoke(form, new[] { allowedSession }));
                Assert(stillAllowed, "once approved, the same room session should not prompt again for every OOS send");
                return 0;
            }
            finally
            {
                Environment.SetEnvironmentVariable("CK3MPS_TEST_RAW_OOS_CONSENT", null);
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
