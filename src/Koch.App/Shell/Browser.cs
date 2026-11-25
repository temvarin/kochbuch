using Microsoft.JSInterop;

namespace Koch.App.Shell;

/// <summary>Browser features without a .NET counterpart (wwwroot/browser.js).</summary>
public sealed class Browser(IJSRuntime js)
{
    private Func<Task>? visible;

    /// <summary>Asks the browser to keep the app's storage when space runs low. Most browsers decide on their own.</summary>
    public Task<bool> ProtectStorageAsync() => js.InvokeAsync<bool>("koch.protectStorage").AsTask();

    /// <summary>One callback; a later call replaces it.</summary>
    public async Task WhenVisibleAsync(Func<Task> callback)
    {
        var listening = visible is not null;
        visible = callback;
        if (!listening) await js.InvokeVoidAsync("koch.whenVisible", DotNetObjectReference.Create(this));
    }

    [JSInvokable]
    public Task OnVisible() => visible?.Invoke() ?? Task.CompletedTask;

    /// <summary>While cooking; where the browser has no wake lock, the screen sleeps as usual.</summary>
    public Task KeepScreenOnAsync() => js.InvokeVoidAsync("koch.keepScreenOn").AsTask();

    public Task LetScreenSleepAsync() => js.InvokeVoidAsync("koch.letScreenSleep").AsTask();

    public Task SetLangAsync(string lang) => js.InvokeVoidAsync("koch.setLang", lang).AsTask();
}
