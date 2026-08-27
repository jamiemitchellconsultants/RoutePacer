namespace RoutePacer.App.Browser;

public enum WakeLockStatus { Unsupported, Acquired, Revoked, Failed, Released }
public interface IWakeLockService : IAsyncDisposable
{
    event Action<WakeLockStatus>? StatusChanged;
    Task AcquireAsync();
    Task ReleaseAsync();
}
