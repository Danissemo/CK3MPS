using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/Danissemo/CK3MPS/releases?per_page=10";
        private const string ReleasesPageUrl = "https://github.com/Danissemo/CK3MPS/releases";
        private const string ReleaseDownloadPrefix = "/Danissemo/CK3MPS/releases/download/";
        private bool updateCheckStarted;
        private bool updateCheckRunning;
        private string pendingUpdateRequestPath;

        private sealed class ReleaseInfo
        {
            public string TagName;
            public string AssetName;
            public string DownloadUrl;
            public string ChecksumUrl;
            public string ManifestUrl;
            public string ReleasePageUrl;
        }

        [DataContract]
        private sealed class GitHubReleaseDto
        {
            [DataMember(Name = "tag_name")] public string TagName;
            [DataMember(Name = "html_url")] public string HtmlUrl;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Prerelease;
            [DataMember(Name = "assets")] public GitHubAssetDto[] Assets;
        }

        [DataContract]
        private sealed class GitHubAssetDto
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string BrowserDownloadUrl;
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
            if (!String.IsNullOrEmpty(pendingUpdateRequestPath) && File.Exists(pendingUpdateRequestPath))
            {
                DialogResult pending = MessageBox.Show(
                    "A verified CK3MPS update is ready. Restart now to install it?",
                    "CK3MPS update ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (pending == DialogResult.Yes)
                    LaunchUpdaterAndExit(pendingUpdateRequestPath);
                return;
            }
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
                    Log("INFO Checking the signed CK3MPS release channel.");

                ReleaseInfo release = await Task.Run(delegate { return FetchLatestRelease(); });
                if (release == null || String.IsNullOrEmpty(release.TagName))
                {
                    if (manual)
                        Log("WARN Update check did not find a stable published GitHub release.");
                    return;
                }

                int comparison = VersionUtilities.CompareReleaseTags(release.TagName, AppVersion);
                if (comparison <= 0)
                {
                    if (manual)
                    {
                        string message = comparison < 0
                            ? "This CK3MPS build is newer than the latest stable release.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName
                            : "CK3MPS is already up to date.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName;
                        Log("OK   No update is required. Current: " + AppVersion + ", latest: " + release.TagName + ".");
                        MessageBox.Show(message, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                ValidateReleaseAssets(release);
                DialogResult result = MessageBox.Show(
                    "A signed CK3MPS update is available.\r\n\r\nCurrent: " + AppVersion + "\r\nNew: " + release.TagName + "\r\n\r\nDownload and verify the update now?",
                    "CK3MPS update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    Log("INFO Update download skipped by user.");
                    return;
                }

                pendingUpdateRequestPath = await DownloadAndPrepareUpdateAsync(release);
                updateDownloadProgress.Value = 100;
                Log("OK   Update package, manifest, checksum and publisher requirements are ready for updater verification.");

                DialogResult restart = MessageBox.Show(
                    "The update is staged. CK3MPS must close before files are replaced.\r\n\r\nRestart now? Choose No to keep using this session and install later from Check updates.",
                    "CK3MPS update ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (restart == DialogResult.Yes)
                    LaunchUpdaterAndExit(pendingUpdateRequestPath);
                else
                    Log("INFO Verified update staged; restart deferred by user.");
            }
            catch (Exception ex)
            {
                if (manual)
                    MessageBox.Show("Update failed:\r\n" + ex.Message, "CK3MPS updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Update failed: " + ex.Message);
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
                GitHubReleaseDto[] releases = ParseReleaseJson(json);
                foreach (GitHubReleaseDto releaseDto in releases)
                {
                    if (releaseDto == null || releaseDto.Draft || releaseDto.Prerelease)
                        continue;
                    ReleaseInfo release = new ReleaseInfo();
                    release.TagName = releaseDto.TagName ?? "";
                    release.ReleasePageUrl = releaseDto.HtmlUrl ?? ReleasesPageUrl;
                    PickReleaseAssets(releaseDto, release);
                    return release;
                }
                return null;
            }
        }

        private async Task<string> DownloadAndPrepareUpdateAsync(ReleaseInfo release)
        {
            CleanupOldUpdateStaging();
            string stagingRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CK3MPS", "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            RejectStagingReparsePoint(stagingRoot);

            string packagePath = Path.Combine(stagingRoot, release.AssetName);
            string checksumPath = packagePath + ".sha256";
            string manifestPath = Path.Combine(stagingRoot, release.AssetName + ".manifest.json");

            using (WebClient client = CreateGitHubWebClient())
            {
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                {
                    int value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                    BeginInvoke((MethodInvoker)delegate { updateDownloadProgress.Value = value; });
                };
                await client.DownloadFileTaskAsync(new Uri(release.DownloadUrl), packagePath);
            }
            using (WebClient client = CreateGitHubWebClient())
            {
                await client.DownloadFileTaskAsync(new Uri(release.ChecksumUrl), checksumPath);
                await client.DownloadFileTaskAsync(new Uri(release.ManifestUrl), manifestPath);
            }

            string checksum = ParseChecksum(File.ReadAllText(checksumPath), release.AssetName);
            VerifyFileHash(packagePath, checksum);

            string updaterCopy = Path.Combine(stagingRoot, "CK3MPS.Updater.exe");
            File.Copy(Application.ExecutablePath, updaterCopy, false);
            string requestPath = Path.Combine(stagingRoot, "update-request.json");
            SafeUpdater.UpdateRequest request = new SafeUpdater.UpdateRequest
            {
                PackagePath = packagePath,
                ManifestPath = manifestPath,
                InstallRoot = Application.StartupPath,
                StagingRoot = stagingRoot,
                CurrentVersion = AppVersion,
                TargetVersion = release.TagName,
                AllowDowngrade = false
            };
            SafeUpdater.WriteJson(requestPath, request);
            return requestPath;
        }

        private void LaunchUpdaterAndExit(string requestPath)
        {
            string updaterPath = Path.Combine(Path.GetDirectoryName(requestPath), "CK3MPS.Updater.exe");
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("The staged updater process is missing.", updaterPath);
            ProcessStartInfo info = new ProcessStartInfo(updaterPath, "\"" + SafeUpdater.ApplyArgument + "\" \"" + requestPath.Replace("\"", "\\\"") + "\"");
            info.UseShellExecute = true;
            Process.Start(info);
            pendingUpdateRequestPath = null;
            Log("INFO Separate updater process started. Closing the main application before replacement.");
            BeginInvoke((MethodInvoker)delegate { Close(); });
        }

        private static void ValidateReleaseAssets(ReleaseInfo release)
        {
            string version = NormalizeVersion(release.TagName);
            string expected = "CK3MPS-" + version + ".zip";
            if (!String.Equals(release.AssetName, expected, StringComparison.Ordinal))
                throw new InvalidDataException("The release does not contain the exact update package asset: " + expected);
            if (String.IsNullOrEmpty(release.ChecksumUrl) || String.IsNullOrEmpty(release.ManifestUrl))
                throw new InvalidDataException("The release is missing its checksum or package manifest.");
            ValidateReleaseDownloadUrl(release.DownloadUrl);
            ValidateReleaseDownloadUrl(release.ChecksumUrl);
            ValidateReleaseDownloadUrl(release.ManifestUrl);
        }

        private static void PickReleaseAssets(GitHubReleaseDto releaseDto, ReleaseInfo release)
        {
            string version = NormalizeVersion(release.TagName);
            string packageName = "CK3MPS-" + version + ".zip";
            string checksumName = packageName + ".sha256";
            string manifestName = packageName + ".manifest.json";
            foreach (GitHubAssetDto asset in releaseDto.Assets ?? new GitHubAssetDto[0])
            {
                string name = asset != null ? asset.Name ?? "" : "";
                string url = asset != null ? asset.BrowserDownloadUrl ?? "" : "";
                if (String.Equals(name, packageName, StringComparison.Ordinal))
                {
                    release.AssetName = name;
                    release.DownloadUrl = url;
                }
                else if (String.Equals(name, checksumName, StringComparison.Ordinal))
                    release.ChecksumUrl = url;
                else if (String.Equals(name, manifestName, StringComparison.Ordinal))
                    release.ManifestUrl = url;
            }
        }

        private static GitHubReleaseDto[] ParseReleaseJson(string json)
        {
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubReleaseDto[]));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "[]")))
                    return serializer.ReadObject(stream) as GitHubReleaseDto[] ?? new GitHubReleaseDto[0];
            }
            catch
            {
                return new GitHubReleaseDto[0];
            }
        }

        private WebClient CreateGitHubWebClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "CK3MPS/" + AppVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static void ValidateReleaseDownloadUrl(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed)
                || !String.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !parsed.AbsolutePath.StartsWith(ReleaseDownloadPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Update asset URL is outside the allowlisted HTTPS release endpoint.");
        }

        private static string ParseChecksum(string text, string exactAssetName)
        {
            string[] parts = (text ?? "").Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Length != 64 || !String.Equals(parts[parts.Length - 1].TrimStart('*'), exactAssetName, StringComparison.Ordinal))
                throw new InvalidDataException("Release checksum metadata is malformed or names a different asset.");
            return parts[0];
        }

        private static void VerifyFileHash(string path, string expected)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded update checksum does not match release metadata.");
            }
        }

        private static void RejectStagingReparsePoint(string stagingRoot)
        {
            DirectoryInfo current = new DirectoryInfo(stagingRoot);
            while (current != null)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Updater staging cannot use a reparse point.");
                current = current.Parent;
            }
        }

        private static void CleanupOldUpdateStaging()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CK3MPS", "updates");
            if (!Directory.Exists(root))
                return;
            foreach (string directory in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddDays(-7))
                        Directory.Delete(directory, true);
                }
                catch { }
            }
        }

        private static string NormalizeVersion(string version)
        {
            return (version ?? "").Trim().TrimStart('v', 'V');
        }
    }
}
