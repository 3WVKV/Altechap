using Altechap.Models;
using Altechap.Services;

namespace Altechap.Tests;

/// <summary>
/// Choix de la cible des macros « suivant » / « précédent ».
///
/// Le cas qui motive ces tests : l'utilisateur clique à la souris une fenêtre
/// désactivée dans le profil actif. Elle n'est pas navigable, mais sa position
/// dans l'ordre d'initiative doit malgré tout servir de point de départ — sinon
/// la macro repart du premier personnage, ce qui n'a aucun sens depuis la
/// quatrième fenêtre.
/// </summary>
public class NavigationTests
{
    /// <summary>Roster de <paramref name="count"/> persos nommés « 1 », « 2 », … tous liés et actifs.</summary>
    private static List<Character> Roster(int count)
    {
        var list = new List<Character>();
        for (int i = 1; i <= count; i++)
            list.Add(new Character { Name = i.ToString(), Enabled = true, Handle = i });
        return list;
    }

    private static Character ByName(List<Character> all, string name) => all.First(c => c.Name == name);

    // ── Cas ordinaire ────────────────────────────────────────────────────

    [Fact]
    public void Suivant_avance_d_un_cran()
    {
        var all = Roster(8);
        var target = Navigation.Neighbour(all, ByName(all, "6").Id, +1);
        Assert.Equal("7", target!.Name);
    }

    [Fact]
    public void Precedent_recule_d_un_cran()
    {
        var all = Roster(8);
        var target = Navigation.Neighbour(all, ByName(all, "6").Id, -1);
        Assert.Equal("5", target!.Name);
    }

    [Fact]
    public void La_navigation_boucle_aux_deux_extremites()
    {
        var all = Roster(8);
        Assert.Equal("1", Navigation.Neighbour(all, ByName(all, "8").Id, +1)!.Name);
        Assert.Equal("8", Navigation.Neighbour(all, ByName(all, "1").Id, -1)!.Name);
    }

    // ── Le courant n'est pas navigable ───────────────────────────────────

    [Fact]
    public void Depuis_une_fenetre_desactivee_on_prend_ses_voisins_reels()
    {
        // Le 4 est désactivé : on est dessus après un clic souris.
        var all = Roster(8);
        ByName(all, "4").Enabled = false;
        var id = ByName(all, "4").Id;

        Assert.Equal("5", Navigation.Neighbour(all, id, +1)!.Name);
        Assert.Equal("3", Navigation.Neighbour(all, id, -1)!.Name);
    }

    [Fact]
    public void Plusieurs_desactives_consecutifs_sont_enjambes()
    {
        var all = Roster(8);
        foreach (var n in new[] { "4", "5", "6" }) ByName(all, n).Enabled = false;
        var id = ByName(all, "5").Id;

        Assert.Equal("7", Navigation.Neighbour(all, id, +1)!.Name);
        Assert.Equal("3", Navigation.Neighbour(all, id, -1)!.Name);
    }

    [Fact]
    public void Le_balayage_depuis_un_desactive_boucle_aussi()
    {
        var all = Roster(8);
        foreach (var n in new[] { "7", "8" }) ByName(all, n).Enabled = false;
        var id = ByName(all, "8").Id;

        // Après le 8 désactivé, le premier navigable est le 1 (bouclage).
        Assert.Equal("1", Navigation.Neighbour(all, id, +1)!.Name);
        Assert.Equal("6", Navigation.Neighbour(all, id, -1)!.Name);
    }

    [Fact]
    public void Une_fenetre_fermee_sert_aussi_de_point_de_depart()
    {
        // Fenêtre fermée : Handle nul, donc non liée, mais toujours dans l'ordre.
        var all = Roster(8);
        ByName(all, "4").Handle = 0;
        var id = ByName(all, "4").Id;

        Assert.Equal("5", Navigation.Neighbour(all, id, +1)!.Name);
        Assert.Equal("3", Navigation.Neighbour(all, id, -1)!.Name);
    }

    // ── Situations dégénérées ────────────────────────────────────────────

    [Fact]
    public void Sans_personnage_navigable_il_n_y_a_nulle_part_ou_aller()
    {
        var all = Roster(3);
        foreach (var c in all) c.Enabled = false;
        Assert.Null(Navigation.Neighbour(all, all[0].Id, +1));
        Assert.Null(Navigation.Neighbour(all, Guid.NewGuid(), -1));
    }

    [Fact]
    public void Un_courant_inconnu_repart_des_extremites()
    {
        var all = Roster(4);
        Assert.Equal("1", Navigation.Neighbour(all, Guid.NewGuid(), +1)!.Name);
        Assert.Equal("4", Navigation.Neighbour(all, Guid.NewGuid(), -1)!.Name);
    }

    [Fact]
    public void Un_seul_navigable_renvoie_toujours_sur_lui_meme()
    {
        var all = Roster(5);
        foreach (var c in all.Where(c => c.Name != "3")) c.Enabled = false;
        var id = ByName(all, "3").Id;

        Assert.Equal("3", Navigation.Neighbour(all, id, +1)!.Name);
        Assert.Equal("3", Navigation.Neighbour(all, id, -1)!.Name);
    }

    [Fact]
    public void Aucune_fenetre_ouverte_la_navigation_reste_possible_entre_actifs()
    {
        // Avant le lancement des clients : personne n'est lié, mais les macros
        // doivent continuer de parcourir les personnages actifs.
        var all = Roster(4);
        foreach (var c in all) c.Handle = 0;

        Assert.Equal("3", Navigation.Neighbour(all, ByName(all, "2").Id, +1)!.Name);
        Assert.Equal("1", Navigation.Neighbour(all, ByName(all, "2").Id, -1)!.Name);
    }

    [Fact]
    public void Un_pas_nul_est_refuse()
        => Assert.Throws<ArgumentOutOfRangeException>(
               () => Navigation.Neighbour(Roster(3), Guid.NewGuid(), 0));
}
