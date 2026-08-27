namespace RoutePacer.Core.Import;

public sealed class RouteImportException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
