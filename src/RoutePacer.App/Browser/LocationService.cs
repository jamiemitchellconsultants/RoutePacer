using Microsoft.JSInterop;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Browser;

public sealed class LocationService(IJSRuntime js) : ILocationService
{
    private IJSObjectReference? module; private DotNetObjectReference<LocationCallbacks>? reference;
    private Func<GeoFix, Task>? onFix; private Func<LocationFailure, Task>? onError;
    public async Task StartAsync(Func<GeoFix, Task> fix, Func<LocationFailure, Task> error, CancellationToken cancellationToken = default)
    {
        if (module is not null) return;
        onFix = fix; onError = error; module = await js.InvokeAsync<IJSObjectReference>("import", "./js/gps.js"); reference = DotNetObjectReference.Create(new LocationCallbacks(this)); await module.InvokeVoidAsync("startTracking", reference);
    }
    public async Task StopAsync() { if (module is not null) await module.InvokeVoidAsync("stopTracking"); }
    public async ValueTask DisposeAsync() { if (module is not null) await module.DisposeAsync(); reference?.Dispose(); module = null; reference = null; }
    private sealed class LocationCallbacks(LocationService owner)
    {
        [JSInvokable] public Task OnPosition(double timestamp, double latitude, double longitude, double accuracy, double? speed) => owner.onFix?.Invoke(new(DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp), latitude, longitude, accuracy, speed)) ?? Task.CompletedTask;
        [JSInvokable] public Task OnError(int code) => owner.onError?.Invoke(code switch { 1 => LocationFailure.PermissionDenied, 2 => LocationFailure.Unavailable, 3 => LocationFailure.Timeout, _ => LocationFailure.Unknown }) ?? Task.CompletedTask;
    }
}
