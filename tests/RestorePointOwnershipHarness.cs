using System;
using System.Collections;
using System.Reflection;

internal static class RestorePointOwnershipHarness
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
                Type restorePointType = mainFormType.GetNestedType("RestorePointListItem", BindingFlags.NonPublic);
                MethodInfo validate = mainFormType.GetMethod("ValidateOwnedRestorePointDeletionSnapshot", instanceFlags);
                if (restorePointType == null || validate == null)
                    throw new InvalidOperationException("Restore point validation members were not found.");

                object owned = CreateRestorePointItem(restorePointType, "101", "CK3MPS before changes 2026-07-16 09:46:00", false);
                object staleForeign = CreateRestorePointItem(restorePointType, "102", "Windows Update checkpoint", true);

                IList validIds = (IList)validate.Invoke(form, new object[] { new[] { "101" }, CreateTypedItemArray(restorePointType, owned) });
                Assert(validIds.Count == 1 && Convert.ToInt32(validIds[0]) == 101, "restore-point validation accepts current CK3MPS-owned points");

                AssertThrowsInvalidOperation(validate, form, new object[] { new[] { "102" }, CreateTypedItemArray(restorePointType, staleForeign) }, "not owned by CK3MPS", "restore-point validation rejects stale IsCk3Mps flag when description ownership changed");
                AssertThrowsInvalidOperation(validate, form, new object[] { new[] { "999" }, CreateTypedItemArray(restorePointType, owned) }, "no longer present", "restore-point validation rejects deleted/missing current sequence ids");
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

    private static Array CreateTypedItemArray(Type itemType, params object[] items)
    {
        Array array = Array.CreateInstance(itemType, items.Length);
        for (int i = 0; i < items.Length; i++)
            array.SetValue(items[i], i);
        return array;
    }

    private static void SetField(Type type, object instance, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(instance, value);
    }

    private static void AssertThrowsInvalidOperation(MethodInfo method, object instance, object[] parameters, string expectedFragment, string message)
    {
        try
        {
            method.Invoke(instance, parameters);
            throw new InvalidOperationException(message);
        }
        catch (TargetInvocationException ex)
        {
            InvalidOperationException inner = ex.InnerException as InvalidOperationException;
            if (inner == null || inner.Message.IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) < 0)
                throw;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
