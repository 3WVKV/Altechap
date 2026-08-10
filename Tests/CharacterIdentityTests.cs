using Altechap.Models;
using Altechap.Services;

namespace Altechap.Tests;

/// <summary>
/// Association fenêtre ↔ personnage et migration des doublons.
/// Ces cas reproduisent exactement le bug qui faisait perdre les raccourcis
/// directs d'une session à l'autre.
/// </summary>
public class CharacterIdentityTests
{
    private static Character Char(string name, string pattern = "", bool regex = false)
        => new() { Name = name, MatchPattern = pattern, UseRegex = regex };

    // ── Association par pseudo ────────────────────────────────────────────
    [Fact]
    public void Pseudo_identique_associe()
        => Assert.True(CharacterIdentity.NameMatches(
            Char("Akame"), TitleParsingTests.Win("Akame - Iop - 3.6.10.10 - Release")));

    [Fact]
    public void Pseudo_different_n_associe_pas()
        => Assert.False(CharacterIdentity.NameMatches(
            Char("Akame"), TitleParsingTests.Win("Kurome - Eliotrope - 3.6.10.10 - Release")));

    [Fact]
    public void Prefixe_commun_n_associe_pas()
    {
        // Le « contains » d'origine faisait capter la fenêtre de Kaguya2 par Kaguya
        var w = TitleParsingTests.Win("Kaguya2 - Pandawa - 3.6.10.10 - Release");
        Assert.False(CharacterIdentity.NameMatches(Char("Kaguya"), w));
        Assert.True(CharacterIdentity.NameMatches(Char("Kaguya2"), w));
    }

    [Fact]
    public void Perso_nomme_Dofus_ne_capte_plus_toutes_les_fenetres()
        => Assert.False(CharacterIdentity.NameMatches(
            Char("Dofus"), TitleParsingTests.Win("Akame - Iop - 3.6.10.10 - Release")));

    [Fact]
    public void Motif_libre_est_pris_en_compte()
        => Assert.True(CharacterIdentity.NameMatches(
            Char("PeuImporte", pattern: "Akame"),
            TitleParsingTests.Win("Akame - Iop - 3.6.10.10 - Release")));

    [Fact]
    public void Motif_regex_est_honore()
        => Assert.True(CharacterIdentity.NameMatches(
            Char("X", pattern: "^Aka(me|to)$", regex: true),
            TitleParsingTests.Win("Akame - Iop - 3.6.10.10 - Release")));

    [Fact]
    public void Regex_invalide_ne_leve_pas()
        => Assert.False(CharacterIdentity.NameMatches(
            Char("X", pattern: "[non-fermé", regex: true),
            TitleParsingTests.Win("Akame - Iop - 3.6.10.10 - Release")));

    // ── Migration des doublons ────────────────────────────────────────────
    [Fact]
    public void Doublons_fusionnes_et_raccourci_preserve()
        {
        var vieux   = Char("Akame");   // celui qui porte le raccourci
        var doublon = Char("Akame");   // recréé par une ancienne session
        var autre   = Char("Kurome");

        var profil = new CombatProfile
        {
            Name     = "Classique",
            Order    = [vieux.Id, autre.Id, doublon.Id],
            Disabled = [doublon.Id],
        };

        var data = new AppData
        {
            Characters = [vieux, doublon, autre],
            Profiles   = [profil],
            Hotkeys    = new HotkeyConfig { Direct = { [vieux.Id] = "Home" } },
        };

        int merged = CharacterIdentity.MergeDuplicates(data);

        Assert.Equal(1, merged);
        Assert.Equal(2, data.Characters.Count);
        Assert.Contains(data.Characters, c => c.Id == vieux.Id);
        Assert.DoesNotContain(data.Characters, c => c.Id == doublon.Id);

        // Le raccourci pointe toujours sur un personnage existant
        Assert.Equal("Home", data.Hotkeys.Direct[vieux.Id]);
        Assert.All(data.Hotkeys.Direct.Keys, id => Assert.Contains(data.Characters, c => c.Id == id));

        // Ordre et désactivations re-ciblés, sans doublon d'identifiant
        Assert.Equal([vieux.Id, autre.Id], profil.Order);
        Assert.Equal([vieux.Id], profil.Disabled);
    }

    [Fact]
    public void Sans_doublon_rien_ne_bouge()
    {
        var a = Char("Akame");
        var b = Char("Kurome");
        var data = new AppData { Characters = [a, b], Profiles = [new CombatProfile { Order = [a.Id, b.Id] }] };

        Assert.Equal(0, CharacterIdentity.MergeDuplicates(data));
        Assert.Equal(2, data.Characters.Count);
    }

    [Fact]
    public void Le_porteur_du_raccourci_survit_meme_s_il_est_en_second()
    {
        var premier = Char("Akame");
        var porteur = Char("Akame");
        var data = new AppData
        {
            Characters = [premier, porteur],
            Profiles   = [new CombatProfile { Order = [premier.Id, porteur.Id] }],
            Hotkeys    = new HotkeyConfig { Direct = { [porteur.Id] = "Home" } },
        };

        CharacterIdentity.MergeDuplicates(data);

        var survivant = Assert.Single(data.Characters);
        Assert.Equal(porteur.Id, survivant.Id);
        Assert.Equal("Home", data.Hotkeys.Direct[porteur.Id]);
    }

    [Fact]
    public void Les_noms_auto_decouverts_ne_sont_jamais_fusionnes_entre_eux()
    {
        // Deux fenêtres encore sur l'écran de chargement ne sont pas « le même perso »
        var a = Char("Dofus");
        var b = Char("Dofus");
        var data = new AppData { Characters = [a, b], Profiles = [new CombatProfile()] };

        Assert.Equal(0, CharacterIdentity.MergeDuplicates(data));
        Assert.Equal(2, data.Characters.Count);
    }
}
