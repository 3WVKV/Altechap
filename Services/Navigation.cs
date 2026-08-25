using Altechap.Models;

namespace Altechap.Services;

/// <summary>
/// Choix du personnage vers lequel sauter. Logique pure (aucun Win32, aucune
/// dépendance WPF) : c'est ce qui la rend testable, contrairement au reste de
/// la navigation qui manipule des fenêtres réelles.
/// </summary>
public static class Navigation
{
    /// <summary>
    /// Personnages atteignables par les macros : actifs dans le profil courant
    /// et dont la fenêtre est ouverte. Repli sur les seuls « actifs » quand
    /// aucune fenêtre n'est liée, pour que la navigation reste utilisable
    /// avant le lancement des clients.
    /// </summary>
    public static List<Character> Navigable(IReadOnlyList<Character> all)
    {
        var linked = all.Where(c => c.Enabled && c.IsLinked).ToList();
        return linked.Count > 0 ? linked : all.Where(c => c.Enabled).ToList();
    }

    /// <summary>
    /// Voisin du personnage courant dans l'ordre d'initiative :
    /// <paramref name="step"/> = +1 pour le suivant, -1 pour le précédent.
    ///
    /// Le personnage courant n'est pas forcément navigable lui-même — c'est le
    /// cas si l'utilisateur a cliqué à la souris une fenêtre désactivée dans le
    /// profil actif. On raisonne alors sur sa position dans l'ordre complet et
    /// on balaie dans le sens demandé jusqu'au premier personnage navigable.
    /// Repartir du début de liste, comme avant, n'avait aucun sens : depuis la
    /// fenêtre 4 désactivée, « suivant » renvoyait à la fenêtre 1.
    ///
    /// Retourne null s'il n'y a nulle part où aller.
    /// </summary>
    public static Character? Neighbour(IReadOnlyList<Character> all, Guid currentId, int step)
    {
        if (step == 0) throw new ArgumentOutOfRangeException(nameof(step), "step doit valoir +1 ou -1.");

        var navigable = Navigable(all);
        if (navigable.Count == 0) return null;

        // Cas ordinaire : le courant fait partie des navigables, on décale d'un cran.
        int i = navigable.FindIndex(c => c.Id == currentId);
        if (i >= 0) return navigable[Mod(i + step, navigable.Count)];

        // Courant absent des navigables (désactivé, fenêtre fermée) ou inconnu.
        var current = all.FirstOrDefault(c => c.Id == currentId);
        if (current == null) return navigable[step > 0 ? 0 : ^1];

        // Balayage depuis sa place réelle dans l'ordre d'initiative.
        int pos = IndexOf(all, current);
        for (int k = 1; k <= all.Count; k++)
        {
            var candidate = all[Mod(pos + step * k, all.Count)];
            if (navigable.Contains(candidate)) return candidate;
        }

        return navigable[0];
    }

    /// <summary>Modulo toujours positif — l'opérateur % de C# rend -1 pour (-1 % n).</summary>
    private static int Mod(int value, int count) => ((value % count) + count) % count;

    private static int IndexOf(IReadOnlyList<Character> all, Character target)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], target)) return i;
        return 0;
    }
}
