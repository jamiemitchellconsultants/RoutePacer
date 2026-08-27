namespace RoutePacer.Persistence.Handoffs;

public interface IHandoffStore
{
    Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
