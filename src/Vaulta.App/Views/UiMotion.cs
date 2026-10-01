namespace Vaulta.App.Views;

internal static class UiMotion
{
    internal static bool ReducedMotion
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(26) && !Android.Animation.ValueAnimator.AreAnimatorsEnabled();
#else
            return false;
#endif
        }
    }

    public static async Task RevealAsync(VisualElement view)
    {
        view.CancelAnimations();
        if (ReducedMotion) { view.Opacity = 1; view.TranslationY = 0; return; }
        view.Opacity = 0;
        view.TranslationY = 8;
        await Task.WhenAll(view.FadeToAsync(1, 200, Easing.CubicOut), view.TranslateToAsync(0, 0, 200, Easing.CubicOut));
        view.Opacity = 1;
        view.TranslationY = 0;
    }

    public static void AttachPress(Button button)
    {
        button.Pressed += async (_, _) =>
        {
            if (ReducedMotion) return;
            button.CancelAnimations();
            await button.ScaleToAsync(0.98, 80, Easing.CubicOut);
        };
        button.Released += async (_, _) =>
        {
            button.CancelAnimations();
            if (ReducedMotion) { button.Scale = 1; return; }
            await button.ScaleToAsync(1, 120, Easing.CubicOut);
        };
        button.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == VisualElement.IsEnabledProperty.PropertyName && !button.IsEnabled)
            {
                button.CancelAnimations();
                button.Scale = 1;
            }
        };
        button.Unfocused += (_, _) => { button.CancelAnimations(); button.Scale = 1; };
        button.Unloaded += (_, _) => { button.CancelAnimations(); button.Scale = 1; };
    }
}
