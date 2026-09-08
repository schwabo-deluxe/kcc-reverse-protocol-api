using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class RbgReportTests
{
    static readonly DateTime T0 = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Unspecified);
    static readonly TelegramFormat Fmt = TelegramFormat.Default;

    static int Off(string name)
    {
        var p = 0;
        foreach (var f in Fmt.Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
            p += f.Length;
        }
        throw new ArgumentException(name);
    }

    static string Data(string mc, string label, string source, string dest, string type = "DM")
    {
        var buf = new char[Fmt.Length];
        Array.Fill(buf, '.');
        void Put(string field, string v)
        {
            var a = Off(field);
            for (var i = 0; i < v.Length && a + i < buf.Length; i++) buf[a + i] = v[i];
        }
        Put("TelegramType", type);
        Put("MessageCode", mc);
        Put("ResourceLabel", label);
        Put("Source", source);
        Put("Destination", dest);
        return new string(buf);
    }

    static readonly RbgOptions Opts = RbgOptions.From(new KccConfig());

    // Bezugsleistung wie konfiguriert: 60 Doppelspiele/h (60 s), je 96 Ein-/Auslagerungen/h
    // (37,5 s). Das Verhältnis 62,5 % stammt aus dem Datenblatt, das Niveau aus der Messung.
    static readonly RbgCapacity Cap =
        new() { DoubleCyclesPerHour = 60, PutsPerHour = 96, GetsPerHour = 96 };

    sealed class Feed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        // Ereignis als DM/AK-Doppel wie von der Anlage (~0,1 s Abstand) — testet die Dedup.
        public void Add(double sec, string mc, string label, string conn = "RBG01")
        {
            var t = T0.AddSeconds(sec);
            Rows.Add(new(_id++, t, TelegramDirection.FromPlc, conn, Data(mc, label, "SRC", "DST"), null));
            Rows.Add(new(_id++, t.AddSeconds(0.1), TelegramDirection.FromPlc, conn, Data(mc, label, "SRC", "DST"), null));
        }
    }

    [Fact]
    public void Zaehlt_Spiele_und_misst_Auftragsdauer()
    {
        var feed = new Feed();
        // Abwechselnd Holen/Bringen, jeder Auftrag 25 s, Fenster 240 s.
        var steps = new (double t, string mc, string label)[]
        {
            (0, "PUPORD", "L1"), (25, "ENDPUP", "L1"),
            (25, "DEPORD", "L2"), (50, "ENDDEP", "L2"),
            (50, "PUPORD", "L3"), (75, "ENDPUP", "L3"),
            (75, "DEPORD", "L4"), (100, "ENDDEP", "L4"),
            (100, "PUPORD", "L5"), (125, "ENDPUP", "L5"),
            (125, "DEPORD", "L6"), (150, "ENDDEP", "L6"),
            (150, "PUPORD", "L7"), (175, "ENDPUP", "L7"),
            (175, "DEPORD", "L8"), (200, "ENDDEP", "L8"),
            (200, "PUPORD", "L9"), (225, "ENDPUP", "L9"),
        };
        foreach (var s in steps)
            feed.Add(s.t, s.mc, s.label);
        feed.Add(30, "ENDDEP", "X", conn: "RBG09");   // fremde Verbindung — muss ignoriert werden

        var r = RbgReport.Compute(feed.Rows, Fmt, "RBG01", Cap, T0, T0.AddSeconds(240), Opts);

        Assert.Equal(4, r.Puts);        // ENDDEP L2,L4,L6,L8
        Assert.Equal(5, r.Gets);     // ENDPUP L1,L3,L5,L7,L9
        Assert.Equal(4, r.DoubleCycles);
        Assert.Equal(1, r.SingleCycles);
        // Zeitbedarf: 4 Doppelspiele à 60 s + 1 Auslagerung à 37,5 s = 277,5 s im 240-s-Fenster.
        Assert.Equal(115.6, r.Percent);        // 277,5 / 240
        Assert.Equal(69.4, r.CyclesPerHour);   // 115,6 % von 60 Doppelspielen/h
        Assert.Equal(25, r.AvgPutSeconds);
        Assert.Equal(25, r.AvgGetSeconds);
        Assert.Equal(15, r.IdleSeconds); // 240 - (4*25 + 5*25)
        Assert.Equal(93.8, r.BusyPercent); // 225 / 240 * 100

        // Gleitender Verlauf: ein Stützpunkt je Schritt, letzter endet bei 'to', Spiele/h > 0.
        Assert.Equal(T0.AddSeconds(240), r.Series[^1].At);
        Assert.All(r.Series, b => Assert.True(b.At > T0 && b.At <= T0.AddSeconds(240)));
        Assert.Contains(r.Series, b => b.Uph > 0);
    }

    [Fact]
    public void Ohne_Ereignisse_alles_null()
    {
        var r = RbgReport.Compute([], Fmt, "RBG01", Cap, T0, T0.AddHours(1), Opts);

        Assert.Equal(0, r.Puts);
        Assert.Equal(0, r.Gets);
        Assert.Equal(0, r.Percent);
        Assert.Equal(0, r.BusyPercent);
        Assert.Equal(3600, r.IdleSeconds);
        Assert.Null(r.LatestAt);
    }

    /// <summary>
    /// Echte Telegramme aus dem Anlagenexport vom 08.09.2026. Belegt das Verhalten am
    /// Originalformat statt an nachgebauten Zeilen:
    /// <list type="bullet">
    ///   <item>Der <c>AK</c> einer RBG-Verbindung trägt denselben Inhalt wie das <c>DM</c>
    ///         (MessageCode, LE, Quelle, Ziel) — nur Typ, Sender und Empfänger drehen sich.</item>
    ///   <item><c>LM</c> (Lebensmeldung) hat gar keinen MessageCode.</item>
    /// </list>
    /// Beides darf die Fahrt nur <b>einmal</b> zählen.
    /// </summary>
    [Fact]
    public void Echtes_DM_AK_Paar_zaehlt_als_eine_Fahrt()
    {
        const string dm = "DM73SR03MFC10100ENDDEP0150SR03LU11..843002046250........SR03LU11.." +
                          "030291012.E01..........................................0000....END" +
                          "..................................";
        const string ak = "AK73MFC1SR030100ENDDEP0150SR03LU11..843002046250........SR03LU11.." +
                          "030291012.E01..........................................0000....END" +
                          "..................................";
        const string lm = "LM40SR05MFC10100......0150............................................." +
                          "..........................................END......................" +
                          "............";

        var at = T0.AddMinutes(5);
        var rows = new List<Telegram>
        {
            new(1, at, TelegramDirection.FromPlc, "RBG03", dm, null),
            new(2, at, TelegramDirection.ToPlc, "RBG03", ak, null),
            new(3, at, TelegramDirection.FromPlc, "RBG03", lm, null),
        };

        var r = RbgReport.Compute(rows, Fmt, "RBG03", Cap, T0, T0.AddHours(1), Opts);

        Assert.Equal(1, r.Puts);          // nicht 2 — der AK ist dieselbe Fahrt
        Assert.Equal(0, r.Gets);
        Assert.Equal(0, r.DoubleCycles);
        Assert.Equal(1, r.SingleCycles);

        // Auch ohne Typfilter bleibt es eine Fahrt: DM und AK stimmen in MessageCode, LE,
        // Quelle und Ziel überein, damit greift schon die Zusammenführung nach Inhalt.
        // Der Typfilter ist die verlässlichere Absicherung, nicht die einzige.
        var ohneFilter = RbgReport.Compute(
            rows, Fmt, "RBG03", Cap, T0, T0.AddHours(1), Opts with { CountTelegramType = "" });
        Assert.Equal(1, ohneFilter.Puts);
    }

    [Fact]
    public void Nur_Auslagerungen_ergibt_lauter_Einzelspiele()
    {
        var feed = new Feed();
        for (var i = 0; i < 6; i++)
            feed.Add(i * 30, "ENDPUP", $"L{i}");

        var r = RbgReport.Compute(feed.Rows, Fmt, "RBG01", Cap, T0, T0.AddSeconds(200), Opts);

        Assert.Equal(0, r.Puts);
        Assert.Equal(6, r.Gets);
        Assert.Equal(0, r.DoubleCycles);
        Assert.Equal(6, r.SingleCycles);
    }
}
