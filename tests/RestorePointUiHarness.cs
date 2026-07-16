using System;
using System.Reflection;
using System.Windows.Forms;

internal static class RestorePointUiHarness
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
                Form windowsForm = form as Form;
                if (windowsForm == null)
                    throw new InvalidOperationException("MainForm is not a Windows Form.");
                IntPtr handle = windowsForm.Handle;

                Type restorePointType = mainFormType.GetNestedType("RestorePointListItem", BindingFlags.NonPublic);
                MethodInfo shouldAllow = mainFormType.GetMethod("ShouldAllowRestorePointItemCheck", instanceFlags);
                MethodInfo updateButton = mainFormType.GetMethod("UpdateRestorePointDeleteButtonState", instanceFlags);
                if (restorePointType == null || shouldAllow == null || updateButton == null)
                    throw new InvalidOperationException("Restore point UI members were not found.");

                object owned = CreateRestorePointItem(restorePointType, "101", "CK3MPS before changes 2026-07-16 09:46:00", true);
                object foreign = CreateRestorePointItem(restorePointType, "102", "Windows Update checkpoint", false);

                bool allowOwned = Convert.ToBoolean(shouldAllow.Invoke(form, new object[] { owned, CheckState.Checked }));
                bool allowForeign = Convert.ToBoolean(shouldAllow.Invoke(form, new object[] { foreign, CheckState.Checked }));
                bool allowUncheckForeign = Convert.ToBoolean(shouldAllow.Invoke(form, new object[] { foreign, CheckState.Unchecked }));
                Assert(allowOwned, "owned restore point should stay checkable in the UI");
                Assert(!allowForeign, "foreign restore point should stay read-only in the UI");
                Assert(allowUncheckForeign, "unchecking or leaving foreign item clear should stay allowed");

                CheckedListBox listBox = (CheckedListBox)GetFieldValue(mainFormType, form, "restorePointsListBox");
                Button deleteButton = (Button)GetFieldValue(mainFormType, form, "deleteSelectedRestorePointsButton");
                listBox.Items.Add(foreign);
                listBox.Items.Add(owned);

                SetFieldValue(mainFormType, form, "restorePointsLoading", false);
                updateButton.Invoke(form, null);
                Assert(!deleteButton.Enabled, "delete button should stay disabled with no checked restore points");

                listBox.SetItemChecked(1, true);
                updateButton.Invoke(form, null);
                Assert(deleteButton.Enabled, "delete button should enable when a CK3MPS-owned restore point is checked");

                listBox.SetItemChecked(1, false);
                updateButton.Invoke(form, null);
                Assert(!deleteButton.Enabled, "delete button should disable again when the owned restore point is unchecked");
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

    private static object CreateRestorePointItem(Type itemType, string sequenceNumber, string description, bool isCk3Mps)
    {
        object item = Activator.CreateInstance(itemType, true);
        SetField(itemType, item, "SequenceNumber", sequenceNumber);
        SetField(itemType, item, "CreationTime", "2026-07-16 09:46:00");
        SetField(itemType, item, "Description", description);
        SetField(itemType, item, "IsCk3Mps", isCk3Mps);
        return item;
    }

    private static object GetFieldValue(Type type, object instance, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        return field.GetValue(instance);
    }

    private static void SetFieldValue(Type type, object instance, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(instance, value);
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
