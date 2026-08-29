using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbSettingsRepository(IIndexedDbModule db) : ISettingsRepository
{
    public async Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<AutoPauseDto>("getAutoPause").ConfigureAwait(false);
        return dto is null ? AutoPauseSettings.Default : new AutoPauseSettings(dto.Enabled, dto.ThresholdSeconds).Clamped();
    }

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
        => db.InvokeVoidAsync("saveAutoPause", [settings.Clamped()]).AsTask();

    public sealed record AutoPauseDto(bool Enabled, int ThresholdSeconds);
}
