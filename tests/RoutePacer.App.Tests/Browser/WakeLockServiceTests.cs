using Bunit;
using FluentAssertions;
using RoutePacer.App.Browser;

namespace RoutePacer.App.Tests.Browser;

public sealed class WakeLockServiceTests : BunitContext
{
    private readonly BunitJSModuleInterop module;

    public WakeLockServiceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        module = JSInterop.SetupModule("./js/wakelock.js");
        module.SetupVoid("acquireWakeLock", _ => true).SetVoidResult();
        module.SetupVoid("releaseWakeLock").SetVoidResult();
    }

    private WakeLockService Create() => new(JSInterop.JSRuntime);

    [Fact]
    public async Task Acquire_imports_the_module_once_and_requests_the_lock()
    {
        var service = Create();

        await service.AcquireAsync();
        await service.AcquireAsync();

        module.Invocations["acquireWakeLock"].Should().HaveCount(2);
    }

    [Fact]
    public async Task Release_without_acquire_is_a_no_op()
    {
        await Create().ReleaseAsync();

        module.Invocations["releaseWakeLock"].Should().BeEmpty();
    }

    [Fact]
    public async Task Disposal_releases_the_lock()
    {
        var service = Create();
        await service.AcquireAsync();

        await service.DisposeAsync();

        module.Invocations["releaseWakeLock"].Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Acquired", WakeLockStatus.Acquired)]
    [InlineData("acquired", WakeLockStatus.Acquired)]
    [InlineData("Unsupported", WakeLockStatus.Unsupported)]
    [InlineData("Revoked", WakeLockStatus.Revoked)]
    [InlineData("Released", WakeLockStatus.Released)]
    [InlineData("Failed", WakeLockStatus.Failed)]
    [InlineData("something-else", WakeLockStatus.Failed)]
    public async Task Status_strings_are_translated_to_typed_events(string status, WakeLockStatus expected)
    {
        WakeLockStatus? received = null;
        var service = Create();
        service.StatusChanged += value => received = value;
        await service.AcquireAsync();

        Status(service, status);

        received.Should().Be(expected);
    }

    // The JSInvokable callback object is a private nested type, so it is driven by reflection.
    private static void Status(WakeLockService service, string status)
    {
        var field = typeof(WakeLockService).GetField("reference", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var reference = field.GetValue(service)!;
        var callbacks = reference.GetType().GetProperty("Value")!.GetValue(reference)!;
        callbacks.GetType().GetMethod("OnStatus")!.Invoke(callbacks, [status]);
    }
}
