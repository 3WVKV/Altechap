using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Altechap.Services;

/// <summary>Une release GitHub plus récente que la build en cours.</summary>
public sealed record UpdateInfo(
    Version Version,
    string  Tag,
    string  Notes,
    string  DownloadUrl,
    string  AssetName,
    long    Size,
    string  PageUrl);

/// <summary>
/// Vérificateur de mise à jour : interroge la dernière release publiée sur
/// GitHub, compare à la version compilée, télécharge le setup et le lance en
/// silencieux.
///
/// Pourquoi GitHub plutôt qu'un serveur maison : rien à héberger, l'historique
/// des versions est conservé, et l'API publique suffit (60 requêtes/h/IP sans
/// jeton — largement au-dessus d'une vérification par démarrage).
/// </summary>
public static class UpdateService
{
    // ⚠️ À adapter si le dépôt est renommé/déplacé : c'est la seule source de vérité.
    public const string Owner = "3WVKV";
    public const string Repo  = "Altechap";

    private const string ApiLatest = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public  const string PageUrl   = $"https://github.com/{Owner}/{Repo}/releases/latest";

    /// <summary>Version de l'exécutable en cours, normalisée (ex. 2.0.0).</summary>
    public static Version Current { get; } = ParseVersion(BuildInfo.Version) ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Timeout court : la vérification est accessoire, elle ne doit jamais
        // faire traîner le démarrage ni bloquer un clic utilisateur.
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // L'API GitHub rejette (403) toute requête sans User-Agent.
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Altechap", BuildInfo.Version));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    // ── Vérification ──────────────────────────────────────────────────────

    /// <summary>
    /// Retourne la release si elle est plus récente que la build en cours,
    /// sinon <c>null</c>. Ne lève jamais : une panne réseau ou un dépôt
    /// injoignable ne doit pas remonter jusqu'à l'utilisateur au démarrage.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(ApiLatest, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // 404 = aucune release publiée (dépôt neuf) : cas normal, pas une erreur.
                Log.Info($"Vérification de mise à jour : réponse {(int)resp.StatusCode} de GitHub.");
                return null;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc    = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var ver = ParseVersion(tag);
            if (ver == null)
            {
                Log.Warn($"Release GitHub ignorée : tag « {tag} » non interprétable en version.");
                return null;
            }

            if (ver <= Current)
            {
                Log.Info($"Vérification de mise à jour : à jour (locale {Current}, distante {ver}).");
                return null;
            }

            var asset = PickInstaller(root);
            if (asset == null)
            {
                Log.Warn($"Release {tag} disponible mais aucun installeur (.exe) attaché.");
                return null;
            }

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var page  = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? PageUrl : PageUrl;

            Log.Info($"Mise à jour disponible : {Current} → {ver} ({asset.Value.name}, {asset.Value.size / 1024 / 1024.0:0.0} Mo).");
            return new UpdateInfo(ver, tag, notes.Trim(), asset.Value.url, asset.Value.name, asset.Value.size, page);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Warn("Vérification de mise à jour impossible", ex);
            return null;
        }
    }

    /// <summary>
    /// Choisit l'installeur parmi les fichiers attachés à la release : un .exe
    /// nommé « setup » en priorité, sinon le premier .exe venu.
    /// </summary>
    private static (string url, string name, long size)? PickInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        (string url, string name, long size)? fallback = null;

        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url  = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0L;

            if (url.Length == 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("install", StringComparison.OrdinalIgnoreCase))
                return (url, name, size);

            fallback ??= (url, name, size);
        }
        return fallback;
    }

    /// <summary>« v2.1.0 », « 2.1 », « 2.1.0-rc1 » → <see cref="Version"/>, sinon null.</summary>
    public static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        // Coupe tout suffixe non numérique (-rc1, +sha, etc.)
        int end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        s = s[..end].TrimEnd('.');

        if (!Version.TryParse(s, out var v)) return null;

        // Normalise : Version.Parse laisse Build/Revision à -1 pour « 2.1 ».
        return new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }

    // ── Téléchargement ────────────────────────────────────────────────────

    /// <summary>
    /// Télécharge l'installeur dans %Temp%\Altechap et retourne son chemin.
    /// <paramref name="progress"/> reçoit une fraction 0→1 (ou -1 si la taille
    /// est inconnue, pour afficher une barre indéterminée).
    /// </summary>
    public static async Task<string> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Altechap");
        Directory.CreateDirectory(dir);

        // Nom fixe par version : un second essai réécrit le fichier au lieu
        // d'empiler des installeurs dans %Temp%.
        var target = Path.Combine(dir, SanitizeFileName(info.AssetName));
        var tmp    = target + ".part";

        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                                    .ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? (info.Size > 0 ? info.Size : -1);
            using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long read  = 0;
            int  n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                progress?.Report(total > 0 ? (double)read / total : -1);
            }
        }

        if (File.Exists(target)) File.Delete(target);
        File.Move(tmp, target);

        Log.Info($"Installeur téléchargé : {target}");
        return target;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Altechap-Setup.exe" : name;
    }

    // ── Installation ──────────────────────────────────────────────────────

    /// <summary>
    /// Lance l'installeur en silencieux et demande le redémarrage d'Altéchap
    /// une fois la copie terminée. L'appelant doit fermer l'application juste
    /// après : l'installeur ne peut pas remplacer un exécutable en cours.
    /// </summary>
    public static void LaunchInstaller(string setupPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = setupPath,
            // /AUTOLAUNCH=1 est lu par le script .iss pour relancer l'app en fin d'install.
            Arguments       = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOLAUNCH=1",
            UseShellExecute = true,
        };
        Process.Start(psi);
        Log.Info("Installeur lancé en silencieux — arrêt d'Altéchap pour libérer les fichiers.");
    }

    /// <summary>Ouvre la page de la release dans le navigateur (repli manuel).</summary>
    public static void OpenPage(string? url = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url ?? PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Ouverture de la page de release impossible", ex); }
    }
}
