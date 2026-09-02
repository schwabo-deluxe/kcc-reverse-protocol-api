using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class ConfigTests
{
    static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kcc-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Bindet_appsettings_Struktur_inklusive_Filter()
    {
        var path = WriteTemp(
            """
            {
              "Url": "wss://anlage/ws",
              "BatchSize": 250,
              "Filter": { "MinDataLength": 4, "Directions": ["FromPlc", "ToPlc"] }
            }
            """);
        try
        {
            var config = KccConfig.Load(CommandLine.Parse(["record", "--config", path]));

            Assert.Equal("wss://anlage/ws", config.Url);
            Assert.Equal(250, config.BatchSize);
            Assert.Equal(4, config.Filter.MinDataLength);
            Assert.Equal(
                new[] { TelegramDirection.FromPlc, TelegramDirection.ToPlc },
                config.Filter.Directions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Kommandozeile_schlaegt_Datei()
    {
        var path = WriteTemp("""{ "Database": "aus-datei.db", "BatchSize": 100 }""");
        try
        {
            var config = KccConfig.Load(CommandLine.Parse(
                ["record", "--config", path, "--db", "override.db", "--batch-size", "999", "--insecure"]));

            Assert.Equal("override.db", config.Database);
            Assert.Equal(999, config.BatchSize);
            Assert.True(config.AllowUntrustedCertificate);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
