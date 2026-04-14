namespace HuntexPos.Api.Services;

public interface IEffectiveMailgunProvider
{
    ValueTask<EffectiveMailgunOptions> GetAsync(CancellationToken ct = default);
}
