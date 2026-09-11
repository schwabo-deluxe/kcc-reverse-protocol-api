using System.Reflection;

namespace Kcc.Recorder;

/// <summary>
/// Versionskennung des Builds. Der Release-Workflow baut mit <c>-p:Version=X.Y.Z</c> aus dem
/// Git-Tag; lokale Builds ohne diese Vorgabe melden <c>dev</c>.
/// </summary>
public static class BuildInfo
{
    /// <summary>Kurzform ohne Build-Metadaten, z. B. <c>0.2.8</c>, sonst <c>dev</c>.</summary>
    public static string Version { get; } = Resolve();

    /// <summary>
    /// Datum der Build-Datei (Schreibzeit der DLL) — überlebt ein Kopieren der EXE, da Windows
    /// dabei die „Geändert am"-Zeit erhält. <c>null</c>, wenn der Pfad nicht ermittelbar ist.
    /// </summary>
    public static DateTime? BuildDate { get; } = ResolveBuildDate();

    static string Resolve()
    {
        var asm = typeof(BuildInfo).Assembly;

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            var v = plus >= 0 ? info[..plus] : info;
            if (v.Length > 0 && v != "1.0.0")
                return v;
        }

        var ver = asm.GetName().Version;
        return ver is null || ver is { Major: 1, Minor: 0, Build: 0 }
            ? "dev"
            : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    static DateTime? ResolveBuildDate()
    {
        // Bei PublishSingleFile hat Assembly.Location keinen Pfad (in die EXE gebündelt, meldet
        // IL3000) — Environment.ProcessPath zeigt zuverlässig auf die kcc.exe-Datei auf der Platte.
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }
    }
}
