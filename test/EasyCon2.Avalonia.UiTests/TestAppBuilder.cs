using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(EasyCon2.Avalonia.UiTests.TestAppBuilder))]

namespace EasyCon2.Avalonia.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
}