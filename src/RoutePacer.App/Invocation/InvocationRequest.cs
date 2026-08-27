namespace RoutePacer.App.Invocation;

public sealed record InvocationRequest(Uri PayloadUri, string Name, long IssuedUnixMilliseconds, string Signature);
