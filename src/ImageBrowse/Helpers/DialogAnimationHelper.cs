using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ImageBrowse.Helpers;

public static class DialogAnimationHelper
{
    public static void AnimateOpen(Window window, bool enableAnimations)
    {
        if (!enableAnimations) return;

        var content = window.Content as FrameworkElement;
        if (content is null) return;

        var translate = new TranslateTransform(0, 30);
        content.RenderTransform = translate;
        content.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var slideUp = new DoubleAnimation(30, 0, duration) { EasingFunction = ease };
        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };

        translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }
}
