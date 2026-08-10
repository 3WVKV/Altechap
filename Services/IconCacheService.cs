using System.IO;
using System.Net.Http;
using Altechap.Models;

namespace Altechap.Services;

/// <summary>
/// Cache local des icônes PNG de classe depuis dofusdb.fr.
/// %AppData%\Altechap\icons\symbol_X.png
/// Télécharge une seule fois, ne re-fetch jamais sauf si fichier manquant.
/// </summary>
public sealed class IconCacheService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Altechap", "icons");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<int> _inFlight = new();

    public void EnsureAll(IEnumerable<int> classIds, Action<int>? onDone = null)
    {
        try { Directory.CreateDirectory(CacheDir); }
        catch (Exception ex) { Log.Warn($"Création du cache d'icônes impossible ({CacheDir})", ex); }

        foreach (var id in classIds.Distinct().Where(i => i > 0))
        {
            if (ClassDefs.IconCached(id)) continue;

            // Les 19 icônes sont embarquées dans l'exécutable : on les extrait
            // au lieu d'appeler le réseau. Le téléchargement ne sert plus que de
            // secours pour un id inconnu de cette version.
            if (ExtractEmbedded(id)) { onDone?.Invoke(id); continue; }

            lock (_inFlight) { if (!_inFlight.Add(id)) continue; }
            _ = DownloadAsync(id, onDone);
        }
    }

    /// <summary>Copie l'icône embarquée vers le cache disque. Faux si absente.</summary>
    private static bool ExtractEmbedded(int id)
    {
        var asm  = typeof(IconCacheService).Assembly;
        var name = $"Altechap.Resources.icons.symbol_{id}.png";
        try
        {
            using var src = asm.GetManifestResourceStream(name);
            if (src == null) return false;
            using var dst = File.Create(ClassDefs.GetIconPath(id));
            src.CopyTo(dst);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Extraction de l'icône embarquée {id} échouée", ex);
            return false;
        }
    }

    private async System.Threading.Tasks.Task DownloadAsync(int id, Action<int>? onDone)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(ClassDefs.GetIconUrl(id));
            await File.WriteAllBytesAsync(ClassDefs.GetIconPath(id), bytes);
            onDone?.Invoke(id);
        }
        catch (Exception ex)
        {
            // Pas de réseau → fallback lettre initiale dans l'UI
            Log.Warn($"Téléchargement de l'icône de classe {id} échoué", ex);
        }
        finally { lock (_inFlight) _inFlight.Remove(id); }
    }
}
