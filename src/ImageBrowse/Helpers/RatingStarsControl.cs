using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImageBrowse.Helpers;

public class RatingStarsControl : StackPanel
{
    private static readonly Geometry StarGeometry = Geometry.Parse(
        "M12,2 L15.09,8.26 22,9.27 17,14.14 18.18,21.02 12,17.77 5.82,21.02 7,14.14 2,9.27 8.91,8.26Z");

    public static readonly DependencyProperty RatingProperty =
        DependencyProperty.Register(nameof(Rating), typeof(int), typeof(RatingStarsControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnRatingChanged));

    public static readonly DependencyProperty StarSizeProperty =
        DependencyProperty.Register(nameof(StarSize), typeof(double), typeof(RatingStarsControl),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure, OnRatingChanged));

    public static readonly DependencyProperty StarFillProperty =
        DependencyProperty.Register(nameof(StarFill), typeof(Brush), typeof(RatingStarsControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), FrameworkPropertyMetadataOptions.AffectsRender, OnRatingChanged));

    public int Rating
    {
        get => (int)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public double StarSize
    {
        get => (double)GetValue(StarSizeProperty);
        set => SetValue(StarSizeProperty, value);
    }

    public Brush StarFill
    {
        get => (Brush)GetValue(StarFillProperty);
        set => SetValue(StarFillProperty, value);
    }

    public RatingStarsControl()
    {
        Orientation = Orientation.Horizontal;
    }

    private static void OnRatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RatingStarsControl)d).Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        int rating = Math.Clamp(Rating, 0, 5);
        if (rating <= 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;

        for (int i = 0; i < 5; i++)
        {
            bool filled = i < rating;
            var path = new Path
            {
                Data = StarGeometry,
                Fill = filled ? StarFill : Brushes.Transparent,
                Stroke = filled ? null : StarFill,
                StrokeThickness = filled ? 0 : 1.2,
                Width = StarSize,
                Height = StarSize,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 1, 0),
                SnapsToDevicePixels = true,
            };
            Children.Add(path);
        }
    }
}
