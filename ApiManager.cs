using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BDSM
{
    public record ApiReleaseInfo(string Version, string DownloadUrl);

    public static class ApiManager
    {
        private static readonly HttpClient httpClient = new();

        public static async Task<ApiReleaseInfo?> GetLatestApiReleaseInfoAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ArkServerApi/AsaApi/releases/latest");
            request.Headers.Add("User-Agent", "Bole's Dedicated Server Manager");

            try
            {
                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var releaseData = await response.Content.ReadFromJsonAsync<GithubRelease>();
                if (releaseData?.Assets != null && releaseData.TagName != null)
                {
                    var zipAsset = releaseData.Assets.Find(a => a.Name?.EndsWith(".zip") ?? false);
                    if (zipAsset?.BrowserDownloadUrl != null)
                    {
                        return new ApiReleaseInfo(releaseData.TagName, zipAsset.BrowserDownloadUrl);
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Error logging removed
            }
            return null;
        }

        public static async Task DownloadAndInstallApiAsync(ApiReleaseInfo releaseInfo, string serverInstallDir)
        {
            string tempZipPath = Path.GetTempFileName();
            string installPath = Path.Combine(serverInstallDir, "ShooterGame", "Binaries", "Win64");

            try
            {
                Directory.CreateDirectory(installPath);

                var zipBytes = await httpClient.GetByteArrayAsync(releaseInfo.DownloadUrl);
                await File.WriteAllBytesAsync(tempZipPath, zipBytes);

                ZipFile.ExtractToDirectory(tempZipPath, installPath, true);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
        }

        public class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("assets")]
            public List<GithubAsset>? Assets { get; set; }
        }

        public class GithubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}