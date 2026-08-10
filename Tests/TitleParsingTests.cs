using Altechap.Models;
using Altechap.Services;

namespace Altechap.Tests;

/// <summary>
/// Analyse des titres de fenêtre Dofus. C'est l'entrée de toute la chaîne
/// d'identification : si le pseudo est mal extrait, tout le reste dérape.
/// </summary>
public class TitleParsingTests
{
    [Theory]
    // Client actuel
    [InlineData("Akame - Iop - 3.6.10.10 - Release", "Akame", 8)]
    [InlineData("Kurome - Eliotrope - 3.6.10.10 - Release", "Kurome", 16)]
    [InlineData("Ulquiorra - Cra - 3.6.10.10 - Release", "Ulquiorra", 9)]   // sans accent
    [InlineData("Ulquiorra - Crâ - 3.6.10.10 - Release", "Ulquiorra", 9)]   // avec accent
    [InlineData("Hinata - Forgelance - 3.6.10.10 - Release", "Hinata", 20)]
    // Ancien client
    [InlineData("Nanika - Eniripsa - Dofus 2.68", "Nanika", 7)]
    public void ParseTitle_extrait_pseudo_et_classe(string title, string name, int classId)
    {
        var (n, c) = WindowScanner.ParseTitle(title);
        Assert.Equal(name, n);
        Assert.Equal(classId, c);
    }

    [Theory]
    [InlineData("Dofus")]
    [InlineData("Dofus (Pandawa)")]
    public void ParseTitle_ecran_de_chargement_ne_donne_pas_de_pseudo(string title)
    {
        var (name, _) = WindowScanner.ParseTitle(title);
        Assert.Equal("Dofus", name);
        // Et surtout : ce titre ne doit jamais être considéré comme résolu,
        // sinon un personnage fantôme est créé à chaque lancement de Dofus.
        Assert.False(CharacterIdentity.IsResolved(Win(title)));
    }

    [Fact]
    public void ParseTitre_resolu_est_bien_detecte_comme_tel()
        => Assert.True(CharacterIdentity.IsResolved(Win("Akame - Iop - 3.6.10.10 - Release")));

    [Theory]
    [InlineData("Dofus", true)]
    [InlineData("Dofus 3.4", true)]
    [InlineData("Akame - Iop - 3.6.10.10 - Release", true)] // titre brut non édité
    [InlineData("", true)]
    [InlineData("Akame", false)]
    [InlineData("Kaguya", false)]
    public void IsAutoDiscoveredName(string name, bool expected)
        => Assert.Equal(expected, CharacterIdentity.IsAutoDiscoveredName(name));

    internal static DofusWindow Win(string title)
    {
        var (name, cls) = WindowScanner.ParseTitle(title);
        return new DofusWindow(1, title, name, cls, "Dofus.exe");
    }
}
