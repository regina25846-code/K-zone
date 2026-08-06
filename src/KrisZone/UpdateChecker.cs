using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace KrisZone
{
    // K-Tube에서 electron-updater가 "테스트빌드(-N) 설치 상태면 정식 릴리즈를 영원히 못 찾는"
    // 문제를 겪었던 것과 같은 함정을 처음부터 피하기 위한 설계 — GitHub의 /releases/latest API는
    // prerelease로 표시된 릴리즈를 자동으로 건너뛰고 항상 "가장 최근 정식 릴리즈"만 돌려주므로,
    // electron-updater처럼 별도 채널 매칭 로직이 필요 없다(2026-08-06, project_ktube_version 참고).
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/regina25846-code/K-zone/releases/latest";
        private const string UserAgent = "K-Zone-Updater";

        public sealed record Result(bool HasUpdate, string? LatestVersion, string? DownloadUrl, string? Error);

        public static async Task<Result> CheckAsync(string currentVersion)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var json = await http.GetStringAsync(ApiUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var latestVersion = tag.TrimStart('v', 'V');

                string? downloadUrl = null;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith("K-Zone.Setup.", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (downloadUrl == null)
                    return new Result(false, latestVersion, null, "릴리즈에서 설치파일을 찾지 못함");

                bool isNewer = CompareCoreVersions(latestVersion, currentVersion) > 0;
                return new Result(isNewer, latestVersion, downloadUrl, null);
            }
            catch (Exception ex)
            {
                return new Result(false, null, null, ex.Message);
            }
        }

        public static async Task<string> DownloadInstallerAsync(string url, string version)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"K-Zone.Setup.{version}.exe");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(tempPath, bytes);
            return tempPath;
        }

        // "1.2.0-3" 같은 테스트빌드 버전이 설치돼있어도 하이픈 뒤는 버리고 앞부분(1.2.0)만 비교 —
        // 정식 릴리즈만 후보로 주는 GitHub API와 짝지어 항상 안정적으로 "진짜 최신 정식버전"과 비교됨.
        private static int CompareCoreVersions(string a, string b)
        {
            static Version Parse(string v) =>
                Version.TryParse(v.Split('-')[0], out var result) ? result : new Version(0, 0, 0);
            return Parse(a).CompareTo(Parse(b));
        }
    }
}
