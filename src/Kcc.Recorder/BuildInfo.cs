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
}
