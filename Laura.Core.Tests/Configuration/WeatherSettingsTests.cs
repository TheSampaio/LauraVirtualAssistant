using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>Verifies persisted, bounded weather location settings.</summary>
public sealed class WeatherSettingsTests
{
    /// <summary>A configured city round-trips through the existing INI settings store.</summary>
    [Fact]
    public async Task WeatherLocationRoundTripsThroughIni()
    {
        string path = Path.Combine(Path.GetTempPath(), $"laura-weather-{Guid.NewGuid():N}.ini");
        try
        {
            var store = new IniSettingsStore(path, NullLogger<IniSettingsStore>.Instance);
            await store.SaveAsync(LauraSettings.Default with { WeatherLocation = "São Paulo" });
            LauraSettings loaded = await store.LoadAsync();
            Assert.Equal("São Paulo", loaded.WeatherLocation);
        }
        finally { if (File.Exists(path)) { File.Delete(path); } }
    }

    /// <summary>Weather input remains a short single-line place name.</summary>
    [Fact]
    public void WeatherLocationIsSanitized()
    {
        LauraSettings sanitized = (LauraSettings.Default with { WeatherLocation = "  São Paulo\r\nBrazil  " }).Sanitized();
        Assert.Equal("São Paulo Brazil", sanitized.WeatherLocation);
        Assert.Equal(100, (LauraSettings.Default with { WeatherLocation = new string('x', 120) }).Sanitized().WeatherLocation.Length);
    }
}
