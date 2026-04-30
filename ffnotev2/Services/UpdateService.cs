using System.Windows;
using Velopack;
using Velopack.Sources;

namespace ffnotev2.Services;

/// <summary>
/// GitHub Releases에서 새 버전을 확인해 사용자에게 알리고, 동의 시 다운로드 후 즉시 재시작.
/// 개발 환경(설치되지 않은 빌드)에서는 자동으로 스킵되므로 dotnet run 등에 영향 없음.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/usbin/ffnotev2";

    private readonly UpdateManager _mgr;

    public UpdateService()
    {
        _mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    }

    public async Task CheckAndPromptAsync(Window owner)
    {
        // 설치되지 않은 환경(개발 빌드)에서는 동작하지 않음
        if (!_mgr.IsInstalled) return;

        try
        {
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(true);
            if (info is null) return; // 새 버전 없음

            var version = info.TargetFullRelease.Version;
            var msg = $"새 버전 {version}이(가) 있습니다.\n지금 업데이트하시겠습니까?\n\n" +
                      $"확인 시 자동으로 다운로드하고 앱이 재시작됩니다.";
            var result = MessageBox.Show(owner, msg, "ffnote v2 업데이트",
                MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result != MessageBoxResult.OK) return;

            await _mgr.DownloadUpdatesAsync(info).ConfigureAwait(true);
            _mgr.ApplyUpdatesAndRestart(info); // 앱 즉시 종료 + 새 버전 재시작
        }
        catch
        {
            // 네트워크/GitHub 일시적 실패는 사용자에게 알리지 않고 조용히 무시
        }
    }
}
