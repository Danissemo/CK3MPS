using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/Danissemo/CK3MPS/releases?per_page=10";
        private bool updateCheckStarted;
        private bool updateCheckRunning;

        private sealed class ReleaseInfo
        {
            public string TagName;
            public string AssetName;
            public string DownloadUrl;
            public string ChecksumUrl;
        }

        private void CheckForUpdatesOnStartup()
        {
            if (updateCheckStarted)
                return;

            if (!updateCheckOnStartup)
            {
                Log("INFO Startup update check is disabled in Advanced settings.");
                return;
            }

            updateCheckStarted = true;
            CheckForUpdates(false);
        }

        private void CheckForUpdatesManual()
        {
            CheckForUpdates(true);
        }

        private async void CheckForUpdates(bool manual)
        {
            if (updateCheckRunning)
                return;

            updateCheckRunning = true;
            updateButton.Enabled = false;
            updateDownloadProgress.Value = 0;

            try
            {
                if (manual)
                    Log("INFO Checking GitHub Releases for CK3MPS updates.");

                ReleaseInfo release = await Task.Run(delegate { return FetchLatestRelease(); });
                if (release == null || String.IsNullOrEmpty(release.TagName))
                {
                    if (manual)
                        Log("WARN Update check did not find a published GitHub release.");
                    return;
                }

                if (!VersionUtilities.IsNewerRelease(release.TagName, AppVersion))
                {
                    if (manual)
                    {
                        Log("OK   CK3MPS is up to date. Current: " + AppVersion + ", latest: " + release.TagName + ".");
                        MessageBox.Show("CK3MPS is already up to date.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                if (String.IsNullOrEmpty(release.DownloadUrl))
                {
                    Log("WARN New CK3MPS release found (" + release.TagName + "), but it has no downloadable CK3MPS asset.");
                    return;
                }

                Log("WARN New CK3MPS release available: " + release.TagName + " (current " + AppVersion + ").");
                DialogResult result = MessageBox.Show(
                    "A new CK3MPS release is available.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName + "\r\n\r\nDownload and install it now?",
                    "CK3MPS update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result != DialogResult.Yes)
                {
                    Log("INFO Update skipped by user.");
                    return;
                }

                await DownloadAndStartUpdater(release);
            }
            catch (Exception ex)
            {
                if (manual)
                    MessageBox.Show("Update check failed:\r\n" + ex.Message, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Update check failed: " + ex.Message);
            }
            finally
            {
                updateCheckRunning = false;
                updateButton.Enabled = true;
            }
        }

        private ReleaseInfo FetchLatestRelease()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (WebClient client = CreateGitHubWebClient())
            {
                string json = client.DownloadString(ReleasesApiUrl);
                ReleaseInfo release = new ReleaseInfo();
                release.TagName = JsonStringValue(json, "tag_name");
                PickReleaseAsset(json, release);
                return release;
            }
        }

        private WebClient CreateGitHubWebClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "CK3MPS/" + AppVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static string JsonStringValue(string json, string key)
        {
            Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? UnescapeJsonString(match.Groups[1].Value) : "";
        }

        private static void PickReleaseAsset(string json, ReleaseInfo release)
        {
            MatchCollection matches = Regex.Matches(
                json ?? "",
                "\"name\"\\s*:\\s*\"([^\"]+)\".*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            string fallbackName = "";
            string fallbackUrl = "";
            foreach (Match match in matches)
            {
                string name = UnescapeJsonString(match.Groups[1].Value);
                string url = UnescapeJsonString(match.Groups[2].Value);
                string lower = name.ToLowerInvariant();

                if (lower == "ck3mps.exe")
                {
                    fallbackName = name;
                    fallbackUrl = url;
                }

                if (lower.StartsWith("ck3mps-", StringComparison.Ordinal) && lower.EndsWith(".zip", StringComparison.Ordinal))
                {
                    release.AssetName = name;
                    release.DownloadUrl = url;
                    release.ChecksumUrl = FindChecksumUrl(json, release.AssetName);
                    return;
                }
            }

            release.AssetName = fallbackName;
            release.DownloadUrl = fallbackUrl;
            if (!String.IsNullOrEmpty(release.AssetName))
                release.ChecksumUrl = FindChecksumUrl(json, release.AssetName);
        }

        private static string FindChecksumUrl(string json, string assetName)
        {
            MatchCollection matches = Regex.Matches(
                json ?? "",
                "\"name\"\\s*:\\s*\"([^\"]+)\".*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            string wanted = (assetName + ".sha256").ToLowerInvariant();
            foreach (Match match in matches)
            {
                string name = UnescapeJsonString(match.Groups[1].Value);
                string url = UnescapeJsonString(match.Groups[2].Value);
                string lower = name.ToLowerInvariant();
                if (lower == wanted || (lower.Contains("sha256") && lower.EndsWith(".txt", StringComparison.Ordinal)))
                    return url;
            }
            return "";
        }

        private static string UnescapeJsonString(string value)
        {
            return (value ?? "").Replace("\\/", "/").Replace("\\\"", "\"");
        }

        private async Task DownloadAndStartUpdater(ReleaseInfo release)
        {
            string workDir = Path.Combine(Path.GetTempPath(), "CK3MPS-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            string assetName = String.IsNullOrEmpty(release.AssetName) ? "CK3MPS-update.bin" : SafeFileName(release.AssetName);
            string downloadPath = Path.Combine(workDir, assetName);

            Log("INFO Downloading update asset: " + release.AssetName);
            using (WebClient client = CreateGitHubWebClient())
            {
                client.DownloadProgressChanged += delegate (object sender, DownloadProgressChangedEventArgs e)
                {
                    updateDownloadProgress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                };
                await client.DownloadFileTaskAsync(new Uri(release.DownloadUrl), downloadPath);
            }

            if (!File.Exists(downloadPath) || new FileInfo(downloadPath).Length == 0)
                throw new InvalidOperationException("Downloaded update asset is empty.");

            if (!String.IsNullOrEmpty(release.ChecksumUrl))
            {
                using (WebClient client = CreateGitHubWebClient())
                {
                    string checksumText = await client.DownloadStringTaskAsync(new Uri(release.ChecksumUrl));
                    VerifyDownloadedSha256(downloadPath, checksumText);
                    Log("OK   Update SHA256 checksum verified.");
                }
            }
            else
            {
                throw new InvalidOperationException("No SHA256 checksum asset found for this release. Automatic update was stopped for safety.");
            }

            string script = Path.Combine(workDir, "apply-update.ps1");
            File.WriteAllText(script, BuildUpdaterScript(), Encoding.UTF8);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "powershell.exe";
            info.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArg(script)
                + " -Source " + QuoteArg(downloadPath)
                + " -Target " + QuoteArg(Application.ExecutablePath)
                + " -ProcessId " + Process.GetCurrentProcess().Id;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;

            Process.Start(info);
            Log("INFO Updater started. CK3MPS will close and restart after the executable is replaced.");
            Application.Exit();
        }

        private void VerifyDownloadedSha256(string path, string checksumText)
        {
            Match match = Regex.Match(checksumText ?? "", "[A-Fa-f0-9]{64}");
            if (!match.Success)
                throw new InvalidOperationException("SHA256 checksum asset did not contain a 64-character hash.");

            string expected = match.Value.ToLowerInvariant();
            string actual = Sha256File(path);
            if (!String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA256 mismatch. Expected " + expected + ", got " + actual + ".");
        }

        private string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string BuildUpdaterScript()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("param([string]$Source, [string]$Target, [int]$ProcessId)");
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$WorkDir = Split-Path -Parent $Source");
            sb.AppendLine("Wait-Process -Id $ProcessId -Timeout 45 -ErrorAction SilentlyContinue");
            sb.AppendLine("Start-Sleep -Milliseconds 500");
            sb.AppendLine("$Candidate = $Source");
            sb.AppendLine("if ([System.IO.Path]::GetExtension($Source) -ieq '.zip') {");
            sb.AppendLine("    $ExtractDir = Join-Path $WorkDir 'extract'");
            sb.AppendLine("    if (Test-Path $ExtractDir) { Remove-Item -LiteralPath $ExtractDir -Recurse -Force }");
            sb.AppendLine("    Expand-Archive -LiteralPath $Source -DestinationPath $ExtractDir -Force");
            sb.AppendLine("    $Candidate = Get-ChildItem -LiteralPath $ExtractDir -Recurse -Filter 'CK3MPS.exe' | Select-Object -First 1 -ExpandProperty FullName");
            sb.AppendLine("    if (-not $Candidate) { throw 'CK3MPS.exe was not found in the release archive.' }");
            sb.AppendLine("}");
            sb.AppendLine("$Backup = $Target + '.bak'");
            sb.AppendLine("if (Test-Path -LiteralPath $Target) { Copy-Item -LiteralPath $Target -Destination $Backup -Force }");
            sb.AppendLine("Copy-Item -LiteralPath $Candidate -Destination $Target -Force");
            sb.AppendLine("Start-Process -FilePath $Target");
            sb.AppendLine("Start-Sleep -Seconds 2");
            sb.AppendLine("Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue");
            return sb.ToString();
        }
    }
}
