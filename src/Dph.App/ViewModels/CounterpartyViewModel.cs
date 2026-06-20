using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Domain;

namespace Dph.App.ViewModels;

public partial class CounterpartyViewModel : ViewModelBase
{
    public string[] RoleOptions { get; } =
    [
        CounterpartyRole.Customer.ToString(),
        CounterpartyRole.Supplier.ToString(),
        CounterpartyRole.Both.ToString()
    ];

    [ObservableProperty] private long id;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string ico = "";
    [ObservableProperty] private string dic = "";
    [ObservableProperty] private string countryCode = "CZ";
    [ObservableProperty] private string role = CounterpartyRole.Supplier.ToString();

    public string DisplayName => Name.NullIfWhiteSpace() ?? Dic.NullIfWhiteSpace() ?? Ico.NullIfWhiteSpace() ?? "(bez názvu)";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnIcoChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnDicChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public static CounterpartyViewModel FromDomain(Counterparty counterparty) => new()
    {
        Id = counterparty.Id,
        Name = counterparty.Name,
        Ico = counterparty.Ico ?? "",
        Dic = counterparty.Dic ?? "",
        CountryCode = counterparty.CountryCode,
        Role = counterparty.Role.ToString()
    };

    public Counterparty ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Ico = Ico.NullIfWhiteSpace(),
        Dic = Dic.NullIfWhiteSpace(),
        CountryCode = CountryCode.NullIfWhiteSpace() ?? "CZ",
        Role = Enum.TryParse<CounterpartyRole>(Role, out var parsed) ? parsed : CounterpartyRole.Supplier
    };
}

internal static class StringUiExtensions
{
    public static string? NullIfWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
