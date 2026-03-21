using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Services;

public sealed class ImagePrefetchService : IDisposable
{
    private readonly ImageLoadingService _loader;
    private readonly ConcurrentDictionary<int, BitmapSource> _cache = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _loading = new();
    private readonly SemaphoreSlim _semaphore = new(2, 2);

    private int _maxDimension;
    private Func<int, (string FilePath, bool IsFolder)>? _indexResolver;

    public int MaxPrefetch { get; set; } = 2;

    public ImagePrefetchService(ImageLoadingService loader)
    {
        _loader = loader;
    }

    public void Configure(int maxDimension, Func<int, (string FilePath, bool IsFolder)> indexResolver)
    {
        _maxDimension = maxDimension;
        _indexResolver = indexResolver;
    }

    public BitmapSource? GetCached(int index)
    {
        return _cache.TryGetValue(index, out var bmp) ? bmp : null;
    }

    public async Task<BitmapSource?> GetOrLoadAsync(int index)
    {
        if (_cache.TryGetValue(index, out var cached))
            return cached;

        var info = _indexResolver?.Invoke(index);
        if (info is null || info.Value.IsFolder)
            return null;

        var bmp = await Task.Run(() => _loader.LoadFullImage(info.Value.FilePath, _maxDimension));
        if (bmp is not null)
            _cache[index] = bmp;

        return bmp;
    }

    public void UpdatePosition(int currentIndex, int totalCount)
    {
        if (_indexResolver is null) return;

        var keepSet = new HashSet<int>();
        for (int offset = -MaxPrefetch; offset <= MaxPrefetch; offset++)
        {
            int idx = currentIndex + offset;
            if (idx >= 0 && idx < totalCount)
                keepSet.Add(idx);
        }

        foreach (var key in _cache.Keys)
        {
            if (!keepSet.Contains(key))
                _cache.TryRemove(key, out _);
        }

        foreach (var key in _loading.Keys)
        {
            if (!keepSet.Contains(key) && _loading.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        foreach (int idx in keepSet)
        {
            if (idx == currentIndex) continue;
            if (_cache.ContainsKey(idx)) continue;
            if (_loading.ContainsKey(idx)) continue;

            var info = _indexResolver(idx);
            if (info.IsFolder) continue;

            var cts = new CancellationTokenSource();
            if (!_loading.TryAdd(idx, cts))
            {
                cts.Dispose();
                continue;
            }

            var filePath = info.FilePath;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _semaphore.WaitAsync(cts.Token);
                    try
                    {
                        if (cts.Token.IsCancellationRequested) return;
                        var bmp = _loader.LoadFullImage(filePath, _maxDimension);
                        if (bmp is not null && !cts.Token.IsCancellationRequested)
                            _cache[idx] = bmp;
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    _loading.TryRemove(idx, out _);
                    cts.Dispose();
                }
            }, cts.Token);
        }
    }

    public void Invalidate(int index)
    {
        _cache.TryRemove(index, out _);
        if (_loading.TryRemove(index, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void Clear()
    {
        foreach (var kvp in _loading)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _loading.Clear();
        _cache.Clear();
    }

    public void Dispose()
    {
        Clear();
        _semaphore.Dispose();
    }
}
