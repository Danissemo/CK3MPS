using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal static class RestorePointOwnershipHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
        string tempRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-restore-point-ownership-" + Guid.NewGuid().ToString("N"));
        string stateRoot = Path.Combine(tempRoot, "CK3MPS_State");
        string assemblyCopyPath = Path.Combine(tempRoot, "CK3MPS.exe");

        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            Directory.CreateDirectory(stateRoot);
            File.Copy(assemblyPath, assemblyCopyPath, true);

            Assembly assembly = Assembly.LoadFrom(assemblyCopyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            Type itemType = mainFormType.GetNestedType("RestorePointListItem", BindingFlags.NonPublic);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);

            try
            {
                SetField(mainFormType, form, "stabilizerRoot", stateRoot, flags);
                SetField(mainFormType, form, "liveLogWritesEnabled", false, flags);

                string validOperationId = "11111111111111111111111111111111";
                string validDescription = "CK3MPS before changes 2026-07-17 05:00:00 [CK3MPS-RP:" + validOperationId + "]";
                object validItem = CreateItem(itemType, "101", "7/17/2026 5:00:00 AM", validDescription);
                Invoke(mainFormType, form, "AppendRestorePointOwnershipRecord", flags, validItem, validOperationId);
                Assert(Validate(mainFormType, form, flags, validItem), "manifest-backed CK3MPS restore point should validate");
                itemType.GetField("IsCk3Mps").SetValue(validItem, true);

                object prefixOnly = CreateItem(itemType, "102", "7/17/2026 5:01:00 AM", "CK3MPS before changes 2026-07-17 05:01:00");
                Assert(!Validate(mainFormType, form, flags, prefixOnly), "prefix-only legacy restore point must not validate");

                object similarName = CreateItem(itemType, "103", "7/17/2026 5:02:00 AM", "CK3MPS before changes manual user point [CK3MPS-RP:22222222222222222222222222222222]");
                Assert(!Validate(mainFormType, form, flags, similarName), "manual point with plausible CK3MPS marker but no manifest row must not validate");

                object changedAfterUi = CreateItem(itemType, "101", "7/17/2026 5:00:00 AM", "CK3MPS before changes tampered [CK3MPS-RP:" + validOperationId + "]");
                Assert(!Validate(mainFormType, form, flags, changedAfterUi), "changed description after UI load must not validate");

                object changedTime = CreateItem(itemType, "101", "7/17/2026 5:00:01 AM", validDescription);
                Assert(!Validate(mainFormType, form, flags, changedTime), "changed creation time after UI load must not validate");

                object nonNumeric = CreateItem(itemType, "not-a-number", "7/17/2026 5:00:00 AM", validDescription);
                Assert(!Validate(mainFormType, form, flags, nonNumeric), "non-numeric sequence number must not validate");

                Array currentItems = Array.CreateInstance(itemType, 2);
                currentItems.SetValue(validItem, 0);
                currentItems.SetValue(prefixOnly, 1);
                object validated = Invoke(mainFormType, form, "ValidateOwnedRestorePointDeletionSnapshot", flags, new string[] { "101", "102", "missing" }, currentItems);
                IList list = (IList)validated;
                Assert(list.Count == 1 && Convert.ToInt32(list[0]) == 101, "bulk deletion must skip unowned/missing restore points and keep only the app-owned point");

                bool canCheckOwned = Convert.ToBoolean(Invoke(mainFormType, form, "ShouldAllowRestorePointItemCheck", flags, validItem, CheckState.Checked));
                bool canCheckOther = Convert.ToBoolean(Invoke(mainFormType, form, "ShouldAllowRestorePointItemCheck", flags, prefixOnly, CheckState.Checked));
                Assert(canCheckOwned, "owned restore point should remain checkable after the UI marks it app-owned");
                Assert(!canCheckOther, "unowned restore point should be read-only in the delete UI");

                string manifest = Convert.ToString(Invoke(mainFormType, form, "RestorePointOwnershipManifestFile", flags));
                string originalManifest = File.ReadAllText(manifest, Encoding.UTF8);
                File.WriteAllText(manifest, originalManifest.Replace(validDescription, validDescription + " tampered"), Encoding.UTF8);
                Assert(!Validate(mainFormType, form, flags, validItem), "tampered manifest must not validate");

                File.WriteAllText(manifest, originalManifest + originalManifest.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)[1] + Environment.NewLine, Encoding.UTF8);
                Assert(!Validate(mainFormType, form, flags, validItem), "duplicate manifest ownership rows must not validate");

                File.Delete(manifest);
                Assert(!Validate(mainFormType, form, flags, validItem), "missing manifest must not validate");

                File.WriteAllText(manifest, "corrupt", Encoding.UTF8);
                Assert(!Validate(mainFormType, form, flags, validItem), "corrupt manifest must not validate");

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
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
    }

    private static object CreateItem(Type itemType, string sequence, string creation, string description)
    {
        object item = Activator.CreateInstance(itemType, true);
        itemType.GetField("SequenceNumber").SetValue(item, sequence);
        itemType.GetField("CreationTime").SetValue(item, creation);
        itemType.GetField("Description").SetValue(item, description);
        itemType.GetField("IsCk3Mps").SetValue(item, false);
        return item;
    }

    private static bool Validate(Type type, object instance, BindingFlags flags, object item)
    {
        object[] parameters = new object[] { item, null };
        return Convert.ToBoolean(Invoke(type, instance, "TryValidateOwnedRestorePointForDeletion", flags, parameters));
    }

    private static object Invoke(Type type, object instance, string methodName, BindingFlags flags, params object[] parameters)
    {
        MethodInfo method = type.GetMethod(methodName, flags);
        if (method == null)
            throw new InvalidOperationException("Method not found: " + methodName);
        return method.Invoke(instance, parameters);
    }

    private static void SetField(Type type, object instance, string fieldName, object value, BindingFlags flags)
    {
        FieldInfo field = type.GetField(fieldName, flags);
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
