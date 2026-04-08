using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

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

    private bool _isDragging;
    private Point _dragStart;
    private Vector _dragOffset;

    private bool _filmstripPinned;
    private bool _filmstripVisible;
    private readonly DispatcherTimer _filmstripFadeTimer;
    private bool _suppressFilmstripNav;

    private const double ZoomStep = 0.15;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20.0;

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

        if (_vm is null) return;

        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_vm is null) return;

        _imageScale = (DisplayImage.RenderTransform as ScaleTransform) ?? new ScaleTransform(1, 1);
        DisplayImage.RenderTransform = _imageScale;

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
        LoadCurrentImage();
        _cursorTimer.Start();
    }

    #region Image Loading

    private async void LoadCurrentImage()
    {
        var item = _vm.SelectedItem;
        if (item is null || item.IsVideo || item.IsFolder) return;

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

        DisplayImage.Source = newImage;
        _prefetch.UpdatePosition(_vm.SelectedIndex, _vm.SortedImages.Count);

        ResetZoom();
        UpdateInfo(item);
        ShowPosition();
        SyncFilmstripSelection();
    }

    #endregion

    #region Zoom/Pan

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        _isFitToScreen = true;
        _imageScale.ScaleX = 1;
        _imageScale.ScaleY = 1;
        DisplayImage.MaxWidth = Bounds.Width > 0 ? Bounds.Width : 1920;
        DisplayImage.MaxHeight = Bounds.Height > 0 ? Bounds.Height : 1080;
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

    private void SetRating(int rating)
    {
        if (_vm.SelectedItem is null) return;
        _vm.SetRatingCommand.Execute(rating.ToString());
        UpdateInfo(_vm.SelectedItem);
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

            if (!_filmstripPinned && pos.Y > Bounds.Height - 120)
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
        _loadCts.Cancel();
        _loadCts.Dispose();
        _prefetch.Dispose();
        base.OnClosed(e);
    }
}
