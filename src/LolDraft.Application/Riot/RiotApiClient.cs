using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LolDraft.Application.Riot;

public class RiotOptions
{
    /// <summary>Chave da Riot (RGAPI-...). Vazia = integração desligada.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Servidor do LoL (plataforma): br1, na1, euw1...</summary>
    public string Platform { get; set; } = "br1";

    /// <summary>Roteamento regional da account-v1: americas, europe, asia.</summary>
    public string Region { get; set; } = "americas";

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

public record RiotAccountDto(
    [property: JsonPropertyName("puuid")] string Puuid,
    [property: JsonPropertyName("gameName")] string? GameName,
    [property: JsonPropertyName("tagLine")] string? TagLine);

public record RiotSummonerDto(
    [property: JsonPropertyName("puuid")] string Puuid,
    [property: JsonPropertyName("profileIconId")] int ProfileIconId,
    [property: JsonPropertyName("summonerLevel")] long SummonerLevel);

public record RiotLeagueEntryDto(
    [property: JsonPropertyName("queueType")] string QueueType,
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("rank")] string Rank,
    [property: JsonPropertyName("leaguePoints")] int LeaguePoints,
    [property: JsonPropertyName("wins")] int Wins,
    [property: JsonPropertyName("losses")] int Losses);

/// <summary>Erro da Riot com mensagem pronta para o usuário.</summary>
public class RiotApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

public interface IRiotApi
{
    bool Enabled { get; }
    Task<RiotAccountDto?> GetAccountByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<RiotSummonerDto?> GetSummonerByPuuidAsync(string puuid, CancellationToken ct = default);
    Task<IReadOnlyList<RiotLeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default);
}

/// <summary>
/// Cliente mínimo da Riot API:
/// account-v1 (Riot ID → PUUID), summoner-v4 (ícone) e league-v4 (elo), todos por PUUID.
/// </summary>
public class RiotApiClient(HttpClient http, IOptions<RiotOptions> options) : IRiotApi
{
    private RiotOptions O => options.Value;
    public bool Enabled => O.Enabled;

    public Task<RiotAccountDto?> GetAccountByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default) =>
        GetAsync<RiotAccountDto>(
            $"https://{O.Region}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}", ct);

    public Task<RiotSummonerDto?> GetSummonerByPuuidAsync(string puuid, CancellationToken ct = default) =>
        GetAsync<RiotSummonerDto>($"https://{O.Platform}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}", ct);

    public async Task<IReadOnlyList<RiotLeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default) =>
        await GetAsync<List<RiotLeagueEntryDto>>($"https://{O.Platform}.api.riotgames.com/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}", ct)
        ?? [];

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        if (!Enabled) throw new RiotApiException("A integração com a Riot não está configurada (Riot:ApiKey).");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Riot-Token", O.ApiKey);
        using var res = await http.SendAsync(req, ct);

        switch (res.StatusCode)
        {
            case HttpStatusCode.OK:
                return await res.Content.ReadFromJsonAsync<T>(ct);
            case HttpStatusCode.NotFound:
                return default;
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new RiotApiException("A chave da Riot é inválida ou expirou (chaves de desenvolvimento duram 24h).", res.StatusCode);
            case HttpStatusCode.TooManyRequests:
                var wait = res.Headers.RetryAfter?.Delta?.TotalSeconds ?? 10;
                throw new RiotApiException($"Limite da Riot API atingido. Tente de novo em {Math.Ceiling(wait)}s.", res.StatusCode);
            default:
                throw new RiotApiException($"A Riot API respondeu {(int)res.StatusCode}. Tente de novo em instantes.", res.StatusCode);
        }
    }
}
