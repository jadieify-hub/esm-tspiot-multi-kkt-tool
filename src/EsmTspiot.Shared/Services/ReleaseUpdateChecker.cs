using System;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EsmTspiot.Shared.Services
{
    public sealed class ReleaseUpdateChecker
    {
        public const string LatestReleaseApi = "https://api.github.com/repos/jadieify-hub/esm-tspiot-multi-kkt-tool/releases/latest";
        public const string ReleasesUrl = "https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/releases/latest";
        private readonly HttpClient _client;

        public ReleaseUpdateChecker(HttpClient client)
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;
        }

        public async Task<string> GetAvailableVersionAsync(string installedVersion, CancellationToken cancellation)
        {
            string[] installedParts = (installedVersion ?? string.Empty).Split(new[] { '-' }, 2);
            Version installed;
            if (!TryVersion(installedParts[0], out installed)) return null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi))
            {
                request.Headers.UserAgent.ParseAdd("MultiKKT/" + installed.ToString());
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await _client.SendAsync(request, cancellation).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode != HttpStatusCode.OK || response.Content == null) return null;
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    var release = JsonHelper.Deserialize<Release>(json);
                    Version latest;
                    if (release == null || release.Draft || release.Prerelease || !TryVersion(release.Tag, out latest)) return null;
                    return latest > installed || (latest == installed && installedParts.Length > 1)
                        ? release.Tag : null;
                }
            }
        }

        private static bool TryVersion(string value, out Version result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            Version parsed;
            if (!Version.TryParse(text, out parsed)) return false;
            result = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
            return true;
        }

        [DataContract]
        private sealed class Release
        {
            [DataMember(Name = "tag_name", IsRequired = true)] public string Tag { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        }
    }
}
