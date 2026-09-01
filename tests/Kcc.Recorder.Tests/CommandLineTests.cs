using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class CommandLineTests
{
    [Fact]
    public void Liest_Kommando_Optionen_und_Flags()
    {
        var cli = CommandLine.Parse(["record", "--take", "50", "--verbose", "--url=wss://host/ws"]);

        Assert.Equal("record", cli.Command);
        Assert.Equal(50, cli.GetInt("take"));
        Assert.True(cli.HasFlag("verbose"));
        Assert.Equal("wss://host/ws", cli.GetString("url"));
    }

    [Fact]
    public void Ohne_Kommando_bleibt_das_Kommando_leer()
    {
        var cli = CommandLine.Parse(["--help"]);

        Assert.Equal("", cli.Command);
        Assert.True(cli.HasFlag("help"));
    }

    [Fact]
    public void Ein_Flag_vor_einem_weiteren_Flag_schluckt_keinen_Wert()
    {
        var cli = CommandLine.Parse(["record", "--insecure", "--db", "x.db"]);

        Assert.True(cli.HasFlag("insecure"));
        Assert.Null(cli.GetString("insecure"));
        Assert.Equal("x.db", cli.GetString("db"));
    }
}
