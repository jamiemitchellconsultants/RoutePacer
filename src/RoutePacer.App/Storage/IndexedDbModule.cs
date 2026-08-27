using Microsoft.JSInterop;

namespace RoutePacer.App.Storage;

public interface IIndexedDbModule : IAsyncDisposable
{
    ValueTask<T?> InvokeAsync<T>(string identifier, object?[]? args = null);
    ValueTask InvokeVoidAsync(string identifier, object?[]? args = null);
}

public sealed class IndexedDbModule(IJSRuntime js) : IIndexedDbModule
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js").AsTask());

    public async ValueTask<T?> InvokeAsync<T>(string identifier, object?[]? args = null) => await (await module.Value).InvokeAsync<T>(identifier, args ?? []).ConfigureAwait(false);
    public async ValueTask InvokeVoidAsync(string identifier, object?[]? args = null) => await (await module.Value).InvokeVoidAsync(identifier, args ?? []).ConfigureAwait(false);
    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated) await (await module.Value).DisposeAsync();
    }
}
