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
                "\"assets\":[" +
                    "{\"name\":\"CK3MPS-v0.4.zip\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-v0.4.zip\"}," +
                    "{\"name\":\"CK3MPS-v0.4.zip.sha256\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS-v0.4.zip.sha256\"}," +
                    "{\"name\":\"CK3MPS.exe\",\"browser_download_url\":\"https://github.com/Danissemo/CK3MPS/releases/download/v0.4/CK3MPS.exe\"}" +
                "]" +
                "}" +
            "]";

            object releaseDto = InvokeStatic(mainFormType, "ParseLatestReleaseJson", staticFlags, json);
            Assert(releaseDto != null, "update JSON parser should deserialize latest release");

            Type releaseDtoType = releaseDto.GetType();
            Assert(GetFieldValue<string>(releaseDtoType, releaseDto, "TagName") == "v0.4", "update JSON parser should keep tag_name");
            Assert(GetFieldValue<string>(releaseDtoType, releaseDto, "HtmlUrl") == "https://github.com/Danissemo/CK3MPS/releases/tag/v0.4", "update JSON parser should keep html_url");

            Type releaseInfoType = mainFormType.GetNestedType("ReleaseInfo", BindingFlags.NonPublic);
            object releaseInfo = Activator.CreateInstance(releaseInfoType, true);
            InvokeStatic(mainFormType, "PickReleaseAsset", staticFlags, releaseDto, releaseInfo);

            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "AssetName") == "CK3MPS-v0.4.zip", "update asset picker should choose the release zip first");
            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "DownloadUrl").IndexOf("CK3MPS-v0.4.zip", StringComparison.OrdinalIgnoreCase) >= 0, "update asset picker should keep the zip download URL");
            Assert(GetFieldValue<string>(releaseInfoType, releaseInfo, "ChecksumUrl").IndexOf(".sha256", StringComparison.OrdinalIgnoreCase) >= 0, "update asset picker should keep the checksum URL");

            string safeUrl = Convert.ToString(InvokeStatic(mainFormType, "SanitizeOfficialReleasePageUrl", staticFlags, "https://github.com/Danissemo/CK3MPS/releases/tag/v0.4")) ?? "";
            string downgradedScheme = Convert.ToString(InvokeStatic(mainFormType, "SanitizeOfficialReleasePageUrl", staticFlags, "http://github.com/Danissemo/CK3MPS/releases/tag/v0.4")) ?? "";
            string downgradedHost = Convert.ToString(InvokeStatic(mainFormType, "SanitizeOfficialReleasePageUrl", staticFlags, "https://evil.example.com/releases")) ?? "";
            string fallback = "https://github.com/Danissemo/CK3MPS/releases";

            Assert(safeUrl == "https://github.com/Danissemo/CK3MPS/releases/tag/v0.4", "release page sanitizer should keep allowed GitHub HTTPS URLs");
            Assert(downgradedScheme == fallback, "release page sanitizer should reject non-HTTPS URLs");
            Assert(downgradedHost == fallback, "release page sanitizer should reject non-GitHub hosts");
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
