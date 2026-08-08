using ViewerPrn.Application.Settings;
using ViewerPrn.Infrastructure.Settings;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task MissingFileYieldsDefaults()
    {
        using TempDirectory temp = new();
        JsonSettingsStore store = new(temp.Combine("settings.json"));

        AppSettings settings = await store.LoadAsync();

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Null(settings.AccentColorArgb);
    }

    [Fact]
    public async Task SettingsSurviveARoundTrip()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("settings.json");

        await new JsonSettingsStore(path).SaveAsync(
            new AppSettings { Theme = ThemePreference.Dark, AccentColorArgb = 0xFF0078D4 });

        AppSettings reloaded = await new JsonSettingsStore(path).LoadAsync();

        Assert.Equal(ThemePreference.Dark, reloaded.Theme);
        Assert.Equal(0xFF0078D4u, reloaded.AccentColorArgb);
        Assert.Equal(AppSettings.CurrentVersion, reloaded.Version);
    }

    [Fact]
    public async Task OverwritingKeepsTheOldVersionAsABackup()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("settings.json");
        JsonSettingsStore store = new(path);

        await store.SaveAsync(new AppSettings { Theme = ThemePreference.Light });
        await store.SaveAsync(new AppSettings { Theme = ThemePreference.Dark });

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("Light", await File.ReadAllTextAsync(path + ".bak"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptFileFallsBackToDefaultsAndIsLeftInPlace()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("settings.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        AppSettings settings = await new JsonSettingsStore(path).LoadAsync();

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveCreatesTheDirectory()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "nested", "deeper", "settings.json");

        await new JsonSettingsStore(path).SaveAsync(AppSettings.Default);

        Assert.True(File.Exists(path));
    }
}
