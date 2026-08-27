using Bunit;
using FluentAssertions;
using Microsoft.JSInterop;
using RoutePacer.App.Browser;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Tests.Browser;

public sealed class LocationServiceTests : BunitContext
{
    private readonly BunitJSModuleInterop module;

    public LocationServiceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        module = JSInterop.SetupModule("./js/gps.js");
        module.SetupVoid("startTracking", _ => true).SetVoidResult();
        module.SetupVoid("stopTracking").SetVoidResult();
    }

    private LocationService Create() => new(JSInterop.JSRuntime);

    [Fact]
    public async Task Start_registers_exactly_one_watch()
    {
        var service = Create();

        await service.StartAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        await service.StartAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);

        module.Invocations["startTracking"].Should().HaveCount(1);
    }

    [Fact]
    public async Task Stopping_then_starting_registers_a_new_watch()
    {
        var service = Create();

        await service.StartAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        await service.StopAsync();
        await service.StartAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);

        module.Invocations["startTracking"].Should().HaveCount(2);
        module.Invocations["stopTracking"].Should().HaveCount(1);
    }

    [Fact]
    public async Task Stopping_without_starting_is_a_no_op()
    {
        await Create().StopAsync();

        module.Invocations["stopTracking"].Should().BeEmpty();
    }

    [Fact]
    public async Task Disposal_stops_the_watch_and_is_idempotent()
    {
        var service = Create();
        await service.StartAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);

        await service.DisposeAsync();
        await service.DisposeAsync();

        module.Invocations["stopTracking"].Should().HaveCount(1);
    }

    [Fact]
    public async Task Epoch_milliseconds_are_converted_to_a_utc_instant()
    {
        GeoFix? received = null;
        var service = Create();
        await service.StartAsync(fix => { received = fix; return Task.CompletedTask; }, _ => Task.CompletedTask);

        await Position(service, 1_787_832_000_000, 51.5, -0.12, 7, 4.5);

        received.Should().NotBeNull();
        received!.TimestampUtc.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_787_832_000_000));
        received.Latitude.Should().Be(51.5);
        received.AccuracyMeters.Should().Be(7);
        received.SpeedMps.Should().Be(4.5);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 5)]
    [InlineData(0, double.PositiveInfinity, 0, 5)]
    [InlineData(0, 0, double.NaN, 5)]
    [InlineData(0, 0, 0, double.NaN)]
    public async Task Non_finite_callback_values_are_dropped(double timestamp, double latitude, double longitude, double accuracy)
    {
        var received = 0;
        var service = Create();
        await service.StartAsync(_ => { received++; return Task.CompletedTask; }, _ => Task.CompletedTask);

        await Position(service, timestamp, latitude, longitude, accuracy, null);

        received.Should().Be(0);
    }

    [Fact]
    public async Task A_non_finite_speed_is_reported_as_absent()
    {
        GeoFix? received = null;
        var service = Create();
        await service.StartAsync(fix => { received = fix; return Task.CompletedTask; }, _ => Task.CompletedTask);

        await Position(service, 0, 0, 0, 5, double.NaN);

        received!.SpeedMps.Should().BeNull();
    }

    [Theory]
    [InlineData(0, LocationFailure.Unsupported)]
    [InlineData(1, LocationFailure.PermissionDenied)]
    [InlineData(2, LocationFailure.Unavailable)]
    [InlineData(3, LocationFailure.Timeout)]
    [InlineData(99, LocationFailure.Unknown)]
    public async Task Browser_error_codes_map_to_typed_failures(int code, LocationFailure expected)
    {
        LocationFailure? received = null;
        var service = Create();
        await service.StartAsync(_ => Task.CompletedTask, failure => { received = failure; return Task.CompletedTask; });

        await Error(service, code);

        received.Should().Be(expected);
    }

    // The JSInvokable callback object handed to gps.js is a private nested type, so it is driven by
    // reflection rather than by widening its accessibility for the tests.
    private static object Callbacks(LocationService service)
    {
        var field = typeof(LocationService).GetField("reference", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var reference = field.GetValue(service)!;
        return reference.GetType().GetProperty("Value")!.GetValue(reference)!;
    }

    private static Task Position(LocationService service, double timestamp, double latitude, double longitude, double accuracy, double? speed)
    {
        var callbacks = Callbacks(service);
        return (Task)callbacks.GetType().GetMethod("OnPosition")!.Invoke(callbacks, [timestamp, latitude, longitude, accuracy, speed])!;
    }

    private static Task Error(LocationService service, int code)
    {
        var callbacks = Callbacks(service);
        return (Task)callbacks.GetType().GetMethod("OnError")!.Invoke(callbacks, [code])!;
    }
}
