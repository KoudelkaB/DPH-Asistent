using System.Globalization;
using Dph.Core.Domain;

namespace Dph.Core.Services;

public interface IExchangeRateProvider
{
    Task<ExchangeRate?> GetRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class CnbExchangeRateClient(HttpClient httpClient) : IExchangeRateProvider
{
    public async Task<ExchangeRate?> GetRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (currencyCode.Equals("CZK", StringComparison.OrdinalIgnoreCase))
        {
            return new ExchangeRate(date, "CZK", 1, 1m);
        }

        var urlDate = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var text = await httpClient.GetStringAsync(
            $"https://www.cnb.cz/cs/financni_trhy/devizovy_trh/kurzy_devizoveho_trhu/denni_kurz.txt?date={urlDate}",
            cancellationToken);

        return Parse(text, currencyCode);
    }

    public static ExchangeRate? Parse(string text, string currencyCode)
    {
        using var reader = new StringReader(text);
        var header = reader.ReadLine();
        if (header is null)
        {
            return null;
        }

        var headerDate = header.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        DateOnly.TryParseExact(headerDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

        _ = reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split('|');
            if (parts.Length < 5 || !parts[3].Equals(currencyCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                && amount > 0
                && decimal.TryParse(parts[4].Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            {
                return new ExchangeRate(date, parts[3], amount, rate);
            }
        }

        return null;
    }
}
