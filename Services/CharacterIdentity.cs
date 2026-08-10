using System.Text.RegularExpressions;
using Altechap.Models;

namespace Altechap.Services;

/// <summary>
/// Logique d'identité des personnages — volontairement PURE (aucune dépendance
/// WPF, aucun état). C'est ici qu'est né le bug de perte des raccourcis :
/// tout ce qui décide « cette fenêtre appartient-elle à ce perso ? » vit ici
/// et est couvert par les tests (Tests/).
/// </summary>
public static class CharacterIdentity
{
    private static readonly Regex VersionRx = new(@"\d+\.\d+\.\d+", RegexOptions.Compiled);

    /// <summary>
    /// Vrai si le nom ressemble à un titre brut auto-détecté plutôt qu'à un pseudo :
    /// vide, « Dofus », « Dofus 3.4… », ou contenant un numéro de version.
    /// </summary>
    public static bool IsAutoDiscoveredName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (name.Equals("Dofus", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Dofus ", StringComparison.OrdinalIgnoreCase)) return true;
        return VersionRx.IsMatch(name);
    }

    /// <summary>
    /// Vrai quand le titre de la fenêtre expose un vrai pseudo — c'est-à-dire que
    /// le client a dépassé l'écran de chargement. Tant que c'est faux, on ne crée
    /// aucun personnage : sinon un fantôme naît à chaque lancement de Dofus.
    /// </summary>
    public static bool IsResolved(DofusWindow w)
        => !string.IsNullOrWhiteSpace(w.CharName) && !IsAutoDiscoveredName(w.CharName);

    /// <summary>
    /// Cette fenêtre correspond-elle à ce personnage ?
    /// Motif explicite s'il y en a un (regex si demandée), sinon égalité stricte
    /// du pseudo — un « contains » ferait capter la fenêtre de « Kaguya2 » par
    /// « Kaguya », et n'importe quelle fenêtre par un perso nommé « Dofus ».
    /// </summary>
    public static bool NameMatches(Character ch, DofusWindow w)
    {
        if (!string.IsNullOrWhiteSpace(ch.MatchPattern))
        {
            if (ch.UseRegex)
            {
                try
                {
                    var rx = new Regex(ch.MatchPattern, RegexOptions.IgnoreCase);
                    return rx.IsMatch(w.CharName) || rx.IsMatch(w.RawTitle);
                }
                catch { return false; } // motif invalide saisi par l'utilisateur
            }
            return w.CharName.Contains(ch.MatchPattern, StringComparison.OrdinalIgnoreCase)
                || w.RawTitle.Contains(ch.MatchPattern, StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(ch.Name) || IsAutoDiscoveredName(ch.Name)) return false;
        return string.Equals(ch.Name.Trim(), w.CharName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Migration : fusionne les personnages en double (même pseudo) accumulés par
    /// les anciennes versions, puis re-cible les ordres de profil, les listes de
    /// désactivation et les raccourcis directs sur le personnage conservé.
    /// Retourne le nombre de doublons absorbés.
    /// </summary>
    public static int MergeDuplicates(AppData d)
    {
        var byName = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
        var remap  = new Dictionary<Guid, Guid>();
        var kept   = new List<Character>();

        // Un perso porteur d'un raccourci est prioritaire pour être celui qu'on garde.
        var ordered = d.Characters
            .OrderByDescending(c => d.Hotkeys.Direct.ContainsKey(c.Id))
            .ThenByDescending(c => !string.IsNullOrWhiteSpace(c.MatchPattern))
            .ToList();

        foreach (var ch in ordered)
        {
            var key = (ch.Name ?? string.Empty).Trim();
            if (key.Length == 0 || IsAutoDiscoveredName(key)) { kept.Add(ch); continue; }

            if (byName.TryGetValue(key, out var survivor))
            {
                remap[ch.Id] = survivor.Id;
                // Récupérer la config la plus riche des doublons
                if (survivor.ClassId == 0 && ch.ClassId > 0) survivor.ClassId = ch.ClassId;
                if (string.IsNullOrWhiteSpace(survivor.MatchPattern) &&
                    !string.IsNullOrWhiteSpace(ch.MatchPattern))
                {
                    survivor.MatchPattern = ch.MatchPattern;
                    survivor.UseRegex     = ch.UseRegex;
                }
            }
            else { byName[key] = ch; kept.Add(ch); }
        }

        if (remap.Count == 0) return 0;

        // Conserver l'ordre d'affichage d'origine parmi les survivants
        var keptSet = new HashSet<Character>(kept);
        d.Characters = d.Characters.Where(keptSet.Contains).ToList();

        foreach (var p in d.Profiles)
        {
            p.Order = Remap(p.Order, remap);
            if (p.Disabled != null) p.Disabled = Remap(p.Disabled, remap);
        }

        var direct = new Dictionary<Guid, string>();
        foreach (var kv in d.Hotkeys.Direct)
        {
            var target = remap.TryGetValue(kv.Key, out var n) ? n : kv.Key;
            if (!direct.ContainsKey(target)) direct[target] = kv.Value;
        }
        d.Hotkeys.Direct = direct;

        return remap.Count;
    }

    private static List<Guid> Remap(List<Guid> ids, Dictionary<Guid, Guid> remap)
    {
        var seen = new HashSet<Guid>();
        return ids.Select(id => remap.TryGetValue(id, out var n) ? n : id)
                  .Where(seen.Add)
                  .ToList();
    }
}
