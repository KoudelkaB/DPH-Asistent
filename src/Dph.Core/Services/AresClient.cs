using System.Net.Http.Json;
using System.Text.Json;
using Dph.Core.Domain;

namespace Dph.Core.Services;

public interface IAresClient
{
    Task<AresSubject?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default);
    Task<AresSubject?> LookupByDicAsync(string dic, CancellationToken cancellationToken = default);
}

public sealed class AresClient(HttpClient httpClient) : IAresClient
{
    public async Task<AresSubject?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIco(ico);
        if (normalized.Length != 8)
        {
            return null;
        }

        using var response = await httpClient.GetAsync(
            $"https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{normalized}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(document.RootElement);
    }

    public Task<AresSubject?> LookupByDicAsync(string dic, CancellationToken cancellationToken = default)
    {
        var ico = TryGetIcoFromDic(dic);
        return ico is null
            ? Task.FromResult<AresSubject?>(null)
            : LookupByIcoAsync(ico, cancellationToken);
    }

    public static AresSubject? Parse(JsonElement root)
    {
        var ico = root.TryGetProperty("ico", out var icoProperty) ? icoProperty.GetString() : null;
        var name = root.TryGetProperty("obchodniJmeno", out var nameProperty) ? nameProperty.GetString() : null;
        if (string.IsNullOrWhiteSpace(ico) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var dic = root.TryGetProperty("dic", out var dicProperty) ? dicProperty.GetString() : null;
        DateOnly? updatedOn = null;
        if (root.TryGetProperty("datumAktualizace", out var updatedProperty)
            && DateOnly.TryParse(updatedProperty.GetString(), out var parsed))
        {
            updatedOn = parsed;
        }

        return new AresSubject(ico, name, dic, updatedOn);
    }

    public static string NormalizeIco(string ico) => new(ico.Where(char.IsDigit).ToArray());

    public static string NormalizeDic(string dic)
    {
        var trimmed = dic.Trim().Replace(" ", "", StringComparison.Ordinal);
        return trimmed.StartsWith("CZ", StringComparison.OrdinalIgnoreCase)
            ? "CZ" + new string(trimmed[2..].Where(char.IsDigit).ToArray())
            : new string(trimmed.Where(char.IsDigit).ToArray());
    }

    public static string? TryGetIcoFromDic(string? dic)
    {
        if (string.IsNullOrWhiteSpace(dic))
        {
            return null;
        }

        var normalized = NormalizeDic(dic);
        if (normalized.StartsWith("CZ", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[2..];
            return suffix.Length == 8 ? suffix : null;
        }

        return normalized.Length == 8 ? normalized : null;
    }
}
