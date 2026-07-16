using System;
using System.Diagnostics;
using System.IO;
using System.Net;
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

                Log("WARN New CK3MPS release available: " + release.TagName + " (current " + AppVersion + "). Automatic install is disabled until CK3MPS has a signed release channel.");
                DialogResult result = MessageBox.Show(
                    "A new CK3MPS release is available.\r\n\r\nCurrent: " + AppVersion + "\r\nLatest: " + release.TagName + "\r\n\r\nAutomatic install is disabled until CK3MPS ships a signed release channel.\r\n\r\nOpen the official releases page now?",
                    "CK3MPS update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result != DialogResult.Yes)
                {
                    Log("INFO Update skipped by user.");
                    return;
                }

                OpenOfficialReleasePage(release.ReleasePageUrl);
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
