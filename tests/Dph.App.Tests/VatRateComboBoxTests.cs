using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Dph.App.ViewModels;
using Dph.Core.Domain;


namespace Dph.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

[Collection("Avalonia")]
public class VatRateComboBoxTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
    public void Dispose() => _session.Dispose();

    [Theory]
    [InlineData(false, false, "21", 0)]
    [InlineData(false, true, "21", 0)]
    [InlineData(true, false, "12", 1)]
    [InlineData(true, true, "12", 1)]
    public Task Legacy_Rate_Can_Be_Changed_Through_ComboBox(bool issued, bool byIndex, string rate, int index) => _session.Dispatch(() =>
    {
        var row = CreateRow(issued, VatRateKind.Zero0);
        var combo = CreateCombo(row);
        var window = new Window { Content = combo };
        window.Show();
        try
        {
            Assert.Equal("0", combo.SelectedItem);
            var options = combo.ItemsSource;
            if (byIndex) combo.SelectedIndex = index;
            else combo.SelectedItem = rate;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rate, combo.SelectedItem);
            Assert.Equal(rate, GetRate(row));
            Assert.Same(options, combo.ItemsSource);
        }
        finally { window.Close(); }
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Reused_ComboBox_Selects_Rate_Of_New_DataContext(bool issued) => _session.Dispatch(() =>
    {
        var legacy = CreateRow(issued, VatRateKind.Zero0);
        var normal = CreateRow(issued, VatRateKind.Standard21);
        var combo = CreateCombo(legacy);
        var window = new Window { Content = combo };
        window.Show();
        try
        {
            combo.DataContext = normal;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("21", combo.SelectedItem);
            Assert.Equal("21", GetRate(normal));
            Assert.Equal(new[] { "21", "12" }, combo.Items.Cast<string>());
            combo.DataContext = legacy;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0", combo.SelectedItem);
            Assert.Equal("0", GetRate(legacy));
        }
        finally { window.Close(); }
    }, CancellationToken.None);

    private static object CreateRow(bool issued, VatRateKind rate) => issued
        ? IssuedInvoiceItemViewModel.FromDomain(new() { VatRate = rate })
        : InvoiceLineViewModel.FromDomain(new() { VatRate = rate });

    private static string GetRate(object row) => row is InvoiceLineViewModel line
        ? line.VatRate : ((IssuedInvoiceItemViewModel)row).VatRate;

    private static ComboBox CreateCombo(object row)
    {
        var combo = new ComboBox();
        combo.Bind(ItemsControl.ItemsSourceProperty, new Binding("VatRateOptions"));
        combo.Bind(ComboBox.SelectedItemProperty, new Binding("VatRate") { Mode = BindingMode.TwoWay });
        combo.DataContext = row;
        return combo;
    }
}
