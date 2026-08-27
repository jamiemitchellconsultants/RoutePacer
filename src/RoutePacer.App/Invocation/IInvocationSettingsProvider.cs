namespace RoutePacer.App.Invocation;

public interface IInvocationSettingsProvider { Task<(bool Enabled, string? PublicKeyJwk)> GetAsync(CancellationToken cancellationToken = default); }
