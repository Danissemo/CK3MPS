using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/Danissemo/CK3MPS/releases?per_page=10";
        private const string ReleasesPageUrl = "https://github.com/Danissemo/CK3MPS/releases";
        private bool updateCheckStarted;
        private bool updateCheckRunning;

        private sealed class ReleaseInfo
        {
            public string TagName;
            public string AssetName;
            public string DownloadUrl;
            public string ChecksumUrl;
            public string ReleasePageUrl;
        }

        private sealed class PreparedUpdate
        {
            public string WorkRoot;
            public string StagedAppRoot;
            public string DownloadedAssetPath;
        }

        [DataContract]
        private sealed class GitHubReleaseDto
        {
            [DataMember(Name = "tag_name")]
            public string TagName;

            [DataMember(Name = "html_url")]
            public string HtmlUrl;

            [DataMember(Name = "assets")]
            public GitHubAssetDto[] Assets;
        }

        [DataContract]
        private sealed class GitHubAssetDto
        {
            [DataMember(Name = "name")]
            public string Name;

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl;
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

                int comparison = VersionUtilities.CompareReleaseTags(release.TagName, AppVersion);
                if (comparison <= 0)
                {
                    if (manual)
                    {
                        if (comparison < 0)
                        {
                            Log("OK   Current build is newer than the latest published GitHub release. Current: " + AppVersion + ", latest published: " + release.TagName + ".");
                            MessageBox.Show("This CK3MPS build is newer than the latest published GitHub release.\r\n\r\nCurrent build: " + AppVersion + "\r\nLatest published release: " + release.TagName, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            Log("OK   CK3MPS is up to date. Current: " + AppVersion + ", latest: " + release.TagName + ".");
                            MessageBox.Show("CK3MPS is already up to date.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    return;
                }

                if (String.IsNullOrEmpty(release.ReleasePageUrl))
                    release.ReleasePageUrl = ReleasesPageUrl;

                if (String.IsNullOrEmpty(release.DownloadUrl))
                {
                    Log("WARN New CK3MPS release found (" + release.TagName + "), but it has no downloadable CK3MPS asset.");
                    return;
                }

                Log("WARN New CK3MPS release available: " + release.TagName + " (current " + AppVersion + ").");
                DialogResult result = MessageBox.Show(
                    "A new CK3MPS release is available.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName + "\r\n\r\nInstall it now? CK3MPS will download the update, close itself, replace the files, and reopen automatically.",
                    "CK3MPS update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result != DialogResult.Yes)
                {
                    Log("INFO Update skipped by user.");
                    return;
                }

                PreparedUpdate prepared = null;
                try
                {
                    prepared = await PrepareUpdatePackage(release);
                    LaunchSelfUpdater(prepared, release);
                    Log("OK   CK3MPS update " + release.TagName + " downloaded. Restarting through the updater.");
                    Application.Exit();
                }
                catch
                {
                    if (prepared != null && !String.IsNullOrEmpty(prepared.WorkRoot))
                        TryDeleteDirectory(prepared.WorkRoot);
                    throw;
                }
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
                GitHubReleaseDto releaseDto = ParseLatestReleaseJson(json);
                if (releaseDto == null)
                    return null;
                ReleaseInfo release = new ReleaseInfo();
                release.TagName = releaseDto.TagName ?? "";
                release.ReleasePageUrl = releaseDto.HtmlUrl ?? "";
                PickReleaseAsset(releaseDto, release);
                return release;
            }
        }

        private async Task<PreparedUpdate> PrepareUpdatePackage(ReleaseInfo release)
        {
            updateDownloadProgress.Value = 0;

            string workRoot = Path.Combine(Path.GetTempPath(), "CK3MPS-update-" + Guid.NewGuid().ToString("N"));
            string stagingRoot = Path.Combine(workRoot, "staged");
            Directory.CreateDirectory(workRoot);
            Directory.CreateDirectory(stagingRoot);

            string safeAssetName = MakeSafeFileName(String.IsNullOrWhiteSpace(release.AssetName) ? "CK3MPS-update.bin" : release.AssetName);
            string downloadPath = Path.Combine(workRoot, safeAssetName);

            Log("INFO Downloading CK3MPS update asset: " + safeAssetName + ".");
            await DownloadFileWithProgress(release.DownloadUrl, downloadPath);

            await VerifyDownloadedChecksumIfAvailable(release, downloadPath);

            string lowerName = safeAssetName.ToLowerInvariant();
            if (lowerName.EndsWith(".zip", StringComparison.Ordinal))
            {
                ZipFile.ExtractToDirectory(downloadPath, stagingRoot);
            }
            else if (lowerName.EndsWith(".exe", StringComparison.Ordinal))
            {
                File.Copy(downloadPath, Path.Combine(stagingRoot, "CK3MPS.exe"), true);
            }
            else
            {
                throw new InvalidOperationException("Unsupported update asset type: " + safeAssetName + ".");
            }

            string stagedAppRoot = FindStagedAppRoot(stagingRoot);
            if (String.IsNullOrEmpty(stagedAppRoot))
                throw new InvalidOperationException("Downloaded update package does not contain CK3MPS.exe.");

            Log("OK   Update package is ready: " + release.TagName + ".");
            return new PreparedUpdate
            {
                WorkRoot = workRoot,
                StagedAppRoot = stagedAppRoot,
                DownloadedAssetPath = downloadPath
            };
        }

        private Task DownloadFileWithProgress(string url, string destinationPath)
        {
            string target = SanitizeOfficialDownloadUrl(url);
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

            WebClient client = CreateGitHubWebClient();
            client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs args)
            {
                int percent = Math.Max(0, Math.Min(100, args.ProgressPercentage));
                updateDownloadProgress.Value = percent;
            };
            client.DownloadFileCompleted += delegate(object sender, AsyncCompletedEventArgs args)
            {
                client.Dispose();
                if (args.Cancelled)
                    completion.TrySetCanceled();
                else if (args.Error != null)
                    completion.TrySetException(args.Error);
                else
                    completion.TrySetResult(true);
            };

            client.DownloadFileAsync(new Uri(target), destinationPath);
            return completion.Task;
        }

        private async Task VerifyDownloadedChecksumIfAvailable(ReleaseInfo release, string downloadPath)
        {
            if (String.IsNullOrEmpty(release.ChecksumUrl))
            {
                Log("INFO Update checksum asset was not found; continuing without checksum verification.");
                return;
            }

            string checksumText = await Task.Run(delegate
            {
                using (WebClient client = CreateGitHubWebClient())
                {
                    return client.DownloadString(SanitizeOfficialDownloadUrl(release.ChecksumUrl));
                }
            });

            string expectedHash = ExtractSha256Hash(checksumText);
            if (String.IsNullOrEmpty(expectedHash))
            {
                Log("WARN Update checksum asset exists but does not contain a SHA-256 hash; continuing without checksum verification.");
                return;
            }

            string actualHash = ComputeSha256Hex(downloadPath);
            if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Downloaded update checksum mismatch.");

            Log("OK   Update checksum verified.");
        }

        private void LaunchSelfUpdater(PreparedUpdate prepared, ReleaseInfo release)
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string appExe = Application.ExecutablePath;
            string updaterScript = Path.Combine(prepared.WorkRoot, "apply-update.cmd");

            string script = BuildSelfUpdaterScript(
                Process.GetCurrentProcess().Id,
                prepared.StagedAppRoot,
                appDirectory,
                appExe,
                prepared.WorkRoot,
                release.TagName);

            File.WriteAllText(updaterScript, script, Utf8NoBom);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = updaterScript;
            startInfo.WorkingDirectory = prepared.WorkRoot;
            startInfo.UseShellExecute = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(startInfo);
        }

        private static string BuildSelfUpdaterScript(int processId, string stagedAppRoot, string appDirectory, string appExe, string workRoot, string releaseTag)
        {
            StringBuilder script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal enableextensions");
            script.AppendLine("set \"CK3MPS_PID=" + processId + "\"");
            script.AppendLine("set \"CK3MPS_SRC=" + stagedAppRoot + "\"");
            script.AppendLine("set \"CK3MPS_DST=" + appDirectory + "\"");
            script.AppendLine("set \"CK3MPS_EXE=" + appExe + "\"");
            script.AppendLine("set \"CK3MPS_TMP=" + workRoot + "\"");
            script.AppendLine("title CK3MPS updater " + releaseTag);
            script.AppendLine(":wait_for_ck3mps");
            script.AppendLine("tasklist /FI \"PID eq %CK3MPS_PID%\" /NH | findstr /I /C:\"CK3MPS.exe\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto wait_for_ck3mps");
            script.AppendLine(")");
            script.AppendLine("robocopy \"%CK3MPS_SRC%\" \"%CK3MPS_DST%\" /E /R:12 /W:1 /NFL /NDL /NJH /NJS /NP >nul");
            script.AppendLine("if errorlevel 8 (");
            script.AppendLine("  start \"\" \"%CK3MPS_EXE%\"");
            script.AppendLine("  exit /b 1");
            script.AppendLine(")");
            script.AppendLine("start \"\" \"%CK3MPS_EXE%\"");
            script.AppendLine("timeout /t 2 /nobreak >nul");
            script.AppendLine("rd /s /q \"%CK3MPS_TMP%\" >nul 2>nul");
            script.AppendLine("del \"%~f0\" >nul 2>nul");
            return script.ToString();
        }

        private static string FindStagedAppRoot(string stagingRoot)
        {
            string direct = Path.Combine(stagingRoot, "CK3MPS.exe");
            if (File.Exists(direct))
                return stagingRoot;

            foreach (string file in Directory.GetFiles(stagingRoot, "CK3MPS.exe", SearchOption.AllDirectories))
                return Path.GetDirectoryName(file);

            return "";
        }

        private static string MakeSafeFileName(string fileName)
        {
            string safe = fileName ?? "";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                safe = safe.Replace(invalid, '_');
            return String.IsNullOrWhiteSpace(safe) ? "CK3MPS-update.bin" : safe;
        }

        private static string ComputeSha256Hex(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string ExtractSha256Hash(string text)
        {
            string value = text ?? "";
            for (int i = 0; i <= value.Length - 64; i++)
            {
                string candidate = value.Substring(i, 64);
                if (IsHex(candidate))
                    return candidate.ToLowerInvariant();
            }
            return "";
        }

        private static bool IsHex(string value)
        {
            if (String.IsNullOrEmpty(value))
                return false;

            foreach (char ch in value)
            {
                bool digit = ch >= '0' && ch <= '9';
                bool lower = ch >= 'a' && ch <= 'f';
                bool upper = ch >= 'A' && ch <= 'F';
                if (!digit && !lower && !upper)
                    return false;
            }

            return true;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!String.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private void OpenOfficialReleasePage(string url)
        {
            string target = SanitizeOfficialReleasePageUrl(url);
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = target;
            info.UseShellExecute = true;
            Process.Start(info);
            Log("INFO Opened official CK3MPS release page: " + target);
        }

        private WebClient CreateGitHubWebClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "CK3MPS/" + AppVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static GitHubReleaseDto ParseLatestReleaseJson(string json)
        {
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubReleaseDto[]));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "[]")))
                {
                    GitHubReleaseDto[] releases = serializer.ReadObject(stream) as GitHubReleaseDto[];
                    return releases != null && releases.Length > 0 ? releases[0] : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeOfficialReleasePageUrl(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(String.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url, UriKind.Absolute, out parsed))
                return ReleasesPageUrl;
            if (!String.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return ReleasesPageUrl;
            if (!String.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(parsed.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
                return ReleasesPageUrl;
            return parsed.AbsoluteUri;
        }

        private static string SanitizeOfficialDownloadUrl(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out parsed))
                throw new InvalidOperationException("Invalid update download URL.");
            if (!String.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update download URL must use HTTPS.");
            if (!String.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(parsed.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update download URL is not an official CK3MPS GitHub release asset.");
            return parsed.AbsoluteUri;
        }

        private static void PickReleaseAsset(GitHubReleaseDto releaseDto, ReleaseInfo release)
        {
            string fallbackName = "";
            string fallbackUrl = "";
            foreach (GitHubAssetDto asset in releaseDto.Assets ?? new GitHubAssetDto[0])
            {
                string name = asset != null ? asset.Name ?? "" : "";
                string url = asset != null ? asset.BrowserDownloadUrl ?? "" : "";
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
                    release.ChecksumUrl = FindChecksumUrl(releaseDto, release.AssetName);
                    return;
                }
            }

            release.AssetName = fallbackName;
            release.DownloadUrl = fallbackUrl;
            if (!String.IsNullOrEmpty(release.AssetName))
                release.ChecksumUrl = FindChecksumUrl(releaseDto, release.AssetName);
        }

        private static string FindChecksumUrl(GitHubReleaseDto releaseDto, string assetName)
        {
            string wanted = (assetName + ".sha256").ToLowerInvariant();
            foreach (GitHubAssetDto asset in releaseDto.Assets ?? new GitHubAssetDto[0])
            {
                string name = asset != null ? asset.Name ?? "" : "";
                string url = asset != null ? asset.BrowserDownloadUrl ?? "" : "";
                string lower = name.ToLowerInvariant();
                if (lower == wanted || (lower.Contains("sha256") && lower.EndsWith(".txt", StringComparison.Ordinal)))
                    return url;
            }
            return "";
        }
    }
}