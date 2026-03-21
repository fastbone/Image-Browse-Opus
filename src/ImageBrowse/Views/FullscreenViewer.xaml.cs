using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;
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

    public FullscreenViewer(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _prefetch = new ImagePrefetchService(new ImageLoadingService());

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cursorTimer.Tick += (_, _) =>
        {
            Cursor = Cursors.None;
            _cursorTimer.Stop();
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
            return (i.FilePath, i.IsFolder);
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

        if (item.ImageWidth > 0)
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
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                Close();
                break;

            case Key.Right:
            case Key.Space:
            case Key.PageDown:
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
        if (item is null || item.IsFolder) return;

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

            if (!_filmstripPinned && pos.Y > ActualHeight - 120)
                ShowFilmstrip();
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
        _loadCts.Cancel();
        _loadCts.Dispose();
        _prefetch.Dispose();
        base.OnClosed(e);
    }
}
