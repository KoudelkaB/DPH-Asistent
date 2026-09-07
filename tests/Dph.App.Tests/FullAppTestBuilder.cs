using Avalonia;
using Avalonia.Headless;

namespace Dph.App.Tests;

public static class FullAppTestBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Dph.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// Avalonia má globální stav; samostatné headless sessions se nesmí překrývat.
[CollectionDefinition("Avalonia", DisableParallelization = true)]
public sealed class AvaloniaCollection { }
