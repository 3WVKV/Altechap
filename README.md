# Altéchap

Multi-manager Dofus pour Windows : détecte les fenêtres du jeu ouvertes, les
organise par ordre d'initiative, permet de naviguer entre elles au clavier et de
réordonner leur position dans la barre des tâches.

<!-- Capture d'écran à ajouter ici : elle vaut mieux que trois paragraphes. -->

## Installation

1. Télécharger `Altechap-Setup-x.y.z.exe` depuis la
   [dernière release](https://github.com/3WVKV/Altechap/releases/latest).
2. Lancer le fichier.
   Windows affichera **« Windows a protégé votre ordinateur »** — c'est normal,
   l'installeur n'est pas signé numériquement (un certificat coûte plusieurs
   centaines d'euros par an). Cliquer sur **Informations complémentaires**, puis
   **Exécuter quand même**.
3. Suivre l'assistant. Aucune autorisation administrateur n'est demandée :
   l'application s'installe pour votre compte uniquement, dans
   `%LocalAppData%\Programs\Altechap`.

Rien d'autre à installer — le runtime .NET est embarqué dans l'installeur.

## Mises à jour

Altéchap vérifie au démarrage s'il existe une version plus récente, et propose
de l'installer en un clic (téléchargement, installation et redémarrage
automatiques). Vérification manuelle : clic sur le numéro de version dans
l'en-tête, ou clic droit sur l'icône de la zone de notification.

Pour désactiver la vérification, mettre `"CheckUpdates": false` dans
`%AppData%\Altechap\config.json`.

## Utilisation

- Lancez vos clients Dofus : Altéchap les détecte et crée une carte par personnage.
- Glissez les cartes pour définir l'ordre d'initiative.
- **Alt+←** / **Alt+→** naviguent entre les personnages (raccourcis modifiables
  via le bouton ⌨, avec possibilité d'un raccourci direct par personnage).
- Les **profils** mémorisent un ordre et un ensemble de personnages actifs :
  un profil par composition de combat.
- **⊞** réordonne les boutons de la barre des tâches selon l'ordre affiché.

Vos personnages, profils et raccourcis sont conservés dans
`%AppData%\Altechap\config.json` ; ils survivent aux mises à jour.

## Développement

```bash
dotnet build
```

```bash
dotnet test Tests/Altechap.Tests.csproj
```

Produire l'installeur (nécessite [Inno Setup 6](https://jrsoftware.org/isinfo.php)) :

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

- [`states.md`](states.md) — description détaillée de l'architecture et des
  décisions de conception.
- [`Installer/README.md`](Installer/README.md) — procédure de publication d'une
  nouvelle version.

---

Projet personnel, sans affiliation avec Ankama. Altéchap ne lit ni ne modifie la
mémoire du jeu : il n'utilise que les API de fenêtres de Windows.
