namespace RoutePacer.App.Invocation;

public interface IInvocationVerifier { Task<bool> VerifyAsync(InvocationRequest request, byte[] canonicalBytes, string publicJwk, CancellationToken cancellationToken = default); }
