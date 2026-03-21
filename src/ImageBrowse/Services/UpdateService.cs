using Velopack;
using Velopack.Sources;

namespace ImageBrowse.Services;

public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/fastbone/Image-Browse-Opus";

    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    public bool IsInstalled
    {
        get
        {
            try
            {
                EnsureManager();
                return _manager!.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    public async Task<string?> CheckForUpdatesAsync()
    {
        try
        {
            EnsureManager();
            if (!_manager!.IsInstalled) return null;

            _pendingUpdate = await _manager.CheckForUpdatesAsync();
            return _pendingUpdate?.TargetFullRelease?.Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DownloadAndApplyAsync(Action<int>? progressCallback = null)
    {
        try
        {
            if (_pendingUpdate is null || _manager is null) return false;

            await _manager.DownloadUpdatesAsync(_pendingUpdate, p => progressCallback?.Invoke(p));
            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureManager()
    {
        _manager ??= new UpdateManager(new GithubSource(RepoUrl, null, false));
    }
}
