using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;
using LibVLCSharp.Shared;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ImageBrowse.Views;

public partial class FullscreenViewer : Window
{
    private readonly MainViewModel _vm;
    private readonly ImagePrefetchService _prefetch;
    private double _zoomLevel = 1.0;
    private bool _infoVisible;
    private bool _isFitToScreen = true;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _positionFadeTimer;
    private readonly DispatcherTimer _zoomFadeTimer;
    private Point _lastMousePos;
    private CancellationTokenSource _loadCts = new();
    private int _loadSequence;

    private bool _isDragging;
    private Point _dragStart;
    private double _dragHOffset;
    private double _dragVOffset;

    private bool _filmstripPinned;
    private bool _filmstripVisible;
    private readonly DispatcherTimer _filmstripFadeTimer;
    private bool _suppressFilmstripNav;
    private CollectionViewSource? _filmstripViewSource;

    private bool _miniMapDragging;
    private double _manipulationCumulativeX;

    private const double ZoomStep = 0.15;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20.0;
    private const int CrossfadeDurationMs = 150;

    private LibVLC? _libVLC;
    private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
    private bool _isVideoActive;
    private bool _isSeeking;
    private readonly DispatcherTimer _videoPositionTimer;
    private readonly DispatcherTimer _controlBarFadeTimer;
    private bool _controlBarVisible;

    private double _videoZoomLevel = 1.0;
    private double _videoCropX, _videoCropY;
    private uint _videoPixelW, _videoPixelH;
    private bool _videoZoomActive;
    private bool _videoMiniMapDragging;

    private static readonly float[] PlaybackSpeeds = [0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f];
    private int _speedIndex = 2; // 1.0x

    public FullscreenViewer(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _prefetch = new ImagePrefetchService(new ImageLoadingService());

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cursorTimer.Tick += (_, _) =>
        {
            Cursor = Cursors.None;
            if (_isVideoActive) HideVideoControlBar();
            _cursorTimer.Stop();
        };

        _videoPositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoPositionTimer.Tick += (_, _) => UpdateVideoPosition();

        _controlBarFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _controlBarFadeTimer.Tick += (_, _) =>
        {
            HideVideoControlBar();
            _controlBarFadeTimer.Stop();
        };

        _positionFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _positionFadeTimer.Tick += (_, _) =>
        {
            FadeOut(PositionIndicator);
            _positionFadeTimer.Stop();
        };

        _zoomFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
        _zoomFadeTimer.Tick += (_, _) =>
        {
            FadeOut(ZoomIndicator);
            _zoomFadeTimer.Stop();
        };

        _filmstripFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _filmstripFadeTimer.Tick += (_, _) =>
        {
            if (!_filmstripPinned)
                HideFilmstrip();
            _filmstripFadeTimer.Stop();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        int screenMax = (int)Math.Max(ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth,
                                       ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight);
        _prefetch.Configure(screenMax * 2, idx =>
        {
            if (idx < 0 || idx >= _vm.SortedImages.Count)
                return ("", true);
            var i = _vm.SortedImages[idx];
            return (i.FilePath, i.IsFolder || i.IsVideo);
        });

        _filmstripViewSource = new CollectionViewSource { Source = _vm.SortedImages };
        _filmstripViewSource.Filter += (_, e) =>
        {
            e.Accepted = e.Item is ImageItem item && !item.IsFolder;
        };
        FilmstripList.ItemsSource = _filmstripViewSource.View;

        LoadCurrentImage(crossfade: false);
        _cursorTimer.Start();
    }

    private async void LoadCurrentImage(bool crossfade = true)
    {
        var item = _vm.SelectedItem;
        if (item is null) return;

        StopVideoPlayback();

        if (item.IsVideo)
        {
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

        System.Windows.Media.Imaging.BitmapSource? newImage = null;

        var cached = _prefetch.GetCached(_vm.SelectedIndex);
        if (cached is not null)
        {
            newImage = cached;
        }
        else
        {
            var image = await _prefetch.GetOrLoadAsync(_vm.SelectedIndex);
            if (ct.IsCancellationRequested || seq != _loadSequence) return;
            newImage = image;
        }

        bool animate = crossfade && _vm.Settings.EnableAnimations;

        if (animate && DisplayImage.Source is not null && newImage is not null)
        {
            BackImage.Source = DisplayImage.Source;
            BackImageScale.ScaleX = ImageScale.ScaleX;
            BackImageScale.ScaleY = ImageScale.ScaleY;
            BackImage.MaxWidth = DisplayImage.MaxWidth;
            BackImage.MaxHeight = DisplayImage.MaxHeight;
            BackImage.Opacity = 1;

            DisplayImage.Source = newImage;
            DisplayImage.Opacity = 0;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(CrossfadeDurationMs));
            var fadeOutBack = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(CrossfadeDurationMs));
            fadeOutBack.Completed += (_, _) => BackImage.Source = null;

            DisplayImage.BeginAnimation(OpacityProperty, fadeIn);
            BackImage.BeginAnimation(OpacityProperty, fadeOutBack);
        }
        else
        {
            DisplayImage.BeginAnimation(OpacityProperty, null);
            DisplayImage.Source = newImage;
            DisplayImage.Opacity = 1;
        }

        _prefetch.UpdatePosition(_vm.SelectedIndex, _vm.SortedImages.Count);

        ResetZoom();
        UpdateInfo(item);
        ShowPosition();
        SyncFilmstripSelection();
    }

    private void EnsureVlcInitialized()
    {
        if (_libVLC is not null) return;
        _libVLC = new LibVLC();
        _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
        _mediaPlayer.EndReached += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _videoPositionTimer.Stop();
            PlayPauseIcon.Text = "\uE768"; // Play
        });
    }

    private void ShowVideoView(ImageItem item)
    {
        _isVideoActive = true;
        ImageScroller.Visibility = Visibility.Collapsed;
        VideoView.Visibility = Visibility.Visible;
        NavLeft.Visibility = Visibility.Collapsed;
        NavRight.Visibility = Visibility.Collapsed;

        try
        {
            EnsureVlcInitialized();
            VideoView.MediaPlayer = _mediaPlayer;

            using var media = new Media(_libVLC!, item.FilePath, FromType.FromPath);
            _mediaPlayer!.Play(media);
            _videoPositionTimer.Start();
            PlayPauseIcon.Text = "\uE769"; // Pause

            if (_mediaPlayer.Volume != (int)VolumeSlider.Value)
                _mediaPlayer.Volume = (int)VolumeSlider.Value;

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
        _isVideoActive = false;
        ResetVideoZoom();
        VideoView.Visibility = Visibility.Collapsed;
        ImageScroller.Visibility = Visibility.Visible;
        NavLeft.Visibility = Visibility.Visible;
        NavRight.Visibility = Visibility.Visible;
        HideVideoControlBar();
    }

    private void StopVideoPlayback()
    {
        if (_mediaPlayer is null) return;
        _videoPositionTimer.Stop();
        ResetVideoZoom();

        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                var stopped = new ManualResetEventSlim(false);
                void handler(object? s, EventArgs a) => stopped.Set();
                _mediaPlayer.Stopped += handler;
                _mediaPlayer.Stop();
                stopped.Wait(2000);
                _mediaPlayer.Stopped -= handler;
            }
        }
        catch { }

        VideoView.MediaPlayer = null;

        _speedIndex = 2;
        _mediaPlayer?.SetRate(1.0f);
    }

    private void ToggleVideoPlayPause()
    {
        if (_mediaPlayer is null) return;

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            _videoPositionTimer.Stop();
            PlayPauseIcon.Text = "\uE768"; // Play
        }
        else
        {
            _mediaPlayer.Play();
            _videoPositionTimer.Start();
            PlayPauseIcon.Text = "\uE769"; // Pause
        }
    }

    private void UpdateVideoPosition()
    {
        if (_mediaPlayer is null || _isSeeking) return;

        long time = _mediaPlayer.Time;
        long length = _mediaPlayer.Length;
        if (length <= 0) return;

        VideoTimeText.Text = FormatTime(time);
        VideoDurationText.Text = FormatTime(length);
        VideoSeekBar.Value = (double)time / length * 1000;
    }

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => ToggleVideoPlayPause();

    private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;

    private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_mediaPlayer is null) return;
        long length = _mediaPlayer.Length;
        if (length > 0)
            _mediaPlayer.Time = (long)(VideoSeekBar.Value / 1000 * length);
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSeeking || _mediaPlayer is null) return;
        long length = _mediaPlayer.Length;
        if (length > 0)
            VideoTimeText.Text = FormatTime((long)(e.NewValue / 1000 * length));
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        _mediaPlayer.Mute = !_mediaPlayer.Mute;
        VolumeIcon.Text = _mediaPlayer.Mute ? "\uE74F" : "\uE767";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaPlayer is null) return;
        _mediaPlayer.Volume = (int)e.NewValue;
    }

    private void ShowVideoControlBar()
    {
        _controlBarFadeTimer.Stop();
        _controlBarFadeTimer.Start();
        if (_controlBarVisible) return;
        _controlBarVisible = true;
        FadeIn(VideoControlBar);
    }

    private void HideVideoControlBar()
    {
        if (!_controlBarVisible) return;
        _controlBarVisible = false;
        FadeOut(VideoControlBar);
    }

    private void VideoNavLeft_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeIn(VideoNavLeft);
        Cursor = Cursors.Arrow;
    }

    private void VideoNavRight_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeIn(VideoNavRight);
        Cursor = Cursors.Arrow;
    }

    private void VideoNavZone_MouseLeave(object sender, MouseEventArgs e)
    {
        FadeOut(VideoNavLeft);
        FadeOut(VideoNavRight);
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

        VideoMiniMapImage.Source = _vm.SelectedItem?.Thumbnail;
        VideoMiniMapPanel.Visibility = Visibility.Visible;
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
        UpdateVideoMiniMap();
    }

    private void ResetVideoZoom()
    {
        _videoZoomActive = false;
        _videoZoomLevel = 1.0;
        _videoCropX = 0;
        _videoCropY = 0;
        if (_mediaPlayer is not null)
            _mediaPlayer.CropGeometry = "";
        VideoMiniMapPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateVideoMiniMap()
    {
        if (_videoPixelW == 0 || _videoPixelH == 0) return;

        double canvasW = VideoMiniMapCanvas.ActualWidth;
        double canvasH = VideoMiniMapCanvas.ActualHeight;
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

    private void VideoMiniMap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _videoMiniMapDragging = true;
        VideoMiniMapPanel.CaptureMouse();
        VideoMiniMapNavigateTo(e.GetPosition(VideoMiniMapCanvas));
        e.Handled = true;
    }

    private void VideoMiniMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (_videoMiniMapDragging)
        {
            VideoMiniMapNavigateTo(e.GetPosition(VideoMiniMapCanvas));
            e.Handled = true;
        }
    }

    private void VideoMiniMap_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_videoMiniMapDragging)
        {
            _videoMiniMapDragging = false;
            VideoMiniMapPanel.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void VideoMiniMapNavigateTo(Point canvasPos)
    {
        if (_videoPixelW == 0 || _videoPixelH == 0) return;

        double canvasW = VideoMiniMapCanvas.ActualWidth;
        double canvasH = VideoMiniMapCanvas.ActualHeight;
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

    private void CyclePlaybackSpeed(int direction)
    {
        if (_mediaPlayer is null) return;
        _speedIndex = Math.Clamp(_speedIndex + direction, 0, PlaybackSpeeds.Length - 1);
        _mediaPlayer.SetRate(PlaybackSpeeds[_speedIndex]);
        SpeedText.Text = $"{PlaybackSpeeds[_speedIndex]:0.##}x";
    }

    private void SpeedButton_Click(object sender, MouseButtonEventArgs e)
    {
        CyclePlaybackSpeed(1);
        e.Handled = true;
    }

    private void SpeedButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        CyclePlaybackSpeed(-1);
        e.Handled = true;
    }

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        _isFitToScreen = true;
        ImageScale.ScaleX = 1;
        ImageScale.ScaleY = 1;
        DisplayImage.MaxWidth = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        DisplayImage.MaxHeight = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
        UpdateMiniMapVisibility();
    }

    private void ApplyZoom(double newZoom)
    {
        _zoomLevel = Math.Clamp(newZoom, MinZoom, MaxZoom);
        _isFitToScreen = false;
        DisplayImage.MaxWidth = double.PositiveInfinity;
        DisplayImage.MaxHeight = double.PositiveInfinity;
        ImageScale.ScaleX = _zoomLevel;
        ImageScale.ScaleY = _zoomLevel;
        ShowZoom();
        UpdateMiniMapVisibility();
    }

    private void FitToScreen()
    {
        ResetZoom();
        ShowZoom();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFitToScreen)
        {
            DisplayImage.MaxWidth = ActualWidth;
            DisplayImage.MaxHeight = ActualHeight;
        }
    }

    private void ActualSize()
    {
        if (DisplayImage.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return;

        double screenW = ActualWidth;
        double screenH = ActualHeight;
        double imgW = bmp.PixelWidth;
        double imgH = bmp.PixelHeight;

        double fitScale = Math.Min(screenW / imgW, screenH / imgH);
        _zoomLevel = 1.0 / fitScale;
        _isFitToScreen = false;

        ImageScale.ScaleX = _zoomLevel;
        ImageScale.ScaleY = _zoomLevel;
        ShowZoom();
    }

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

        FadeIn(PositionIndicator);
        _positionFadeTimer.Stop();
        _positionFadeTimer.Start();
    }

    private void ShowZoom()
    {
        string text = _isFitToScreen ? "Fit" : $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = text;
        FadeIn(ZoomIndicator);
        _zoomFadeTimer.Stop();
        _zoomFadeTimer.Start();
    }

    private void UpdateInfo(ImageItem item)
    {
        InfoFileName.Text = item.FileName;

        if (item.IsVideo && item.Duration.HasValue)
        {
            InfoDimensions.Text = item.DurationDisplay;
            InfoDimensionsGrid.Visibility = Visibility.Visible;
        }
        else if (item.ImageWidth > 0)
        {
            InfoDimensions.Text = item.DimensionsDisplay;
            InfoDimensionsGrid.Visibility = Visibility.Visible;
        }
        else
        {
            InfoDimensionsGrid.Visibility = Visibility.Collapsed;
        }

        InfoFileSize.Text = item.FileSizeDisplay;

        if (item.DateTaken.HasValue)
        {
            InfoDateTaken.Text = item.DateTaken.Value.ToString("yyyy-MM-dd HH:mm");
            InfoDateTakenGrid.Visibility = Visibility.Visible;
        }
        else
        {
            InfoDateTakenGrid.Visibility = Visibility.Collapsed;
        }

        InfoDateModified.Text = item.DateModified.ToString("yyyy-MM-dd HH:mm");

        bool hasCamera = !string.IsNullOrEmpty(item.CameraModel);
        bool hasLens = !string.IsNullOrEmpty(item.LensModel);

        if (hasCamera)
        {
            InfoCamera.Text = $"{item.CameraManufacturer} {item.CameraModel}".Trim();
            InfoCameraGrid.Visibility = Visibility.Visible;
        }
        else
        {
            InfoCameraGrid.Visibility = Visibility.Collapsed;
        }

        if (hasLens)
        {
            InfoLens.Text = item.LensModel;
            InfoLensGrid.Visibility = Visibility.Visible;
        }
        else
        {
            InfoLensGrid.Visibility = Visibility.Collapsed;
        }

        var expParts = new List<string>();
        if (!string.IsNullOrEmpty(item.FNumber)) expParts.Add(item.FNumber);
        if (!string.IsNullOrEmpty(item.ExposureTime)) expParts.Add(item.ExposureTime);
        if (item.Iso.HasValue) expParts.Add($"ISO {item.Iso}");
        if (!string.IsNullOrEmpty(item.FocalLength)) expParts.Add(item.FocalLength);

        if (expParts.Count > 0)
        {
            InfoExposure.Text = string.Join("  |  ", expParts);
            InfoExposureGrid.Visibility = Visibility.Visible;
        }
        else
        {
            InfoExposureGrid.Visibility = Visibility.Collapsed;
        }

        CameraSeparator.Visibility = hasCamera || hasLens || expParts.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        InfoRatingStars.Rating = item.Rating;
    }

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

    // Nav hover zones
    private void NavLeft_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeIn(NavLeft);
        Cursor = Cursors.Arrow;
    }

    private void NavRight_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeIn(NavRight);
        Cursor = Cursors.Arrow;
    }

    private void NavZone_MouseLeave(object sender, MouseEventArgs e)
    {
        FadeOut(NavLeft);
        FadeOut(NavRight);
    }

    private void NavLeft_Click(object sender, MouseButtonEventArgs e)
    {
        NavigatePrevious();
        e.Handled = true;
    }

    private void NavRight_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateNext();
        e.Handled = true;
    }

    // Drag-pan when zoomed
    private void ImageScroller_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isFitToScreen)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ImageScroller);
            _dragHOffset = ImageScroller.HorizontalOffset;
            _dragVOffset = ImageScroller.VerticalOffset;
            ImageScroller.Cursor = Cursors.Hand;
            ImageScroller.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ImageScroller_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ImageScroller.ReleaseMouseCapture();
            ImageScroller.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void ImageScroller_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetPosition(ImageScroller);
            double dx = _dragStart.X - pos.X;
            double dy = _dragStart.Y - pos.Y;
            ImageScroller.ScrollToHorizontalOffset(_dragHOffset + dx);
            ImageScroller.ScrollToVerticalOffset(_dragVOffset + dy);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_isVideoActive)
        {
            HandleVideoKeyDown(e);
            if (e.Handled) return;
        }

        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                Close();
                break;

            case Key.Right:
            case Key.PageDown:
                NavigateNext();
                e.Handled = true;
                break;

            case Key.Space:
                NavigateNext();
                e.Handled = true;
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

            case Key.D1 when Keyboard.Modifiers == ModifierKeys.Control:
                ActualSize();
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

            case Key.R when Keyboard.Modifiers == ModifierKeys.None:
                RotateCurrentImage();
                e.Handled = true;
                break;

            case Key.Delete:
                DeleteCurrentImage();
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

            case Key.D1 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad1 when Keyboard.Modifiers == ModifierKeys.None:
                SetRating(1);
                e.Handled = true;
                break;
            case Key.D2 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad2 when Keyboard.Modifiers == ModifierKeys.None:
                SetRating(2);
                e.Handled = true;
                break;
            case Key.D3 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad3 when Keyboard.Modifiers == ModifierKeys.None:
                SetRating(3);
                e.Handled = true;
                break;
            case Key.D4 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad4 when Keyboard.Modifiers == ModifierKeys.None:
                SetRating(4);
                e.Handled = true;
                break;
            case Key.D5 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad5 when Keyboard.Modifiers == ModifierKeys.None:
                SetRating(5);
                e.Handled = true;
                break;
        }
    }

    private void HandleVideoKeyDown(KeyEventArgs e)
    {
        if (_mediaPlayer is null) return;
        switch (e.Key)
        {
            case Key.Space:
                ToggleVideoPlayPause();
                e.Handled = true;
                break;
            case Key.Left:
            {
                long delta = Keyboard.Modifiers == ModifierKeys.Shift ? 30000 : 5000;
                _mediaPlayer.Time = Math.Max(_mediaPlayer.Time - delta, 0);
                ShowVideoControlBar();
                e.Handled = true;
                break;
            }
            case Key.Right:
            {
                long delta = Keyboard.Modifiers == ModifierKeys.Shift ? 30000 : 5000;
                long length = _mediaPlayer.Length;
                if (length > 0)
                    _mediaPlayer.Time = Math.Min(_mediaPlayer.Time + delta, length);
                ShowVideoControlBar();
                e.Handled = true;
                break;
            }
            case Key.Up:
                _mediaPlayer.Volume = Math.Min(_mediaPlayer.Volume + 5, 100);
                VolumeSlider.Value = _mediaPlayer.Volume;
                e.Handled = true;
                break;
            case Key.Down:
                _mediaPlayer.Volume = Math.Max(_mediaPlayer.Volume - 5, 0);
                VolumeSlider.Value = _mediaPlayer.Volume;
                e.Handled = true;
                break;
            case Key.M:
                _mediaPlayer.Mute = !_mediaPlayer.Mute;
                VolumeIcon.Text = _mediaPlayer.Mute ? "\uE74F" : "\uE767";
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

    private void DeleteCurrentImage()
    {
        var item = _vm.SelectedItem;
        if (item is null || item.IsFolder) return;

        if (_vm.Settings.ConfirmBeforeDelete)
        {
            var result = MessageBox.Show(this,
                $"Move \"{item.FileName}\" to the Recycle Bin?\n\nYou can disable this confirmation in Settings.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        if (item.IsVideo)
            StopVideoPlayback();

        if (FileOperationService.MoveToRecycleBin(item.FilePath))
        {
            _prefetch.Invalidate(_vm.SelectedIndex);
            int currentIdx = _vm.SelectedIndex;
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
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not rotate \"{item.FileName}\":\n{ex.Message}",
                "Rotation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetRating(int rating)
    {
        if (_vm.SelectedItem is null) return;
        _vm.SetRatingCommand.Execute(rating.ToString());
        UpdateInfo(_vm.SelectedItem);
    }

    private void ToggleInfo()
    {
        _infoVisible = !_infoVisible;
        if (_infoVisible)
            FadeIn(InfoPanel);
        else
            FadeOut(InfoPanel);
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isVideoActive && _videoZoomActive)
        {
            double factor = e.Delta > 0 ? (1 + ZoomStep) : (1 / (1 + ZoomStep));
            ApplyVideoZoom(_videoZoomLevel * factor);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            double factor = e.Delta > 0 ? (1 + ZoomStep) : (1 / (1 + ZoomStep));
            ApplyZoom(_zoomLevel * factor);
        }
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePos.X) > 3 || Math.Abs(pos.Y - _lastMousePos.Y) > 3)
        {
            Cursor = Cursors.Arrow;
            _cursorTimer.Stop();
            _cursorTimer.Start();

            if (_isVideoActive)
            {
                ShowVideoControlBar();
            }
            else if (!_filmstripPinned && pos.Y > ActualHeight - 120)
            {
                ShowFilmstrip();
            }
        }
        _lastMousePos = pos;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            NavigatePrevious();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            NavigateNext();
            e.Handled = true;
        }
    }

    private void FadeIn(UIElement element)
    {
        if (_vm.Settings.EnableAnimations)
        {
            var animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            element.BeginAnimation(OpacityProperty, animation);
        }
        else
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
        }
    }

    private void FadeOut(UIElement element)
    {
        if (_vm.Settings.EnableAnimations)
        {
            var animation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            element.BeginAnimation(OpacityProperty, animation);
        }
        else
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 0;
        }
    }

    private void ImageScroller_ManipulationStarting(object? sender, ManipulationStartingEventArgs e)
    {
        e.ManipulationContainer = ImageScroller;
        e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
        _manipulationCumulativeX = 0;
        e.Handled = true;
    }

    private void ImageScroller_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
    {
        double scaleX = e.DeltaManipulation.Scale.X;
        double scaleY = e.DeltaManipulation.Scale.Y;
        double avgScale = (scaleX + scaleY) / 2.0;

        bool isPinching = Math.Abs(avgScale - 1.0) > 0.001;

        if (isPinching)
        {
            ApplyZoom(_zoomLevel * avgScale);
        }
        else if (!_isFitToScreen)
        {
            ImageScroller.ScrollToHorizontalOffset(
                ImageScroller.HorizontalOffset - e.DeltaManipulation.Translation.X);
            ImageScroller.ScrollToVerticalOffset(
                ImageScroller.VerticalOffset - e.DeltaManipulation.Translation.Y);
        }
        else
        {
            _manipulationCumulativeX += e.DeltaManipulation.Translation.X;
        }

        e.Handled = true;
    }

    private void ImageScroller_ManipulationCompleted(object? sender, ManipulationCompletedEventArgs e)
    {
        const double swipeThreshold = 80;
        if (_isFitToScreen)
        {
            if (_manipulationCumulativeX > swipeThreshold)
                NavigatePrevious();
            else if (_manipulationCumulativeX < -swipeThreshold)
                NavigateNext();
        }
        _manipulationCumulativeX = 0;
        e.Handled = true;
    }

    private void UpdateMiniMapVisibility()
    {
        if (_isFitToScreen)
        {
            MiniMapPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            MiniMapPanel.Visibility = Visibility.Visible;
            MiniMapImage.Source = DisplayImage.Source;
            UpdateMiniMapLayout();
        }
    }

    private void UpdateMiniMapLayout()
    {
        if (MiniMapPanel.Visibility != Visibility.Visible) return;

        double canvasW = MiniMapCanvas.ActualWidth;
        double canvasH = MiniMapCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return;

        if (DisplayImage.Source is System.Windows.Media.Imaging.BitmapSource bmp)
        {
            double imgW = bmp.PixelWidth;
            double imgH = bmp.PixelHeight;
            double scale = Math.Min(canvasW / imgW, canvasH / imgH);
            double mw = imgW * scale;
            double mh = imgH * scale;
            double mx = (canvasW - mw) / 2;
            double my = (canvasH - mh) / 2;

            Canvas.SetLeft(MiniMapImage, mx);
            Canvas.SetTop(MiniMapImage, my);
            MiniMapImage.Width = mw;
            MiniMapImage.Height = mh;

            double extentW = ImageScroller.ExtentWidth;
            double extentH = ImageScroller.ExtentHeight;
            if (extentW <= 0 || extentH <= 0) return;

            double vpX = ImageScroller.HorizontalOffset / extentW * mw + mx;
            double vpY = ImageScroller.VerticalOffset / extentH * mh + my;
            double vpW = Math.Min(ImageScroller.ViewportWidth / extentW * mw, mw);
            double vpH = Math.Min(ImageScroller.ViewportHeight / extentH * mh, mh);

            Canvas.SetLeft(MiniMapViewport, vpX);
            Canvas.SetTop(MiniMapViewport, vpY);
            MiniMapViewport.Width = Math.Max(vpW, 4);
            MiniMapViewport.Height = Math.Max(vpH, 4);
        }
    }

    private void ImageScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateMiniMapLayout();
    }

    private void MiniMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _miniMapDragging = true;
        MiniMapPanel.CaptureMouse();
        MiniMapNavigateTo(e.GetPosition(MiniMapCanvas));
        e.Handled = true;
    }

    private void MiniMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (_miniMapDragging)
        {
            MiniMapNavigateTo(e.GetPosition(MiniMapCanvas));
            e.Handled = true;
        }
    }

    private void MiniMap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_miniMapDragging)
        {
            _miniMapDragging = false;
            MiniMapPanel.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void MiniMapNavigateTo(Point canvasPos)
    {
        if (DisplayImage.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return;

        double canvasW = MiniMapCanvas.ActualWidth;
        double canvasH = MiniMapCanvas.ActualHeight;
        double imgW = bmp.PixelWidth;
        double imgH = bmp.PixelHeight;
        double scale = Math.Min(canvasW / imgW, canvasH / imgH);
        double mw = imgW * scale;
        double mh = imgH * scale;
        double mx = (canvasW - mw) / 2;
        double my = (canvasH - mh) / 2;

        double relX = (canvasPos.X - mx) / mw;
        double relY = (canvasPos.Y - my) / mh;
        relX = Math.Clamp(relX, 0, 1);
        relY = Math.Clamp(relY, 0, 1);

        double targetH = relX * ImageScroller.ExtentWidth - ImageScroller.ViewportWidth / 2;
        double targetV = relY * ImageScroller.ExtentHeight - ImageScroller.ViewportHeight / 2;
        ImageScroller.ScrollToHorizontalOffset(targetH);
        ImageScroller.ScrollToVerticalOffset(targetV);
    }

    private void ShowFilmstrip()
    {
        if (_filmstripVisible) return;
        _filmstripVisible = true;
        FadeIn(FilmstripPanel);
        PositionIndicator.Margin = new Thickness(0, 0, 0, 100);
        _filmstripFadeTimer.Stop();
        _filmstripFadeTimer.Start();
        SyncFilmstripSelection();
    }

    private void HideFilmstrip()
    {
        if (!_filmstripVisible) return;
        _filmstripVisible = false;
        FadeOut(FilmstripPanel);
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

    private void FilmstripList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilmstripNav) return;
        if (FilmstripList.SelectedItem is not ImageItem item) return;

        int idx = _vm.SortedImages.IndexOf(item);
        if (idx < 0) return;

        _vm.SelectedIndex = idx;
        _vm.SelectedItem = item;
        LoadCurrentImage();
    }

    private void Filmstrip_MouseEnter(object sender, MouseEventArgs e)
    {
        _filmstripFadeTimer.Stop();
    }

    private void Filmstrip_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_filmstripPinned)
            _filmstripFadeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cursorTimer.Stop();
        _positionFadeTimer.Stop();
        _zoomFadeTimer.Stop();
        _filmstripFadeTimer.Stop();
        _videoPositionTimer.Stop();
        _controlBarFadeTimer.Stop();
        _loadCts.Cancel();
        _loadCts.Dispose();

        StopVideoPlayback();
        _mediaPlayer?.Dispose();
        _libVLC?.Dispose();

        _prefetch.Dispose();
        base.OnClosed(e);
    }
}
