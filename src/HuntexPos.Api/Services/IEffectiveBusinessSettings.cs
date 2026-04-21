namespace HuntexPos.Api.Services;

public interface IEffectiveBusinessSettings
{
    ValueTask<EffectiveBusinessSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Invalidate any in-memory cache after a settings write.</summary>
    void Invalidate();
}
