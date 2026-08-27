namespace RoutePacer.App.Invocation;

/// <summary>
/// A safe, rider-facing invocation failure. <see cref="Retryable"/> is true only for failures proven to
/// precede payload dispatch: once the GET is sent the relay has consumed the row, so a retry can only fail.
/// </summary>
public sealed class InvocationFailedException(string message, bool retryable, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool Retryable { get; } = retryable;
}
