using System;
using System.Reflection;

internal static class UpdatesHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";

        try
        {
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;

            string json = "[" +
                "{" +
                "\"tag_name\":\"v0.4\"," +
                "\"html_url\":\"https://github.com/Danissemo/CK3MPS/releases/tag/v0.4\"," +
                "\"draft\":false," +
                "\"prerelease\":false," +
                "\"assets\":[" +
                    "{\"name\":\"CK3MPS-0.4.zip\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-0.4.zip\"}," +
                    "{\"name\":\"CK3MPS-0.4.zip.sha256\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-0.4.zip.sha256\"}," +
                    "{\"name\":\"CK3MPS-0.4.zip.manifest.json\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-0.4.zip.manifest.json\"}" +
                "]" +
                "}" +
            "]";

            Array releases = InvokeStatic(mainFormType, "ParseReleaseJson", staticFlags, json) as Array;
            Assert(releases != null && releases.Length == 1, "update JSON parser should deserialize release list");
            object releaseDto = releases.GetValue(0);
            Type releaseDtoType = releaseDto.GetType();
            Assert(GetFieldValue<string>(releaseDtoType, releaseDto, "TagName") == "v0.4", "update JSON parser should keep tag_name");
            Assert(!GetFieldValue<bool>(releaseDtoType, releaseDto, "Draft"), "stable release should not be draft");
            Assert(!GetFieldValue<bool>(releaseDtoType, releaseDto, "Prerelease"), "stable release should not be prerelease");

            Type releaseInfoType = mainFormType.GetNestedType("ReleaseInfo", BindingFlags.NonPublic);
            object releaseInfo = Activator.CreateInstance(releaseInfoType, true);
            SetFieldValue(releaseInfoType, releaseInfo, "TagName", "v0.4");
            InvokeStatic(mainFormType, "PickReleaseAssets", staticFlags, releaseDto, releaseInfo);

            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "AssetName") == "CK3MPS-0.4.zip", "update asset picker should require exact versioned zip");
            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "ChecksumUrl").EndsWith(".sha256", StringComparison.Ordinal), "update asset picker should keep checksum URL");
            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "ManifestUrl").EndsWith(".manifest.json", StringComparison.Ordinal), "update asset picker should keep manifest URL");

            InvokeStatic(mainFormType, "ValidateReleaseDownloadUrl", staticFlags, "https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-0.4.zip");
            AssertInvocationFails(mainFormType, "ValidateReleaseDownloadUrl", staticFlags, "http://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-0.4.zip", "non-HTTPS release URL should be rejected");
            AssertInvocationFails(mainFormType, "ValidateReleaseDownloadUrl", staticFlags, "https://evil.example.com/CK3MPS-0.4.zip", "foreign release host should be rejected");
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

    private static object InvokeStatic(Type type, string methodName, BindingFlags flags, params object[] parameters)
    {
        MethodInfo method = type.GetMethod(methodName, flags);
        if (method == null)
            throw new InvalidOperationException("Method not found: " + methodName);
        return method.Invoke(null, parameters);
    }

    private static void AssertInvocationFails(Type type, string methodName, BindingFlags flags, object parameter, string message)
    {
        try
        {
            InvokeStatic(type, methodName, flags, parameter);
        }
        catch (TargetInvocationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static T GetFieldValue<T>(Type type, object instance, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(instance);
    }

    private static void SetFieldValue(Type type, object instance, string fieldName, object value)
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
