namespace Laura.App.Infrastructure;

/// <summary>Starts native navigation immediately and animates the destination without recreating cached pages.</summary>
internal static class NavigationMotion
{
    private static readonly global::Windows.UI.ViewManagement.UISettings Accessibility = new();
    /// <summary>Gets the shared navigation transition duration in milliseconds.</summary>
    internal const uint Duration = 300;

    /// <summary>Opens a page immediately with a short, accessible arrival transition.</summary>
    internal static async Task PushAsync(INavigation navigation, Page page)
    {
        using var performance = PerformanceTrace.Measure("navigation.present." + page.GetType().Name);
        Task presentation = navigation.PushAsync(page, false);
        Task arrival = ArriveAsync(page, 12);
        await presentation.ConfigureAwait(true); performance.Dispose();
        await arrival.ConfigureAwait(true);
    }

    /// <summary>Returns to the retained page and animates its arrival in the opposite direction.</summary>
    internal static async Task PopAsync(INavigation navigation)
    {
        Page? destination = navigation.NavigationStack.Count > 1 ? navigation.NavigationStack[^2] : null;
        Task presentation = navigation.PopAsync(false);
        Task arrival = destination is null ? Task.CompletedTask : ArriveAsync(destination, -12);
        await presentation.ConfigureAwait(true); await arrival.ConfigureAwait(true);
    }

    /// <summary>Uses a single lightweight native animation, honoring Windows' reduced-motion preference.</summary>
    private static Task ArriveAsync(Page page, double offset)
    {
        if (page is not ContentPage { Content: View view } || !Accessibility.AnimationsEnabled) { return Task.CompletedTask; }
        view.AbortAnimation("NavigationArrival");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.Opacity = .94; view.TranslationX = offset;
        var animation = new Animation(progress => { view.Opacity = .94 + .06 * progress; view.TranslationX = offset * (1 - progress); });
        animation.Commit(view, "NavigationArrival", 16, Duration, Easing.CubicOut, (_, _) =>
        { view.Opacity = 1; view.TranslationX = 0; completion.TrySetResult(); });
        return completion.Task;
    }
}
