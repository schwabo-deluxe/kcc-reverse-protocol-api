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
        new() { DoubleCyclesPerHour = 30, StoresPerHour = 48, RetrievalsPerHour = 48 };

    const string Rack = "010361211";   // Regalplatz: rein numerisch
    const string Infeed = "MA41";      // Uebergabestationen tragen Buchstaben
    const string Outfeed = "MA62";
    const string Crane = "SR01LU11";

    sealed class Feed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        /// <summary>Ereignis als DM/AK-Doppel wie von der Anlage - pruefet nebenbei die Dedup.</summary>
        public void Add(double sec, string mc, string label, string src, string dst, string conn = "RBG01")
        {
            var t = T0.AddSeconds(sec);
            Rows.Add(new(_id++, t, TelegramDirection.FromPlc, conn, Data(mc, label, src, dst), null));
            Rows.Add(new(_id++, t.AddSeconds(0.1), TelegramDirection.FromPlc, conn,
                Data(mc, label, src, dst, "AK"), null));
        }

        /// <summary>
        /// Ein vollstaendiger Transport wie in der Anlage: aufnehmen (PUPORD/ENDPUP), dann abgeben
        /// (DEPORD/ENDDEP). Die Richtung steckt in Quelle und Ziel - eine Einlagerung endet auf
        /// einem Regalplatz, eine Auslagerung an einer Station.
        /// </summary>
        public void Transport(double startSec, double endSec, string label, bool store,
            string conn = "RBG01")
        {
            var pickFrom = store ? Infeed : Rack;
            var dropTo = store ? Rack : Outfeed;
            Add(startSec, "PUPORD", label, pickFrom, Crane, conn);
            Add(endSec - 5, "ENDPUP", label, pickFrom, Crane, conn);
            Add(endSec - 4, "DEPORD", label, Crane, dropTo, conn);
            Add(endSec, "ENDDEP", label, Crane, dropTo, conn);
        }
    }

    [Fact]
    public void Zaehlt_Transporte_nach_Richtung_und_misst_ihre_Dauer()
    {
        var feed = new Feed();
        // Neun Transporte a 25 s, abwechselnd aus- und einlagern; Fenster 240 s.
        for (var i = 0; i < 9; i++)
            feed.Transport(i * 25, i * 25 + 25, $"L{i}", store: i % 2 == 1);
        feed.Transport(30, 55, "X", store: true, conn: "RBG09");   // fremde Verbindung

        var r = RbgReport.Compute(feed.Rows, Fmt, "RBG01", Cap, T0, T0.AddSeconds(240), Opts);

        Assert.Equal(4, r.Stores);        // i = 1,3,5,7
        Assert.Equal(5, r.Retrievals);    // i = 0,2,4,6,8
        Assert.Equal(9, r.Transports);
        Assert.Equal(4, r.DoubleCycles);  // vier Paare, je zwei Transporte
        Assert.Equal(1, r.SingleCycles);

        // Zeitbedarf laut Auslegung: 4 Doppelspiele a 120 s + 1 Einzelspiel a 75 s = 555 s
        // in einem 240-s-Fenster.
        Assert.Equal(231.2, r.Percent);
        Assert.Equal(69.4, r.CyclesPerHour);   // 231,2 % von 30 Doppelspielen/h

        Assert.Equal(25, r.AvgStoreSeconds);
        Assert.Equal(25, r.AvgRetrieveSeconds);
        Assert.Equal(15, r.IdleSeconds);       // 240 - 9*25
        Assert.Equal(93.8, r.BusyPercent);     // 225 / 240

        Assert.Equal(T0.AddSeconds(240), r.Series[^1].At);
        Assert.All(r.Series, b => Assert.True(b.At > T0 && b.At <= T0.AddSeconds(240)));
        Assert.Contains(r.Series, b => b.Uph > 0);
    }

    [Fact]
    public void Ohne_Ereignisse_alles_null()
    {
        var r = RbgReport.Compute([], Fmt, "RBG01", Cap, T0, T0.AddHours(1), Opts);

        Assert.Equal(0, r.Stores);
        Assert.Equal(0, r.Retrievals);
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

        Assert.Equal(1, r.Stores);          // nicht 2 — der AK ist dieselbe Fahrt
        Assert.Equal(0, r.Retrievals);
        Assert.Equal(0, r.DoubleCycles);
        Assert.Equal(1, r.SingleCycles);

        // Auch ohne Typfilter bleibt es eine Fahrt: DM und AK stimmen in MessageCode, LE,
        // Quelle und Ziel überein, damit greift schon die Zusammenführung nach Inhalt.
        // Der Typfilter ist die verlässlichere Absicherung, nicht die einzige.
        var ohneFilter = RbgReport.Compute(
            rows, Fmt, "RBG03", Cap, T0, T0.AddHours(1), Opts with { CountTelegramType = "" });
        Assert.Equal(1, ohneFilter.Stores);
    }

    /// <summary>
    /// Echte Telegramme des Anlagenexports vom 08.09.2026, RBG01, zwei vollstaendige Transporte:
    /// erst eine Auslagerung (PUPORD vom Regalplatz 010260512 auf das RBG, DEPORD weiter an die
    /// Station MA62), dann eine Einlagerung (PUPORD von MA41, DEPORD auf den Regalplatz
    /// 010361211). Zusammen sind das ein Doppelspiel - nicht zwei.
    /// </summary>
    [Fact]
    public void Echte_Transportfolge_ergibt_ein_Doppelspiel()
    {
        const string pad = "..................................................END" +
                           "..................................";
        const string padEnd = "..........................................0000....END" +
                              "..................................";

        var rows = new List<Telegram>();
        long id = 1;
        void Add(string time, string data) =>
            rows.Add(new(id++, DateTime.Parse("2026-09-08T" + time), TelegramDirection.FromPlc,
                "RBG01", data, null));

        // Auslagerung: aus dem Regal an die Station.
        Add("12:12:21", "DM16MFC1SR010100PUPORD0150SR01LU11..844097553692........010260512.SR01LU11..E01" + pad);
        Add("12:12:21", "AK16SR01MFC10100PUPORD0150SR01LU11..844097553692........010260512.SR01LU11..E01" + pad);
        Add("12:13:00", "DM93SR01MFC10100ENDPUP0150SR01LU11..844097553692........010260512.SR01LU11..E01" + padEnd);
        Add("12:13:01", "DM18MFC1SR010100DEPORD0150SR01LU11..844097553692........SR01LU11..MA62......E01" + pad);
        Add("12:13:30", "DM94SR01MFC10100ENDDEP0150SR01LU11..844097553692........SR01LU11..MA62......E01" + padEnd);
        Add("12:13:30", "AK94MFC1SR010100ENDDEP0150SR01LU11..844097553692........SR01LU11..MA62......E01" + padEnd);

        // Einlagerung: von der Station ins Regal.
        Add("12:13:31", "DM19MFC1SR010100PUPORD0150SR01LU11..843002084412........MA41......SR01LU11..E01" + pad);
        Add("12:13:42", "DM95SR01MFC10100ENDPUP0150SR01LU11..843002084412........MA41......SR01LU11..E01" + padEnd);
        Add("12:13:42", "DM20MFC1SR010100DEPORD0150SR01LU11..843002084412........SR01LU11..010361211.E01" + pad);
        Add("12:14:19", "DM96SR01MFC10100ENDDEP0150SR01LU11..843002084412........SR01LU11..010361211.E01" + padEnd);

        var from = DateTime.Parse("2026-09-08T12:12:00");
        var r = RbgReport.Compute(rows, Fmt, "RBG01", Cap, from, from.AddHours(1), Opts);

        Assert.Equal(1, r.Stores);        // Ziel 010361211 = Regalplatz
        Assert.Equal(1, r.Retrievals);    // Ziel MA62 = Station
        Assert.Equal(2, r.Transports);
        Assert.Equal(1, r.DoubleCycles);  // ein Doppelspiel aus zwei Transporten
        Assert.Equal(0, r.SingleCycles);

        // Transportdauern: 12:12:21->12:13:30 = 69 s und 12:13:31->12:14:19 = 48 s.
        Assert.Equal(48, r.AvgStoreSeconds);
        Assert.Equal(69, r.AvgRetrieveSeconds);

        // Fahrzeit zusammen 117 s (69 + 48) - das Doppelspiel laut Auslegung 120 s.
        Assert.Equal(3600 - 117, r.IdleSeconds, 1);
        Assert.Equal(3.3, r.Percent);   // ein Doppelspiel a 120 s von 3600 s
    }

    [Fact]
    public void Nur_Auslagerungen_ergibt_lauter_Einzelspiele()
    {
        var feed = new Feed();
        for (var i = 0; i < 6; i++)
            feed.Transport(i * 30, i * 30 + 20, $"L{i}", store: false);

        var r = RbgReport.Compute(feed.Rows, Fmt, "RBG01", Cap, T0, T0.AddSeconds(200), Opts);

        Assert.Equal(0, r.Stores);
        Assert.Equal(6, r.Retrievals);
        Assert.Equal(0, r.DoubleCycles);
        Assert.Equal(6, r.SingleCycles);
        Assert.Equal(225, r.Percent);   // 6 Einzelspiele a 75 s = 450 s in 200 s
    }
}
