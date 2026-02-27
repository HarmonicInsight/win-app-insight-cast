using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace InsightCast.Services;

/// <summary>
/// Checks for new versions by querying GitHub Releases API.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/HarmonicInsight/win-app-insight-cast/releases/latest";

    /// <summary>
    /// Checks if a newer version is available.
    /// Returns the new version tag and download URL, or null if up-to-date or check fails.
    /// </summary>
    public static async Task<(string Tag, string Url)?> CheckForUpdateAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("InsightCast-UpdateCheck/1.0");

            var json = await http.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString();
            var htmlUrl = root.GetProperty("html_url").GetString();

            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(htmlUrl))
                return null;

            // Parse version from tag (e.g., "v1.1.0" → 1.1.0)
            var tagVersion = tag.TrimStart('v', 'V');
            if (!Version.TryParse(tagVersion, out var remoteVersion))
                return null;

            var localVersion = typeof(UpdateChecker).Assembly.GetName().Version;
            if (localVersion == null)
                return null;

            // Compare major.minor.build (ignore revision)
            var local3 = new Version(localVersion.Major, localVersion.Minor, localVersion.Build);
            var remote3 = new Version(remoteVersion.Major, remoteVersion.Minor,
                Math.Max(0, remoteVersion.Build));

            if (remote3 > local3)
                return (tag, htmlUrl);

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UpdateChecker failed: {ex.Message}");
            return null;
        }
    }
}
