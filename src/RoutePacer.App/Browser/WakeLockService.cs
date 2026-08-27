using Microsoft.JSInterop;

namespace RoutePacer.App.Browser;

public sealed class WakeLockService(IJSRuntime js) : IWakeLockService
{
    private IJSObjectReference? module; private DotNetObjectReference<Callbacks>? reference;
    public event Action<WakeLockStatus>? StatusChanged;
    public async Task AcquireAsync() { module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/wakelock.js"); reference ??= DotNetObjectReference.Create(new Callbacks(this)); await module.InvokeVoidAsync("acquireWakeLock", reference); }
    public async Task ReleaseAsync() { if (module is not null) await module.InvokeVoidAsync("releaseWakeLock"); }
    public async ValueTask DisposeAsync() { await ReleaseAsync(); if (module is not null) await module.DisposeAsync(); reference?.Dispose(); }
    private sealed class Callbacks(WakeLockService owner) { [JSInvokable] public void OnStatus(string status) => owner.StatusChanged?.Invoke(Enum.TryParse<WakeLockStatus>(status, true, out var value) ? value : WakeLockStatus.Failed); }
}
