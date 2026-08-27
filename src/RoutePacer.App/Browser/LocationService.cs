using Microsoft.JSInterop;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Browser;

public sealed class LocationService(IJSRuntime js) : ILocationService
{
    private IJSObjectReference? module; private DotNetObjectReference<LocationCallbacks>? reference; private bool watching;
    private Func<GeoFix, Task>? onFix; private Func<LocationFailure, Task>? onError;

    public async Task StartAsync(Func<GeoFix, Task> fix, Func<LocationFailure, Task> error, CancellationToken cancellationToken = default)
    {
        if (watching) return;
        onFix = fix; onError = error;
        module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./js/gps.js");
        reference ??= DotNetObjectReference.Create(new LocationCallbacks(this));
        await module.InvokeVoidAsync("startTracking", cancellationToken, reference);
        watching = true;
    }

    public async Task StopAsync()
    {
        if (!watching || module is null) return;
        watching = false;
        await module.InvokeVoidAsync("stopTracking");
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { if (watching) await module.InvokeVoidAsync("stopTracking"); await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        watching = false; reference?.Dispose(); module = null; reference = null;
    }

    private sealed class LocationCallbacks(LocationService owner)
    {
        [JSInvokable]
        public Task OnPosition(double timestamp, double latitude, double longitude, double accuracy, double? speed)
        {
            if (!double.IsFinite(timestamp) || !double.IsFinite(latitude) || !double.IsFinite(longitude) || !double.IsFinite(accuracy)) return Task.CompletedTask;
            if (speed is { } value && !double.IsFinite(value)) speed = null;
            return owner.onFix?.Invoke(new(DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp), latitude, longitude, accuracy, speed)) ?? Task.CompletedTask;
        }

        [JSInvokable]
        public Task OnError(int code) => owner.onError?.Invoke(code switch
        {
            0 => LocationFailure.Unsupported,
            1 => LocationFailure.PermissionDenied,
            2 => LocationFailure.Unavailable,
            3 => LocationFailure.Timeout,
            _ => LocationFailure.Unknown
        }) ?? Task.CompletedTask;
    }
}
