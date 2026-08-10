using Altechap.Services;

namespace Altechap.Tests;

/// <summary>
/// Le vérificateur décide de proposer une mise à jour en comparant le tag de la
/// release GitHub à la version compilée. Une erreur d'interprétation ici
/// signifie soit une mise à jour jamais proposée, soit une boucle de
/// réinstallation de la version déjà installée.
/// </summary>
public class UpdateVersionTests
{
    [Theory]
    [InlineData("v2.1.0",     2, 1, 0)]
    [InlineData("2.1.0",      2, 1, 0)]
    [InlineData("V2.1.0",     2, 1, 0)]
    [InlineData("  v2.1.0  ", 2, 1, 0)]
    [InlineData("2.1",        2, 1, 0)] // Version.Parse laisserait Build à -1
    [InlineData("v2.1.0-rc1", 2, 1, 0)] // suffixe de pré-release ignoré
    [InlineData("2.1.0+abc",  2, 1, 0)] // métadonnée de build ignorée
    [InlineData("v10.0.3",   10, 0, 3)]
    public void ParseVersion_lit_les_formes_de_tag_usuelles(string tag, int maj, int min, int build)
        => Assert.Equal(new Version(maj, min, build), UpdateService.ParseVersion(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-final")]
    public void ParseVersion_refuse_ce_qui_n_est_pas_une_version(string? tag)
        => Assert.Null(UpdateService.ParseVersion(tag));

    [Fact]
    public void Une_version_superieure_est_bien_detectee_comme_plus_recente()
    {
        var current = UpdateService.ParseVersion("2.0.0")!;
        Assert.True(UpdateService.ParseVersion("2.0.1")! > current);
        Assert.True(UpdateService.ParseVersion("2.1.0")! > current);
        Assert.True(UpdateService.ParseVersion("v3.0.0")! > current);
    }

    [Fact]
    public void Une_version_identique_ou_anterieure_ne_declenche_rien()
    {
        var current = UpdateService.ParseVersion("2.0.0")!;
        Assert.False(UpdateService.ParseVersion("2.0.0")! > current);
        Assert.False(UpdateService.ParseVersion("v2.0")!  > current);
        Assert.False(UpdateService.ParseVersion("1.9.9")! > current);
    }

    [Fact]
    public void La_version_compilee_est_exploitable()
    {
        // Si BuildInfo renvoie quelque chose d'illisible, toute comparaison
        // se ferait contre 0.0.0 → mise à jour proposée en boucle.
        Assert.NotNull(UpdateService.ParseVersion(BuildInfo.Version));
        Assert.True(UpdateService.Current > new Version(0, 0, 0));
    }
}
