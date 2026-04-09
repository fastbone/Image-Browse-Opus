using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using ImageBrowse.Helpers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;
using LibVLCSharp.Shared;

namespace ImageBrowse.Views;

public partial class FullscreenViewer : Window
{
    public DateTime LastEscapePress { get; private set; } = DateTime.MinValue;

    private readonly MainViewModel _vm = null!;
    private readonly ImagePrefetchService _prefetch;
    private readonly AvaloniaImageLoadingService _imageLoader = new();
    private double _zoomLevel = 1.0;
    private bool _infoVisible;
    private bool _isFitToScreen = true;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _positionFadeTimer;
    private readonly DispatcherTimer _zoomFadeTimer;
    private Point _lastMousePos;
    private CancellationTokenSource _loadCts = new();
    private int _loadSequence;

    private ScaleTransform _imageScale = null!;
    private ScaleTransform _backImageScale = null!;

    private bool _isDragging;
    private Point _dragStart;
    private Vector _dragOffset;

    private bool _filmstripPinned;
    private bool _filmstripVisible;
    private readonly DispatcherTimer _filmstripFadeTimer;
    private bool _suppressFilmstripNav;

    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentVideoMedia;
    private bool _isVideoActive;
    private readonly DispatcherTimer _videoPositionTimer;
    private readonly DispatcherTimer _videoControlFadeTimer;
    private bool _isSeeking;
    private bool _suppressVolumeEvent;
    private static readonly float[] PlaybackSpeeds = [0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f];
    private int _speedIndex = 2;

    private double _videoZoomLevel = 1.0;
    private double _videoCropX;
    private double _videoCropY;
    private uint _videoPixelW;
    private uint _videoPixelH;
    private bool _videoZoomActive;
    private bool _videoMiniMapDragging;

    private const double ZoomStep = 0.15;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20.0;
    private const int CrossfadeDurationMs = 150;

    public FullscreenViewer() : this(null!) { }

    public FullscreenViewer(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _prefetch = new ImagePrefetchService(_imageLoader);

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cursorTimer.Tick += (_, _) =>
        {
            Cursor = new Cursor(StandardCursorType.None);
            _cursorTimer.Stop();
        };

        _positionFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _positionFadeTimer.Tick += (_, _) =>
        {
            PositionIndicator.Opacity = 0;
            _positionFadeTimer.Stop();
        };

        _zoomFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
        _zoomFadeTimer.Tick += (_, _) =>
        {
            ZoomIndicator.Opacity = 0;
            _zoomFadeTimer.Stop();
        };

        _filmstripFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _filmstripFadeTimer.Tick += (_, _) =>
        {
            if (!_filmstripPinned) HideFilmstrip();
            _filmstripFadeTimer.Stop();
        };

        _videoPositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoPositionTimer.Tick += (_, _) => UpdateVideoPosition();

        _videoControlFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _videoControlFadeTimer.Tick += (_, _) =>
        {
            VideoControlBar.Opacity = 0;
            _videoControlFadeTimer.Stop();
        };

        if (_vm is null) return;

        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_vm is null) return;

        _imageScale = (DisplayImage.RenderTransform as ScaleTransform) ?? new ScaleTransform(1, 1);
        DisplayImage.RenderTransform = _imageScale;
        _backImageScale = (BackImage.RenderTransform as ScaleTransform) ?? new ScaleTransform(1, 1);
        BackImage.RenderTransform = _backImageScale;

        int screenMax = (int)Math.Max(Bounds.Width > 0 ? Bounds.Width : 1920,
                                       Bounds.Height > 0 ? Bounds.Height : 1080);
        _prefetch.Configure(screenMax * 2, idx =>
        {
            if (idx < 0 || idx >= _vm.SortedImages.Count)
                return ("", true);
            var i = _vm.SortedImages[idx];
            return (i.FilePath, i.IsFolder || i.IsVideo);
        });

        PopulateFilmstrip();
        LoadCurrentImage(crossfade: false);
        _cursorTimer.Start();
    }

    #region Image Loading

    private async void LoadCurrentImage(bool crossfade = true)
    {
        var item = _vm.SelectedItem;
        if (item is null || item.IsFolder) return;

        StopVideoPlayback();

        if (item.IsVideo)
        {
            BackImage.Source = null;
            BackImage.Opacity = 0;
            ShowVideoView(item);
            UpdateInfo(item);
            ShowPosition();
            SyncFilmstripSelection();
            return;
        }

        ShowImageView();

        _loadCts.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        int seq = Interlocked.Increment(ref _loadSequence);

        Bitmap? newImage = null;

        var cached = _prefetch.GetCached(_vm.SelectedIndex);
        if (cached is Bitmap cachedBmp)
        {
            newImage = cachedBmp;
        }
        else
        {
            var image = await _prefetch.GetOrLoadAsync(_vm.SelectedIndex);
            if (ct.IsCancellationRequested || seq != _loadSequence) return;
            newImage = image as Bitmap;
        }

        if (newImage is null)
        {
            DisplayImage.Source = null;
            return;
        }

        bool animate = crossfade && _vm.Settings.EnableAnimations;

        if (animate && DisplayImage.Source is not null)
        {
            BackImage.Source = DisplayImage.Source;
            _backImageScale.ScaleX = _imageScale.ScaleX;
            _backImageScale.ScaleY = _imageScale.ScaleY;
            BackImage.MaxWidth = DisplayImage.MaxWidth;
            BackImage.MaxHeight = DisplayImage.MaxHeight;
            BackImage.Opacity = 1;

            DisplayImage.Source = newImage;
            DisplayImage.Opacity = 0;

            await CrossfadeSwapAsync();

            BackImage.Source = null;
            BackImage.Opacity = 0;
            DisplayImage.Opacity = 1;
        }
        else
        {
            BackImage.Source = null;
            BackImage.Opacity = 0;
            DisplayImage.Opacity = 1;
            DisplayImage.Source = newImage;
        }

        _prefetch.UpdatePosition(_vm.SelectedIndex, _vm.SortedImages.Count);

        ResetZoom();
        UpdateInfo(item);
        ShowPosition();
        SyncFilmstripSelection();
    }

    private async Task CrossfadeSwapAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < CrossfadeDurationMs)
        {
            double t = sw.ElapsedMilliseconds / (double)CrossfadeDurationMs;
            DisplayImage.Opacity = t;
            BackImage.Opacity = 1 - t;
            await Task.Delay(16);
        }

        DisplayImage.Opacity = 1;
        BackImage.Opacity = 0;
    }

    private void EnsureVlcInitialized()
    {
        if (_libVLC is not null) return;
        var opts = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            opts.Add("--no-xlib");
        _libVLC = new LibVLC(opts.ToArray());
        _mediaPlayer = new MediaPlayer(_libVLC);
        _mediaPlayer.EndReached += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _videoPositionTimer.Stop();
            PlayPauseButton.Content = "Play";
        });
        _mediaPlayer.Playing += (_, _) => Dispatcher.UIThread.Post(ApplyVideoSizing);
    }

    private void ShowVideoView(ImageItem item)
    {
        _isVideoActive = true;
        ImageScroller.IsVisible = false;
        VideoView.IsVisible = true;
        NavLeft.IsVisible = false;
        NavRight.IsVisible = false;
        FilmstripPanel.IsHitTestVisible = false;

        try
        {
            EnsureVlcInitialized();
            VideoView.MediaPlayer = _mediaPlayer;

            _currentVideoMedia?.Dispose();
            _currentVideoMedia = new Media(_libVLC!, item.FilePath, FromType.FromPath);
            _mediaPlayer!.Play(_currentVideoMedia);
            _videoPositionTimer.Start();
            PlayPauseButton.Content = "Pause";

            _suppressVolumeEvent = true;
            VolumeSlider.Value = _mediaPlayer.Volume;
            _suppressVolumeEvent = false;

            _speedIndex = 2;
            _mediaPlayer.SetRate(1.0f);
            SpeedText.Text = "1.0x";
        }
        catch (Exception ex)
        {
            _videoPositionTimer.Stop();
            VideoTimeText.Text = "Error";
            System.Diagnostics.Debug.WriteLine($"Video playback error: {ex.Message}");
        }
    }

    private void ShowImageView()
    {
        if (!_isVideoActive) return;
        ResetVideoZoom();
        _isVideoActive = false;
        _videoPositionTimer.Stop();
        VideoView.IsVisible = false;
        ImageScroller.IsVisible = true;
        NavLeft.IsVisible = true;
        NavRight.IsVisible = true;
        FilmstripPanel.IsHitTestVisible = true;
        VideoControlBar.Opacity = 0;
        VideoView.MediaPlayer = null;
    }

    private void StopVideoPlayback()
    {
        _videoPositionTimer.Stop();
        try
        {
            _mediaPlayer?.Stop();
        }
        catch { }

        _currentVideoMedia?.Dispose();
        _currentVideoMedia = null;

        if (_isVideoActive)
            ShowImageView();
    }

    private void ApplyVideoSizing()
    {
        if (_mediaPlayer is null) return;
        uint pw = 0, ph = 0;
        if (!_mediaPlayer.Size(0, ref pw, ref ph) || pw == 0 || ph == 0) return;
        _videoPixelW = pw;
        _videoPixelH = ph;
        if (_videoZoomActive)
            Dispatcher.UIThread.Post(() => UpdateVideoMiniMap(), DispatcherPriority.Background);
    }

    private void ToggleVideoZoom()
    {
        if (_mediaPlayer is null) return;

        if (_videoZoomActive)
        {
            ResetVideoZoom();
            return;
        }

        uint pw = 0, ph = 0;
        if (!_mediaPlayer.Size(0, ref pw, ref ph) || pw == 0 || ph == 0)
            return;
        _videoPixelW = pw;
        _videoPixelH = ph;

        _videoZoomActive = true;
        _videoZoomLevel = 2.0;
        _videoCropX = _videoPixelW / 4.0;
        _videoCropY = _videoPixelH / 4.0;
        ApplyVideoZoom(_videoZoomLevel);

        if (_vm.SelectedItem?.Thumbnail is Bitmap bmp)
            VideoMiniMapImage.Source = bmp;
        else
            VideoMiniMapImage.Source = null;

        VideoMiniMapPanel.IsVisible = true;
        Dispatcher.UIThread.Post(() => UpdateVideoMiniMap(), DispatcherPriority.Loaded);
    }

    private void ApplyVideoZoom(double newZoom)
    {
        if (_mediaPlayer is null || _videoPixelW == 0) return;

        _videoZoomLevel = Math.Clamp(newZoom, 1.0, 8.0);

        if (_videoZoomLevel <= 1.01)
        {
            ResetVideoZoom();
            return;
        }

        double cropW = _videoPixelW / _videoZoomLevel;
        double cropH = _videoPixelH / _videoZoomLevel;

        _videoCropX = Math.Clamp(_videoCropX, 0, _videoPixelW - cropW);
        _videoCropY = Math.Clamp(_videoCropY, 0, _videoPixelH - cropH);

        int cw = (int)Math.Round(cropW);
        int ch = (int)Math.Round(cropH);
        int cx = (int)Math.Round(_videoCropX);
        int cy = (int)Math.Round(_videoCropY);

        _mediaPlayer.CropGeometry = $"{cw}x{ch}+{cx}+{cy}";
        Dispatcher.UIThread.Post(() => UpdateVideoMiniMap(), DispatcherPriority.Background);
    }

    private void ResetVideoZoom()
    {
        _videoZoomActive = false;
        _videoZoomLevel = 1.0;
        _videoCropX = 0;
        _videoCropY = 0;
        if (_mediaPlayer is not null)
            _mediaPlayer.CropGeometry = "";
        VideoMiniMapPanel.IsVisible = false;
    }

    private void UpdateVideoMiniMap()
    {
        if (_videoPixelW == 0 || _videoPixelH == 0) return;

        double canvasW = VideoMiniMapCanvas.Bounds.Width;
        double canvasH = VideoMiniMapCanvas.Bounds.Height;
        if (canvasW <= 0 || canvasH <= 0) return;

        double scale = Math.Min(canvasW / _videoPixelW, canvasH / _videoPixelH);
        double mapW = _videoPixelW * scale;
        double mapH = _videoPixelH * scale;
        double mapX = (canvasW - mapW) / 2;
        double mapY = (canvasH - mapH) / 2;

        Canvas.SetLeft(VideoMiniMapImage, mapX);
        Canvas.SetTop(VideoMiniMapImage, mapY);
        VideoMiniMapImage.Width = mapW;
        VideoMiniMapImage.Height = mapH;

        double cropW = _videoPixelW / _videoZoomLevel;
        double cropH = _videoPixelH / _videoZoomLevel;

        double vpX = _videoCropX / _videoPixelW * mapW + mapX;
        double vpY = _videoCropY / _videoPixelH * mapH + mapY;
        double vpW = cropW / _videoPixelW * mapW;
        double vpH = cropH / _videoPixelH * mapH;

        Canvas.SetLeft(VideoMiniMapViewport, vpX);
        Canvas.SetTop(VideoMiniMapViewport, vpY);
        VideoMiniMapViewport.Width = Math.Max(vpW, 4);
        VideoMiniMapViewport.Height = Math.Max(vpH, 4);
    }

    private void VideoMiniMapNavigateTo(Point canvasPos)
    {
        if (_videoPixelW == 0 || _videoPixelH == 0) return;

        double canvasW = VideoMiniMapCanvas.Bounds.Width;
        double canvasH = VideoMiniMapCanvas.Bounds.Height;
        double scale = Math.Min(canvasW / _videoPixelW, canvasH / _videoPixelH);
        double mapW = _videoPixelW * scale;
        double mapH = _videoPixelH * scale;
        double mapX = (canvasW - mapW) / 2;
        double mapY = (canvasH - mapH) / 2;

        double relX = (canvasPos.X - mapX) / mapW;
        double relY = (canvasPos.Y - mapY) / mapH;
        relX = Math.Clamp(relX, 0, 1);
        relY = Math.Clamp(relY, 0, 1);

        double cropW = _videoPixelW / _videoZoomLevel;
        double cropH = _videoPixelH / _videoZoomLevel;
        _videoCropX = relX * _videoPixelW - cropW / 2;
        _videoCropY = relY * _videoPixelH - cropH / 2;

        ApplyVideoZoom(_videoZoomLevel);
    }

    private void VideoMiniMap_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        _videoMiniMapDragging = true;
        e.Pointer.Capture(border);
        VideoMiniMapNavigateTo(e.GetPosition(VideoMiniMapCanvas));
        e.Handled = true;
    }

    private void VideoMiniMap_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_videoMiniMapDragging) return;
        VideoMiniMapNavigateTo(e.GetPosition(VideoMiniMapCanvas));
        e.Handled = true;
    }

    private void VideoMiniMap_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_videoMiniMapDragging) return;
        _videoMiniMapDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateVideoPosition()
    {
        if (_mediaPlayer is null || !_isVideoActive) return;
        long len = _mediaPlayer.Length;
        long time = _mediaPlayer.Time;
        if (len > 0)
        {
            VideoDurationText.Text = FormatVideoTime(len);
            if (!_isSeeking)
                VideoSeekBar.Value = len > 0 ? (double)time / len * 1000.0 : 0;
        }
        VideoTimeText.Text = FormatVideoTime(time);
    }

    private static string FormatVideoTime(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private void ShowVideoControlBar()
    {
        VideoControlBar.Opacity = 1;
        _videoControlFadeTimer.Stop();
        _videoControlFadeTimer.Start();
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            PlayPauseButton.Content = "Play";
        }
        else
        {
            _mediaPlayer.Play();
            PlayPauseButton.Content = "Pause";
        }
    }

    private void VideoSeekBar_PointerPressed(object? sender, PointerPressedEventArgs e) => _isSeeking = true;

    private void VideoSeekBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        _isSeeking = false;
        long length = _mediaPlayer.Length;
        if (length > 0)
            _mediaPlayer.Time = (long)(VideoSeekBar.Value / 1000.0 * length);
    }

    private void VideoSeekBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isSeeking || _mediaPlayer is null) return;
        long length = _mediaPlayer.Length;
        if (length > 0)
            VideoTimeText.Text = FormatVideoTime((long)(VideoSeekBar.Value / 1000.0 * length));
    }

    private void VolumeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolumeEvent || _mediaPlayer is null) return;
        _mediaPlayer.Volume = (int)VolumeSlider.Value;
    }

    private void CyclePlaybackSpeed(int direction)
    {
        if (_mediaPlayer is null) return;
        _speedIndex = Math.Clamp(_speedIndex + direction, 0, PlaybackSpeeds.Length - 1);
        _mediaPlayer.SetRate(PlaybackSpeeds[_speedIndex]);
        SpeedText.Text = $"{PlaybackSpeeds[_speedIndex]:0.##}x";
    }

    #endregion

    #region Zoom/Pan

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        _isFitToScreen = true;
        _imageScale.ScaleX = 1;
        _imageScale.ScaleY = 1;
        _backImageScale.ScaleX = 1;
        _backImageScale.ScaleY = 1;
        double w = Bounds.Width > 0 ? Bounds.Width : 1920;
        double h = Bounds.Height > 0 ? Bounds.Height : 1080;
        DisplayImage.MaxWidth = w;
        DisplayImage.MaxHeight = h;
        BackImage.MaxWidth = w;
        BackImage.MaxHeight = h;
    }

    private void ApplyZoom(double newZoom)
    {
        _zoomLevel = Math.Clamp(newZoom, MinZoom, MaxZoom);
        _isFitToScreen = false;
        DisplayImage.MaxWidth = double.PositiveInfinity;
        DisplayImage.MaxHeight = double.PositiveInfinity;
        _imageScale.ScaleX = _zoomLevel;
        _imageScale.ScaleY = _zoomLevel;
        ShowZoom();
    }

    private void FitToScreen()
    {
        ResetZoom();
        ShowZoom();
    }

    private void Window_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_isVideoActive && _mediaPlayer is not null)
        {
            if (_videoZoomActive)
            {
                double zoomFactor = e.Delta.Y > 0 ? (1 + ZoomStep) : (1 / (1 + ZoomStep));
                ApplyVideoZoom(_videoZoomLevel * zoomFactor);
                e.Handled = true;
                return;
            }

            int delta = e.Delta.Y > 0 ? 5 : -5;
            _mediaPlayer.Volume = Math.Clamp(_mediaPlayer.Volume + delta, 0, 100);
            _suppressVolumeEvent = true;
            VolumeSlider.Value = _mediaPlayer.Volume;
            _suppressVolumeEvent = false;
            ShowVideoControlBar();
            e.Handled = true;
            return;
        }

        double factor = e.Delta.Y > 0 ? (1 + ZoomStep) : (1 / (1 + ZoomStep));
        ApplyZoom(_zoomLevel * factor);
        e.Handled = true;
    }

    private void ImageScroller_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isFitToScreen && e.GetCurrentPoint(ImageScroller).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ImageScroller);
            _dragOffset = new Vector(ImageScroller.Offset.X, ImageScroller.Offset.Y);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    private void ImageScroller_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }

    private void ImageScroller_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetPosition(ImageScroller);
            double dx = _dragStart.X - pos.X;
            double dy = _dragStart.Y - pos.Y;
            ImageScroller.Offset = new Vector(_dragOffset.X + dx, _dragOffset.Y + dy);
            e.Handled = true;
        }
    }

    #endregion

    #region Navigation

    private void NavigateNext()
    {
        var item = _vm.GetNextImage();
        if (item is not null) LoadCurrentImage();
    }

    private void NavigatePrevious()
    {
        var item = _vm.GetPreviousImage();
        if (item is not null) LoadCurrentImage();
    }

    private void NavigateFirst()
    {
        var item = _vm.GetFirstImage();
        if (item is not null) LoadCurrentImage();
    }

    private void NavigateLast()
    {
        var item = _vm.GetLastImage();
        if (item is not null) LoadCurrentImage();
    }

    private void NavLeft_PointerEntered(object? sender, PointerEventArgs e)
    {
        NavLeft.Opacity = 1;
        Cursor = Cursor.Default;
    }

    private void NavRight_PointerEntered(object? sender, PointerEventArgs e)
    {
        NavRight.Opacity = 1;
        Cursor = Cursor.Default;
    }

    private void NavZone_PointerExited(object? sender, PointerEventArgs e)
    {
        NavLeft.Opacity = 0;
        NavRight.Opacity = 0;
    }

    private void NavLeft_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NavigatePrevious();
        e.Handled = true;
    }

    private void NavRight_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NavigateNext();
        e.Handled = true;
    }

    #endregion

    #region Info / Indicators

    private void ShowPosition()
    {
        if (_vm.SortedImages.Count == 0) return;

        int imageIndex = 0;
        int totalImages = 0;
        for (int i = 0; i < _vm.SortedImages.Count; i++)
        {
            if (_vm.SortedImages[i].IsFolder) continue;
            totalImages++;
            if (i <= _vm.SelectedIndex)
                imageIndex = totalImages;
        }

        PositionText.Text = $"{imageIndex} / {totalImages}";
        PositionIndicator.Opacity = 1;
        _positionFadeTimer.Stop();
        _positionFadeTimer.Start();
    }

    private void ShowZoom()
    {
        string text = _isFitToScreen ? "Fit" : $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = text;
        ZoomIndicator.Opacity = 1;
        _zoomFadeTimer.Stop();
        _zoomFadeTimer.Start();
    }

    private void UpdateInfo(ImageItem item)
    {
        InfoFileName.Text = item.FileName;

        if (item.ImageWidth > 0)
        {
            InfoDimensions.Text = item.DimensionsDisplay;
            InfoDimensionsGrid.IsVisible = true;
        }
        else
        {
            InfoDimensionsGrid.IsVisible = false;
        }

        InfoFileSize.Text = item.FileSizeDisplay;

        if (item.DateTaken.HasValue)
        {
            InfoDateTaken.Text = item.DateTaken.Value.ToString("yyyy-MM-dd HH:mm");
            InfoDateTakenGrid.IsVisible = true;
        }
        else
        {
            InfoDateTakenGrid.IsVisible = false;
        }

        InfoDateModified.Text = item.DateModified.ToString("yyyy-MM-dd HH:mm");

        bool hasCamera = !string.IsNullOrEmpty(item.CameraModel);
        bool hasLens = !string.IsNullOrEmpty(item.LensModel);

        if (hasCamera)
        {
            InfoCamera.Text = $"{item.CameraManufacturer} {item.CameraModel}".Trim();
            InfoCameraGrid.IsVisible = true;
        }
        else
        {
            InfoCameraGrid.IsVisible = false;
        }

        if (hasLens)
        {
            InfoLens.Text = item.LensModel;
            InfoLensGrid.IsVisible = true;
        }
        else
        {
            InfoLensGrid.IsVisible = false;
        }

        var expParts = new List<string>();
        if (!string.IsNullOrEmpty(item.FNumber)) expParts.Add(item.FNumber);
        if (!string.IsNullOrEmpty(item.ExposureTime)) expParts.Add(item.ExposureTime);
        if (item.Iso.HasValue) expParts.Add($"ISO {item.Iso}");
        if (!string.IsNullOrEmpty(item.FocalLength)) expParts.Add(item.FocalLength);

        if (expParts.Count > 0)
        {
            InfoExposure.Text = string.Join("  |  ", expParts);
            InfoExposureGrid.IsVisible = true;
        }
        else
        {
            InfoExposureGrid.IsVisible = false;
        }

        CameraSeparator.IsVisible = hasCamera || hasLens || expParts.Count > 0;

        InfoRatingText.Text = item.Rating > 0
            ? new string('\u2605', item.Rating) + new string('\u2606', 5 - item.Rating)
            : "";
    }

    private void ToggleInfo()
    {
        _infoVisible = !_infoVisible;
        InfoPanel.Opacity = _infoVisible ? 1 : 0;
        InfoPanel.IsHitTestVisible = _infoVisible;
    }

    #endregion

    #region Filmstrip

    private void PopulateFilmstrip()
    {
        var imageItems = _vm.SortedImages.Where(i => !i.IsFolder).ToList();
        _suppressFilmstripNav = true;
        FilmstripList.ItemsSource = imageItems;
        _suppressFilmstripNav = false;
    }

    private void SyncFilmstripSelection()
    {
        if (_vm.SelectedItem is null) return;
        _suppressFilmstripNav = true;
        try
        {
            FilmstripList.SelectedItem = _vm.SelectedItem;
            FilmstripList.ScrollIntoView(_vm.SelectedItem);
        }
        finally
        {
            _suppressFilmstripNav = false;
        }
    }

    private void ShowFilmstrip()
    {
        if (_filmstripVisible) return;
        _filmstripVisible = true;
        FilmstripPanel.Opacity = 1;
        PositionIndicator.Margin = new Thickness(0, 0, 0, 100);
        _filmstripFadeTimer.Stop();
        _filmstripFadeTimer.Start();
        SyncFilmstripSelection();
    }

    private void HideFilmstrip()
    {
        if (!_filmstripVisible) return;
        _filmstripVisible = false;
        FilmstripPanel.Opacity = 0;
        PositionIndicator.Margin = new Thickness(0, 0, 0, 30);
    }

    private void ToggleFilmstripPin()
    {
        _filmstripPinned = !_filmstripPinned;
        if (_filmstripPinned)
        {
            _filmstripFadeTimer.Stop();
            ShowFilmstrip();
        }
        else
        {
            _filmstripFadeTimer.Start();
        }
    }

    private void FilmstripList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilmstripNav) return;
        if (FilmstripList.SelectedItem is not ImageItem item) return;

        int idx = _vm.SortedImages.IndexOf(item);
        if (idx < 0) return;

        _vm.SelectedIndex = idx;
        _vm.SelectedItem = item;
        LoadCurrentImage();
    }

    private void Filmstrip_PointerEntered(object? sender, PointerEventArgs e)
    {
        _filmstripFadeTimer.Stop();
    }

    private void Filmstrip_PointerExited(object? sender, PointerEventArgs e)
    {
        if (!_filmstripPinned)
            _filmstripFadeTimer.Start();
    }

    #endregion

    #region Keyboard

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_isVideoActive)
        {
            HandleVideoKeyDown(e);
            if (e.Handled) return;
        }

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                LastEscapePress = DateTime.UtcNow;
                Close();
                break;
            case Key.Enter:
                Close();
                break;

            case Key.Right:
            case Key.PageDown:
                NavigateNext();
                e.Handled = true;
                break;
            case Key.Space:
                if (!_isVideoActive)
                {
                    NavigateNext();
                    e.Handled = true;
                }
                break;

            case Key.Left:
            case Key.Back:
            case Key.PageUp:
                NavigatePrevious();
                e.Handled = true;
                break;

            case Key.Home:
                NavigateFirst();
                e.Handled = true;
                break;
            case Key.End:
                NavigateLast();
                e.Handled = true;
                break;

            case Key.Add:
            case Key.OemPlus:
                ApplyZoom(_zoomLevel * (1 + ZoomStep));
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ApplyZoom(_zoomLevel / (1 + ZoomStep));
                e.Handled = true;
                break;

            case Key.D0:
            case Key.NumPad0:
                FitToScreen();
                e.Handled = true;
                break;

            case Key.I:
                ToggleInfo();
                e.Handled = true;
                break;

            case Key.T:
                ToggleFilmstripPin();
                e.Handled = true;
                break;

            case Key.R:
                if (!ctrl) RotateCurrentImage();
                e.Handled = true;
                break;

            case Key.Q:
                if (_vm.SelectedItem is not null)
                {
                    _vm.ToggleTagCommand.Execute(null);
                    UpdateInfo(_vm.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Delete when !_isVideoActive:
                _ = DeleteCurrentImageAsync();
                e.Handled = true;
                break;

            case Key.D1 when !ctrl:
            case Key.NumPad1: SetRating(1); e.Handled = true; break;
            case Key.D2 when !ctrl:
            case Key.NumPad2: SetRating(2); e.Handled = true; break;
            case Key.D3 when !ctrl:
            case Key.NumPad3: SetRating(3); e.Handled = true; break;
            case Key.D4 when !ctrl:
            case Key.NumPad4: SetRating(4); e.Handled = true; break;
            case Key.D5 when !ctrl:
            case Key.NumPad5: SetRating(5); e.Handled = true; break;
        }
    }

    private async Task DeleteCurrentImageAsync()
    {
        if (!_vm.Settings.FileOperationsEnabled) return;
        var item = _vm.SelectedItem;
        if (item is null || item.IsFolder) return;

        if (_vm.Settings.ConfirmBeforeDelete)
        {
            var ok = await DialogUtil.ShowYesNoAsync(this,
                $"Move \"{item.FileName}\" to the Recycle Bin?\n\nYou can disable this confirmation in Settings.",
                "Confirm Delete");
            if (!ok) return;
        }

        if (item.IsVideo)
            StopVideoPlayback();

        if (!FileOperationService.MoveToRecycleBin(item.FilePath))
            return;

        _prefetch.Invalidate(_vm.SelectedIndex);
        _vm.RemoveImage(item);

        if (_vm.SortedImages.Count == 0 || _vm.SortedImages.All(i => i.IsFolder))
        {
            Close();
            return;
        }

        var next = _vm.GetNextImage() ?? _vm.GetPreviousImage();
        if (next is not null)
            LoadCurrentImage();
        else
            Close();
    }

    private void SetRating(int rating)
    {
        if (_vm.SelectedItem is null) return;
        _vm.SetRatingCommand.Execute(rating.ToString());
        UpdateInfo(_vm.SelectedItem);
    }

    private void HandleVideoKeyDown(KeyEventArgs e)
    {
        if (_mediaPlayer is null) return;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        switch (e.Key)
        {
            case Key.Space:
                PlayPause_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Left:
            {
                long delta = shift ? 30000 : 5000;
                _mediaPlayer.Time = Math.Max(_mediaPlayer.Time - delta, 0);
                ShowVideoControlBar();
                e.Handled = true;
                break;
            }
            case Key.Right:
            {
                long delta = shift ? 30000 : 5000;
                long length = _mediaPlayer.Length;
                if (length > 0)
                    _mediaPlayer.Time = Math.Min(_mediaPlayer.Time + delta, length);
                ShowVideoControlBar();
                e.Handled = true;
                break;
            }
            case Key.Up:
                _mediaPlayer.Volume = Math.Min(_mediaPlayer.Volume + 5, 100);
                _suppressVolumeEvent = true;
                VolumeSlider.Value = _mediaPlayer.Volume;
                _suppressVolumeEvent = false;
                e.Handled = true;
                break;
            case Key.Down:
                _mediaPlayer.Volume = Math.Max(_mediaPlayer.Volume - 5, 0);
                _suppressVolumeEvent = true;
                VolumeSlider.Value = _mediaPlayer.Volume;
                _suppressVolumeEvent = false;
                e.Handled = true;
                break;
            case Key.M:
                _mediaPlayer.Mute = !_mediaPlayer.Mute;
                e.Handled = true;
                break;
            case Key.Z:
                ToggleVideoZoom();
                e.Handled = true;
                break;
            case Key.N:
                NavigateNext();
                e.Handled = true;
                break;
            case Key.P:
                NavigatePrevious();
                e.Handled = true;
                break;
            case Key.OemOpenBrackets:
                CyclePlaybackSpeed(-1);
                ShowVideoControlBar();
                e.Handled = true;
                break;
            case Key.OemCloseBrackets:
                CyclePlaybackSpeed(1);
                ShowVideoControlBar();
                e.Handled = true;
                break;
        }
    }

    private async void RotateCurrentImage()
    {
        var item = _vm.SelectedItem;
        if (item is null || item.IsFolder || item.IsVideo) return;

        try
        {
            var (newW, newH) = await Task.Run(() => ImageRotationService.RotateClockwise90(item.FilePath));
            var fi = new FileInfo(item.FilePath);
            item.ImageWidth = newW;
            item.ImageHeight = newH;
            item.DateModified = fi.LastWriteTime;
            item.FileSize = fi.Length;
            _vm.RefreshThumbnail(item);
            _prefetch.Invalidate(_vm.SelectedIndex);
            LoadCurrentImage();
            if (_infoVisible) UpdateInfo(item);
        }
        catch { }
    }

    #endregion

    #region Cursor / Mouse

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePos.X) > 3 || Math.Abs(pos.Y - _lastMousePos.Y) > 3)
        {
            Cursor = Cursor.Default;
            _cursorTimer.Stop();
            _cursorTimer.Start();

            if (_isVideoActive)
                ShowVideoControlBar();

            if (!_filmstripPinned && pos.Y > Bounds.Height - 120 && !_isVideoActive)
            {
                ShowFilmstrip();
            }
        }
        _lastMousePos = pos;
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _cursorTimer.Stop();
        _positionFadeTimer.Stop();
        _zoomFadeTimer.Stop();
        _filmstripFadeTimer.Stop();
        _videoPositionTimer.Stop();
        _videoControlFadeTimer.Stop();
        _loadCts.Cancel();
        _loadCts.Dispose();
        _prefetch.Dispose();

        try
        {
            _mediaPlayer?.Stop();
        }
        catch { }

        VideoView.MediaPlayer = null;
        _currentVideoMedia?.Dispose();
        _currentVideoMedia = null;
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        _libVLC?.Dispose();
        _libVLC = null;

        base.OnClosed(e);
    }
}
