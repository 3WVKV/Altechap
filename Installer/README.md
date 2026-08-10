# Distribution d'Altéchap

Comment produire l'installeur, publier une mise à jour, et ce que voient les
utilisateurs.

---

## 1. Prérequis (poste de développement uniquement)

```bash
winget install -e --id JRSoftware.InnoSetup
```

```bash
winget install -e --id GitHub.cli
```

Puis, une seule fois : `gh auth login`.

Le dépôt GitHub doit s'appeler **`3WVKV/Altechap`** — c'est ce qui est codé en
dur dans `Services/UpdateService.cs` (constantes `Owner` / `Repo`). S'il porte
un autre nom, corrigez ces deux constantes avant le premier build.

---

## 2. Produire l'installeur

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Résultat : `dist\Altechap-Setup-<version>.exe` (~48 Mo).
C'est le fichier unique à envoyer aux amis — il ne demande **rien** d'autre
(le runtime .NET 8 est embarqué dans l'installeur).

## 3. Publier une mise à jour

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 2.1.0 -Release -Notes "Correction du tri par initiative."
```

Le script enchaîne : mise à jour de la version dans le `.csproj` → compilation →
installeur → création de la release GitHub `v2.1.0` avec l'installeur attaché.

Sans `-Notes`, GitHub génère automatiquement la liste des commits.
Sans `-Release`, l'installeur est produit mais rien n'est publié.

⚠️ **La version doit toujours augmenter.** Le vérificateur compare
`tag de la release` (ex. `v2.1.0`) à la version compilée : une release taguée
avec une version inférieure ou égale est ignorée.

---

## 4. Ce que vit l'utilisateur

**Installation** — double-clic sur `Altechap-Setup-x.y.z.exe`. Pas d'UAC :
l'app s'installe dans `%LocalAppData%\Programs\Altechap`, pour l'utilisateur
courant. L'assistant propose un raccourci bureau et le lancement au démarrage
de Windows. Un désinstalleur apparaît dans « Applications installées ».

**Mise à jour** — 4 secondes après le démarrage, Altéchap interroge la dernière
release GitHub. Si une version supérieure existe, un dialogue affiche les notes
de version et trois choix :

| Bouton | Effet |
|---|---|
| **Installer et redémarrer** | Télécharge l'installeur, le lance en silencieux, ferme et relance Altéchap |
| **Plus tard** | Ne fait rien ; la proposition revient au prochain démarrage |
| **Ignorer cette version** | Mémorise le refus (`SkippedUpdate` dans `config.json`) ; seule une version *ultérieure* sera reproposée |

**Vérification manuelle** — clic sur le numéro de version dans l'en-tête, ou
clic droit sur l'icône de la zone de notification → « ⟳ Vérifier les mises à
jour ». Ce mode affiche aussi le message « vous êtes à jour », et ignore un
éventuel refus antérieur.

**Désactiver la vérification** — passer `"CheckUpdates": false` dans
`%AppData%\Altechap\config.json`.

Les personnages, profils et raccourcis vivent dans `%AppData%\Altechap\` :
ils survivent aux mises à jour, et à la désinstallation sauf réponse contraire.

---

## 5. Signalement SmartScreen

L'installeur n'est pas signé numériquement. Windows affichera donc, les
premières fois, un écran bleu « Windows a protégé votre ordinateur » →
*Informations complémentaires* → *Exécuter quand même*. C'est normal et sans
gravité, mais **prévenez vos amis** : sans explication, la plupart abandonnent
à cet écran.

Faire disparaître l'avertissement demande un certificat de signature de code
(~100–400 €/an), ce qui ne se justifie que pour une diffusion large.

---

## 6. Fichiers concernés

| Fichier | Rôle |
|---|---|
| `build.ps1` | Compilation → installeur → release GitHub |
| `Installer/Altechap.iss` | Script Inno Setup (raccourcis, désinstalleur, relance auto) |
| `Services/UpdateService.cs` | Interrogation de l'API GitHub, téléchargement, lancement du setup |
| `Views/UpdateDialog.xaml{,.cs}` | Dialogue de proposition + barre de progression |
| `dist/` | Sortie de build — ne pas versionner |

`AppId` dans le `.iss` (`{8F3C2A17-…}`) identifie le produit pour Windows :
le modifier ferait cohabiter deux installations au lieu d'en remplacer une.
