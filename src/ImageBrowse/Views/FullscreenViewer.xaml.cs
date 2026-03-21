using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

    private const double ZoomStep = 0.15;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20.0;

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

        LoadCurrentImage();
        _cursorTimer.Start();
    }

    private async void LoadCurrentImage()
    {
        var item = _vm.SelectedItem;
        if (item is null) return;

        var cached = _prefetch.GetCached(_vm.SelectedIndex);
        var image = cached ?? await _prefetch.GetOrLoadAsync(_vm.SelectedIndex);
        DisplayImage.Source = image;

        _prefetch.UpdatePosition(_vm.SelectedIndex, _vm.SortedImages.Count);

        ResetZoom();
        UpdateInfo(item);
        ShowPosition();
    }

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        _isFitToScreen = true;
        ImageScale.ScaleX = 1;
        ImageScale.ScaleY = 1;
        DisplayImage.MaxWidth = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        DisplayImage.MaxHeight = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
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
        int idx = _vm.SelectedIndex + 1;
        int total = _vm.SortedImages.Count;
        PositionText.Text = $"{idx} / {total}";

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
        InfoDimensions.Text = item.ImageWidth > 0 ? $"Dimensions: {item.DimensionsDisplay}" : "";
        InfoFileSize.Text = $"Size: {item.FileSizeDisplay}";
        InfoDateTaken.Text = item.DateTaken.HasValue ? $"Taken: {item.DateTaken:yyyy-MM-dd HH:mm}" : "";
        InfoDateModified.Text = $"Modified: {item.DateModified:yyyy-MM-dd HH:mm}";
        InfoCamera.Text = !string.IsNullOrEmpty(item.CameraModel)
            ? $"Camera: {item.CameraManufacturer} {item.CameraModel}" : "";
        InfoLens.Text = !string.IsNullOrEmpty(item.LensModel) ? $"Lens: {item.LensModel}" : "";

        var expParts = new List<string>();
        if (!string.IsNullOrEmpty(item.FNumber)) expParts.Add(item.FNumber);
        if (!string.IsNullOrEmpty(item.ExposureTime)) expParts.Add(item.ExposureTime);
        if (item.Iso.HasValue) expParts.Add($"ISO {item.Iso}");
        if (!string.IsNullOrEmpty(item.FocalLength)) expParts.Add(item.FocalLength);
        InfoExposure.Text = expParts.Count > 0 ? string.Join("  |  ", expParts) : "";

        InfoRating.Text = item.Rating > 0 ? new string('\u2605', item.Rating) + new string('\u2606', 5 - item.Rating) : "";
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
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

    private void RotateCurrentImage()
    {
        var item = _vm.SelectedItem;
        if (item is null || item.IsFolder) return;

        try
        {
            var (newW, newH) = ImageRotationService.RotateClockwise90(item.FilePath);
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
            System.Diagnostics.Debug.WriteLine($"Rotation failed: {ex.Message}");
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

    private static void FadeIn(UIElement element)
    {
        var animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
        element.BeginAnimation(OpacityProperty, animation);
    }

    private static void FadeOut(UIElement element)
    {
        var animation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
        element.BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnClosed(EventArgs e)
    {
        _prefetch.Dispose();
        base.OnClosed(e);
    }
}
