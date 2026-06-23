using System.Net.Http.Json;
using System.Text.Json;
using Dph.Core.Domain;

namespace Dph.Core.Services;

public interface IAresClient
{
    Task<AresSubject?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default);
    Task<AresSubject?> LookupByDicAsync(string dic, CancellationToken cancellationToken = default);
    Task<AresSubjectDetail?> LookupDetailByIcoAsync(string ico, CancellationToken cancellationToken = default);
}

public sealed class AresClient(HttpClient httpClient) : IAresClient
{
    private const string BaseUrl = "https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty";
    private const string CiselnikUrl = "https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ciselniky-nazevniky/vyhledat";

    // kód územního pracoviště (financniUrad z ARES) -> kód cílového FÚ (c_ufo). Načte se z ARES
    // jednou za běh aplikace.
    private Dictionary<string, string>? _taxOfficeByWorkplace;
    private readonly SemaphoreSlim _ciselnikLock = new(1, 1);

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

    public async Task<AresSubjectDetail?> LookupDetailByIcoAsync(string ico, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIco(ico);
        if (normalized.Length != 8)
        {
            return null;
        }

        using var response = await httpClient.GetAsync($"{BaseUrl}/{normalized}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var resolvedIco = String(root, "ico");
        var name = String(root, "obchodniJmeno");
        if (string.IsNullOrWhiteSpace(resolvedIco) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var sidlo = root.TryGetProperty("sidlo", out var sidloElement) ? sidloElement : default;
        var taxOfficeCode = await ResolveTaxOfficeCodeAsync(String(root, "financniUrad"), cancellationToken);

        return new AresSubjectDetail(
            resolvedIco,
            name,
            String(root, "dic"),
            Trim(String(sidlo, "nazevUlice")),
            BuildHouseNumber(String(sidlo, "cisloDomovni"), String(sidlo, "cisloOrientacni")),
            Trim(String(sidlo, "nazevObce")),
            Trim(String(sidlo, "psc")),
            taxOfficeCode);
    }

    // ARES vrací u subjektu kód územního pracoviště (financniUrad). Cílový FÚ (c_ufo) je jeho
    // nadřízený kraj (kodNadrizeny 451–464); Specializovaný FÚ (kód 013) má v EPO c_ufo = 13.
    private async Task<string?> ResolveTaxOfficeCodeAsync(string? workplaceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workplaceCode))
        {
            return null;
        }

        var map = await LoadTaxOfficeMapAsync(cancellationToken);
        if (map is null)
        {
            return null;
        }

        if (map.TryGetValue(workplaceCode, out var parent))
        {
            return parent;
        }

        // Specializovaný finanční úřad nemá nadřízený kraj a v EPO má vlastní kód.
        return workplaceCode is "013" or "13" ? "13" : null;
    }

    private async Task<Dictionary<string, string>?> LoadTaxOfficeMapAsync(CancellationToken cancellationToken)
    {
        if (_taxOfficeByWorkplace is not null)
        {
            return _taxOfficeByWorkplace;
        }

        await _ciselnikLock.WaitAsync(cancellationToken);
        try
        {
            if (_taxOfficeByWorkplace is not null)
            {
                return _taxOfficeByWorkplace;
            }

            using var content = JsonContent.Create(new { kodCiselniku = "FinancniUrad" });
            using var response = await httpClient.PostAsync(CiselnikUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (document.RootElement.TryGetProperty("ciselniky", out var ciselniky)
                && ciselniky.ValueKind == JsonValueKind.Array
                && ciselniky.GetArrayLength() > 0
                && ciselniky[0].TryGetProperty("polozkyCiselniku", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var kod = String(item, "kod");
                    var parent = String(item, "kodNadrizeny");
                    if (!string.IsNullOrEmpty(kod) && !string.IsNullOrEmpty(parent))
                    {
                        map[kod] = parent;
                    }
                }
            }

            _taxOfficeByWorkplace = map;
            return map;
        }
        finally
        {
            _ciselnikLock.Release();
        }
    }

    private static string? String(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)
            ? property.ValueKind == JsonValueKind.Number
                ? property.GetRawText()
                : property.GetString()
            : null;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? BuildHouseNumber(string? cisloDomovni, string? cisloOrientacni)
    {
        var domovni = Trim(cisloDomovni);
        var orientacni = Trim(cisloOrientacni);
        return domovni is null
            ? orientacni
            : orientacni is null ? domovni : $"{domovni}/{orientacni}";
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
