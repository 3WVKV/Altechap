using System.IO;
using System.Reflection;

namespace Altechap.Services;

/// <summary>
/// Version et date de build, affichées dans l'en-tête et écrites au démarrage
/// dans le journal. Sans ça, rien ne distingue visuellement deux builds — on
/// peut croire tester une correction alors qu'on lance l'ancien exécutable.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = ReadVersion();
    public static DateTime BuildDate { get; } = ReadBuildDate();

    /// <summary>Ex. « v2.0.0 · 10/08/2026 ».</summary>
    public static string Display => $"v{Version} · {BuildDate:dd/MM/yyyy}";

    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Le SDK suffixe parfois « +<sha> » : on ne garde que la version
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "?";
    }

    private static DateTime ReadBuildDate()
    {
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(path)) path = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return File.GetLastWriteTime(path);
        }
        catch { /* valeur d'affichage uniquement */ }
        return DateTime.MinValue;
    }
}
