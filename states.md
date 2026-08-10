# Altéchap — État de l'application (Windows)

> Document de référence de l'état **actuel** du code (v2.0.1, 10/08/2026).
> Décrit ce que fait l'app aujourd'hui ; l'historique des corrections est en §10.

---

## 1. Vue d'ensemble

Altéchap est un multi-manager pour Dofus : détecte les fenêtres du jeu ouvertes,
permet de les organiser par ordre d'initiative, de naviguer entre elles au clavier,
et de réordonner leur position dans la barre des tâches Windows.

**Stack** : WPF (.NET 8, `net8.0-windows`) · MVVM via `CommunityToolkit.Mvvm 8.3.2`
· Icône système via `Hardcodet.NotifyIcon.Wpf 1.1.0` · Persistance JSON via
`System.Text.Json` · Tests xUnit.

**Fenêtre principale** : 500×700px par défaut (min 460×480), position et taille
mémorisées, épinglage optionnel (`Topmost` lié au bouton 📌), fond beige `#D9D2C3`,
`AllowsTransparency=False` (opacité forcée via Win32 dans `OnSourceInitialized`).

**Instance unique** : un second lancement réveille la fenêtre existante et se termine.

**Distribution** : installeur Inno Setup par utilisateur (sans UAC) + vérificateur
de mise à jour adossé aux releases GitHub — voir §11. Pas de signature de code :
SmartScreen avertit au premier lancement.

---

## 2. Modèle de données (`Models/Models.cs`)

| Type | Rôle | Persisté ? |
|---|---|---|
| `Character` | Un perso : `Id`, `Name`, `ClassId`, `Enabled`, `MatchPattern`, `UseRegex` | Oui |
| `Character.Handle` | HWND Win32 de la fenêtre liée | Non (`[JsonIgnore]`) |
| `Character.RawTitle` | Titre brut de la fenêtre | Non |
| `Character.IsDragging` | Carte en cours de déplacement (retour visuel) | Non |
| `Character.IsLinked` | `Handle != 0` | Calculé |
| `ClassDefs` | Table statique des 19 classes (id, nom, couleur) + chemins d'icônes | Statique |
| `CombatProfile` | `Name`, `Order: List<Guid>`, `Disabled: List<Guid>?` | Oui |
| `HotkeyConfig` | `Next`, `Prev`, `NextProfile`, `ToggleWindow`, `Direct: Dict<Guid,string>` | Oui |
| `WindowPlacement` | `Left`, `Top`, `Width`, `Height` de la fenêtre principale | Oui |
| `AppData` | Racine : `Characters`, `Profiles`, `ActiveProfile`, `Hotkeys`, `AlwaysOnTop`, `RefreshSeconds`, `Window` | Oui |
| `DofusWindow` | Snapshot runtime d'un scan : `Handle, RawTitle, CharName, ClassId, ProcessExe` | Non |

`CombatProfile.Disabled` à `null` = profil antérieur à l'activation par profil ; il
est initialisé au chargement depuis l'état global des personnages. Liste vide =
profil configuré, personne de désactivé.

**Identité** : les raccourcis directs et les ordres de profil référencent les
personnages par `Guid`. La stabilité de ce `Guid` d'une session à l'autre est donc
critique — c'est l'objet de `CharacterIdentity` (§4) et des tests (§9).

---

## 3. Détection des fenêtres (`Services/WindowScanner.cs`)

- `EnumWindows` (Win32) sur toutes les fenêtres visibles.
- Filtre : classe fenêtre `UnityWndClass` **OU** process commençant par `dofus`.
- Titre parsé par 3 regex en cascade :
  1. **Client actuel** : `Pseudo - Classe - X.Y.Z…` (version numérique après la classe)
  2. **Ancien client** : `Pseudo - Classe - Dofus X.Y`
  3. **Écran de chargement** : `Dofus` ou `Dofus (Classe)`
  4. Fallback : titre brut tronqué à 60 caractères, classe devinée par mots-clés
- `ClassDefs.IdFromTitle()` : matching par alias (`cra`/`crâ` → id 9, etc.)
- `Focus(Character)` : `SetForegroundWindow` + `SW_RESTORE` si minimisée.

> **Limite connue** : `UnityWndClass` est la classe de *tous* les jeux Unity. Un autre
> jeu Unity ouvert peut être pris pour un client Dofus (le parsing retombe alors sur
> le titre brut). Non corrigé à ce jour.

---

## 4. Cycle de vie (`ViewModels/MainViewModel.cs`, 896 lignes)

### Démarrage (`Init`)
1. Charge `AppData` (`StorageService`, repli sur `.bak` si illisible).
2. **Fusionne les doublons hérités** (`CharacterIdentity.MergeDuplicates`) et re-cible
   ordres de profil, désactivations et raccourcis directs sur les survivants.
3. Restaure `Characters`, `Profiles`, profil actif.
4. Attache `HotkeyService` (5 événements : next, prev, profil suivant, focus direct,
   afficher/masquer) en lui passant la **collection vivante** de personnages.
5. **Purge les orphelins** : persos auto-découverts, non liés, sans motif — sauf s'ils
   portent un raccourci direct.
6. Initialise `Disabled` des profils antérieurs, applique ordre **et** activations.
7. Sauvegarde (rend la migration définitive), extrait les icônes manquantes.
8. Démarre le watcher (période = `RefreshSeconds`, bornée 1-60s).
9. `ApplyWindowPlacement` restaure la géométrie, recadrée sur l'écran virtuel.

### Identité des personnages (`Services/CharacterIdentity.cs`) — pure, testée
- `IsResolved(w)` : la fenêtre a-t-elle dépassé l'écran de chargement ? Tant que non,
  **aucun personnage n'est créé** (sinon un fantôme naît à chaque lancement de Dofus).
- `NameMatches(ch, w)` : motif libre ou regex s'il y en a un, sinon **égalité stricte**
  du pseudo (un `contains` faisait capter `Kaguya2` par `Kaguya`).
- `IsAutoDiscoveredName(name)` : le nom ressemble-t-il à un titre brut ?
- `MergeDuplicates(data)` : migration, retourne le nombre de doublons absorbés.

### Scan / fusion (`MergeWindows`)
- Invalide les handles morts ; supprime le perso s'il est purement auto-découvert,
  sinon conserve sa config en remettant `Handle` à zéro.
- Fenêtre déjà connue dont le titre vient de se résoudre → **ré-identification** : si
  un perso persisté porte ce pseudo, la fenêtre lui est rendue et la carte provisoire
  est libérée. C'est ce qui préserve les `Guid`, donc les raccourcis.
- Fenêtre inconnue et résolue → nouveau `Character`, ajouté à tous les profils.
- Fenêtre non résolue → ignorée (comptée dans le statut « N en chargement… »).
- `RebuildLinkedCharacters()` reconstruit sans rebuild destructif (pas de flicker)
  les deux vues : `LinkedCharacters` (fenêtre ouverte) et `OfflineCharacters`.

### Navigation
`NavigateNext` / `NavigatePrev` parcourent les persos `Enabled && IsLinked`
(repli sur `Enabled` seul si aucun n'est lié), changent `Current` et focalisent.

### Activation (`SetEnabled`)
- OFF → minimise la fenêtre **sans voler le focus** (`MinimizeNoFocus`).
- ON → restaure en arrière-plan (`ShowWithoutFocus`).
- L'état est écrit dans `ActiveProfile.Disabled` : c'est une propriété du **profil**.
- Si le perso courant est désactivé → bascule sur le suivant actif.

### Profils
Changer de profil applique l'ordre **et** les activations (`ApplyProfileEnabled`),
en minimisant/restaurant réellement les fenêtres concernées. Un nouveau profil démarre
tout actif ; une copie hérite de l'original.

### Drag & drop (`Helpers/DragDropBehavior.cs`)
- Implémentation **custom sans OLE DragDrop** (`DoDragDrop` a ~400ms de latence).
- `MouseDown` mémorise la source · `MouseMove` déplace live au-delà du seuil ·
  `MouseUp` sauvegarde **une seule fois**. Clic sans seuil dépassé → focus.
- Toute la carte est saisissable (pas de poignée dédiée).
- **Retour visuel** : `IsDragging` (anneau turquoise + ombre courte, sans mise à
  l'échelle — elle faisait déborder la carte), ligne d'insertion dessinée dans
  l'`AdornerLayer` (`DropLineAdorner`, masquée hors viewport), capture souris,
  auto-scroll près des bords.

### Réordonnancement taskbar (`Helpers/TaskbarOrder.cs`)
- **AppUserModelID unique par fenêtre** (`SHGetPropertyStoreForWindow` +
  `PKEY_AppUserModel_ID`) pour casser le groupage Windows.
- L'AUMID inclut un **jeton de session + un numéro de run** : c'est le *changement*
  d'AUMID qui force Explorer à recréer le bouton de taskbar, donc à le replacer.
  Appliqué séquentiellement (40ms d'écart) pour qu'Explorer traite dans l'ordre reçu.
- Séquence : masquer Altéchap (protège le rendu WPF) → AUMID → tout minimiser →
  restaurer dans l'ordre → re-minimiser ceux qui l'étaient → réafficher Altéchap.
- **Verrou `SemaphoreSlim(1,1)`** + `Reordering` qui grise le bouton `⊞` : deux
  séquences entrelacées produisaient un ordre aléatoire.
- `SetForegroundWindow` échoue souvent ici (Altéchap est masquée, donc sans droits de
  premier plan) — sans effet sur l'ordre, fixé par l'AUMID.
- Accessible via le bouton `⊞` et le menu du tray. Chaque run est journalisé.

### Raccourcis globaux (`Services/HotkeyService.cs`)
- `RegisterHotKey` / `WM_HOTKEY`, pas de hook clavier bas niveau.
- **Pause automatique** quand Altéchap a le focus, reprise à la perte de focus.
  **Exception** : `ToggleWindow` reste inscrit même en pause, sinon il ne pourrait
  jamais servir à masquer la fenêtre.
- `SuspendAutoToggle()` / `ResumeAutoToggle()` pendant le réordonnancement.
- `Validate(cfg)` teste réellement chaque combinaison auprès de Windows (inscription
  puis libération immédiate) et remonte celles qui sont refusées.
- Debounce 30ms, pas de `MOD_NOREPEAT` (répétition native conservée).
- 5 types : `Next`, `Prev`, `NextProfile`, `ToggleWindow`, `Direct` (par personnage).

---

## 5. Persistance

| Donnée | Emplacement | Format |
|---|---|---|
| Config (persos, profils, raccourcis, géométrie) | `%AppData%\Altechap\config.json` | JSON indenté |
| Sauvegarde de la config précédente | `%AppData%\Altechap\config.json.bak` | JSON |
| Icônes de classe (cache) | `%AppData%\Altechap\icons\symbol_{id}.png` | PNG |
| Journal | `%AppData%\Altechap\log.txt` (+ `log.old.txt`) | Texte, rotation 512 Ko |

**Écriture atomique** (`StorageService.Save`) : écriture dans `.tmp` → **relecture de
contrôle** → `File.Replace` (l'ancienne version devient `.bak`). Un crash pendant
l'écriture ne peut plus détruire la configuration. `Load` bascule seul sur le `.bak`
si le fichier principal est illisible.

**Journal** (`Services/Log.cs`) : plus aucun `catch` silencieux sur les chemins qui
comptent. Tracés : démarrage/arrêt avec version, config chargée, migration des
doublons, création de personnage, ré-identification de fenêtre, application des
raccourcis (et ceux refusés par Windows), période du watcher, runs de réordonnancement,
erreurs disque et réseau.

---

## 6. Interface (`Views/MainWindow.xaml`, 538 lignes)

### Structure (grille verticale)
1. **En-tête** : logo, nom, statut, **version**, bouton 📌 (épinglage), ⌨ (raccourcis), Actualiser
2. **Bandeau initiative** (si ≥1 perso lié) : `Nom (Classe) → Nom (Classe) → …`
3. **Barre profil** : ComboBox + Renommer / Nouveau / Dupliquer / Sauvegarder l'ordre / Supprimer
4. **Liste** (`ListBox` + `DragDropBehavior`) : cartes persos, état vide si aucun lié
5. **Bandeau « personnages hors ligne »** : replié/déplié selon qu'il y a des comptes
   ouverts à côté ; c'est le **seul endroit** où supprimer un perso dont la fenêtre
   est fermée
6. **Pied** : ◀ / perso courant / ▶ / ⊞ réordonner la taskbar

### Fenêtres secondaires
- `HotkeySettingsWindow.xaml` — 600×560, redimensionnable (min 520×420). 4 raccourcis
  globaux + focus direct **des fenêtres actives uniquement** (message explicite si
  aucune n'est ouverte). Capture de touche via overlay, refus des doublons.
- `CharacterEditDialog.xaml` — nom / classe / motif, grille de classes générée en C#.
- `WindowPickerDialog.xaml` — liaison manuelle à une fenêtre détectée.

### Conventions de style (`App.xaml`)
- `RB` : bouton arrondi générique. **`MinWidth`/`MinHeight` = 30** — tout bouton
  déclaré plus petit voit sa bordure rognée (cause des `✕` coupés).
- `RBClear` : bouton « effacer » discret, rouge seulement au survol.
- `Btn*` : Primary (ambre), Teal, Ghost, Danger, Icon.

### Icône système (tray)
Menu dynamique : profil actif, sous-menu de changement, réordonner, ouvrir, quitter.
Fermer la fenêtre principale l'occulte (`Hide`) sans quitter l'application.

---

## 7. Points d'attention / fragilité connue

- **`ReorderTaskbar`** repose sur une séquence de délais fixes (40 à 200ms) et sur un
  comportement non documenté d'Explorer. Deux sources de non-déterminisme ont été
  supprimées (§10.2), mais l'opération reste sensible à la charge système. Piste
  suivante si nécessaire : `SW_HIDE`/`SW_SHOW` par fenêtre (recréation garantie du
  bouton, plus brutal pour le client de jeu).
- **Détection trop large** : voir la limite `UnityWndClass` en §3.
- **Distinction auto-découvert / configuré** : repose sur `MatchPattern` vide + nom
  « ressemblant » à un titre brut. Un perso renommé manuellement avec un nom contenant
  un numéro de version serait pris pour un auto-découvert.
- **`ScanAll`** tourne sur le thread appelant lors du `Init` initial ; le watcher et
  `ScanNow` sont bien dispatchés.

---

## 8. Fichiers du projet

```
Altechap/
├── Altechap.csproj                  — net8.0-windows, Version, icônes embarquées, exclusion Tests/
├── App.xaml(.cs)                    — styles globaux, tray, instance unique, AUMID process
├── Models/Models.cs                 — Character, ClassDefs, CombatProfile, HotkeyConfig,
│                                      WindowPlacement, AppData, DofusWindow
├── ViewModels/MainViewModel.cs      — orchestration (896 lignes)
├── Services/
│   ├── WindowScanner.cs             — EnumWindows + parsing de titre
│   ├── CharacterIdentity.cs         — identité perso ↔ fenêtre + migration (pur, testé)
│   ├── HotkeyService.cs             — RegisterHotKey / WM_HOTKEY / validation
│   ├── StorageService.cs            — JSON atomique + repli .bak
│   ├── IconCacheService.cs          — extraction des icônes embarquées, réseau en secours
│   ├── Log.cs                       — journal fichier avec rotation
│   ├── BuildInfo.cs                 — version + date de build
│   └── UpdateService.cs             — release GitHub, téléchargement, install silencieuse
├── Helpers/
│   ├── Win32.cs                     — P/Invoke (fenêtres, focus, raccourcis)
│   ├── TaskbarOrder.cs              — AUMID + réordonnancement taskbar
│   └── DragDropBehavior.cs          — drag & drop custom + ligne d'insertion
├── Views/                           — MainWindow, HotkeySettings, CharacterEdit,
│                                      WindowPicker, UpdateDialog
├── Resources/icons/*.png            — 19 symboles de classe embarqués
├── Installer/
│   ├── Altechap.iss                 — script Inno Setup 6
│   └── README.md                    — procédure de diffusion et de publication
├── build.ps1                        — publish → installeur → release GitHub
└── Tests/                           — xUnit, 43 cas
```

**Total** : ~2 900 lignes de C# applicatif + ~1 070 lignes de XAML + ~300 lignes de tests.

---

## 9. Tests

```bash
dotnet test Tests/Altechap.Tests.csproj
```

43 cas, sur la logique **pure** extraite dans `CharacterIdentity`, `WindowScanner`
et `UpdateService` :

- **Analyse des titres** : client actuel et ancien, écran de chargement, `Cra`/`Crâ`.
- **Association fenêtre ↔ perso** : pseudo exact, préfixe commun (`Kaguya`/`Kaguya2`),
  perso nommé « Dofus », motif libre, regex valide, regex invalide (ne doit pas lever).
- **Migration des doublons** : raccourci préservé, ordre et désactivations re-ciblés,
  porteur du raccourci conservé en priorité, noms auto-découverts jamais fusionnés.
- **Comparaison de versions** : tags `v2.1.0` / `2.1` / `2.1.0-rc1`, rejet des tags
  non numériques (`latest`), une version égale ou antérieure ne déclenche rien.

`Tests/**` est exclu du glob de compilation d'`Altechap.csproj` (sans quoi les tests
seraient compilés dans l'application).

---

## 10. Historique des corrections

### 10.1 Perte des raccourcis directs entre sessions — cause racine

1. Au lancement de Dofus, la fenêtre s'appelle `Dofus` (chargement). `MergeWindows`
   créait aussitôt un personnage fantôme avec un **nouveau Guid**.
2. Quand le titre se résolvait, le fantôme était **renommé** (« Akame ») et devenait
   permanent.
3. Le vrai « Akame » persisté — celui qui porte le raccourci, indexé par `Guid` —
   restait non lié pour toujours.
4. Résultat : un doublon par session (45 persos pour 8 comptes) et une touche sans effet.

Corrigé par : aucune création tant que le titre n'est pas résolu · ré-identification
au moment où il se résout · égalité stricte du pseudo · migration `MergeDuplicates`
qui re-cible ordres et raccourcis · purge des orphelins qui épargne les porteurs de
raccourci.

**Second point de fuite** : la fenêtre Raccourcis reconstruisait `Direct` à partir des
seules lignes affichées → tout raccourci d'un perso absent de la liste était effacé.
L'écriture part désormais de la config existante.

### 10.2 Réordonnancement taskbar non déterministe

- **AUMID figé par position** : une fenêtre retombant sur le même index gardait le même
  identifiant → aucun événement, aucun déplacement, ordre partiellement faux au 2ᵉ appel.
  L'AUMID contient maintenant session + numéro de run.
- **Réentrance** : cliquer plusieurs fois entrelaçait les séquences. Verrou + bouton grisé.

### 10.3 Boutons rognés

Le template `RB` impose `MinWidth`/`MinHeight` = 30 ; les boutons déclarés en 26×26 ou
28×28 avaient une bordure plus grande qu'eux-mêmes. Tous passés en 32×32, colonnes
élargies en conséquence, et marge réservée à la barre de défilement dans la liste des
raccourcis. Les `StackPanel` horizontaux (qui mesurent leurs enfants en largeur infinie,
donc débordent au lieu de tronquer) ont été remplacés par des `Grid` + `TextTrimming`.

### 10.4 Fonctionnalités mortes réactivées

- **Épinglage** : `AlwaysOnTop` était persisté et sa commande existait, mais `Topmost`
  n'apparaissait nulle part et aucun bouton n'y était lié. La fenêtre n'a jamais été au
  premier plan. Bouton 📌 + binding ajoutés.
- **`RefreshSeconds`** : persisté, jamais lu (watcher figé à 2s en dur).

### 10.5 Autres apports

Journal fichier · écriture atomique de la config · instance unique · position/taille de
fenêtre mémorisées et recadrées sur l'écran · activation par profil · raccourci
afficher/masquer · version affichée dans l'en-tête et le journal · bandeau des
personnages hors ligne (seul moyen de supprimer un perso non lié) · icônes de classe
embarquées · tests.

### 10.6 Écarté

Overlay in-game de l'ordre d'initiative (complexité élevée, et zone grise vis-à-vis
des règles sur les outils tiers).

> **Réintégrés en v2.0.1** — le démarrage automatique avec Windows et la publication
> self-contained avaient été écartés tant que l'usage restait personnel. La diffusion
> à des tiers les rend nécessaires : le runtime .NET 8 absent est le premier motif
> d'échec d'une installation WPF, et le démarrage automatique est devenu une simple
> case à cocher de l'assistant (§11).

---

## 11. Distribution et mises à jour

### Chaîne de production (`build.ps1`)

```
dotnet publish (self-contained win-x64) → dist\app  (~146 Mo)
        ↓  ISCC.exe Installer\Altechap.iss
dist\Altechap-Setup-<version>.exe  (~48 Mo)
        ↓  gh release create v<version>   (option -Release)
release GitHub — c'est ce que l'app interroge
```

`SatelliteResourceLanguages=fr;en` dans le `.csproj` : les 13 autres langues de
ressources du runtime pèsent ~15 Mo pour rien.

### Installeur (`Installer/Altechap.iss`)

- **Par utilisateur** (`PrivilegesRequired=lowest`) → `%LocalAppData%\Programs\Altechap`,
  **aucune invite UAC**. C'est la condition qui rend la mise à jour silencieuse
  possible : un installeur admin ouvrirait une fenêtre d'élévation que personne
  ne voit quand elle est déclenchée depuis l'app.
- `AppId = {8F3C2A17-…}` : identité du produit pour Windows. **Ne jamais le changer** —
  chaque version s'installerait à côté de la précédente.
- `CloseApplications=yes` : le Restart Manager ferme une instance restée ouverte
  au lieu d'échouer sur un `.exe` verrouillé.
- Tâches optionnelles : raccourci bureau, lancement au démarrage de Windows.
- Désinstallation : les journaux partent, `%AppData%\Altechap\config.json` est
  **conservé** sauf réponse explicite à la question posée en fin de désinstallation.

### Vérificateur (`Services/UpdateService.cs`)

| Aspect | Choix |
|---|---|
| Source | `api.github.com/repos/3WVKV/Altechap/releases/latest` |
| Constantes | `Owner` / `Repo` — seule source de vérité, à corriger si le dépôt bouge |
| Déclenchement | 4 s après le démarrage (si `AppData.CheckUpdates`), ou à la demande |
| Comparaison | tag `v2.1.0` → `Version`, strictement supérieure à `BuildInfo.Version` |
| Filtres | brouillons et pré-releases ignorés ; release sans `.exe` attaché ignorée |
| Asset retenu | `.exe` contenant « setup » / « install », sinon premier `.exe` |
| Robustesse | `CheckAsync` ne lève jamais : réseau coupé, 404 (aucune release), quota API → `null` et une ligne de journal |
| Timeout | 15 s — la vérification ne doit jamais faire traîner le démarrage |

L'API GitHub exige un `User-Agent` (403 sinon) et tolère 60 requêtes/h/IP sans
jeton, très au-dessus d'une vérification par démarrage.

### Application de la mise à jour

1. `UpdateDialog` affiche les notes de release et propose **Installer et redémarrer**
   / **Plus tard** / **Ignorer cette version**.
2. Téléchargement dans `%Temp%\Altechap\` (fichier `.part` renommé à la fin, barre
   de progression), échec → proposition d'ouvrir la page GitHub.
3. `setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOLAUNCH=1`, puis
   `Shutdown()` immédiat — l'installeur ne peut pas remplacer un exécutable
   en cours d'exécution.
4. `/AUTOLAUNCH=1` est lu par la fonction `ShouldAutoLaunch` du `.iss`, qui relance
   Altéchap en fin d'installation. C'est le seul mécanisme de retour : sans lui,
   l'app disparaîtrait silencieusement après la mise à jour.

**Refus mémorisé** : `AppData.SkippedUpdate` stocke la version refusée. Elle n'est
plus proposée au démarrage, mais une version *ultérieure* l'est, et la vérification
manuelle passe outre.

**Points d'entrée manuels** : clic sur le numéro de version dans l'en-tête, ou
menu de l'icône système → « ⟳ Vérifier les mises à jour ». Ces deux chemins
affichent aussi le cas « vous êtes à jour », que le mode silencieux tait.

### Limite connue

L'installeur n'est **pas signé**. SmartScreen affiche « Windows a protégé votre
ordinateur » aux premiers téléchargements, y compris pour les mises à jour
téléchargées par l'app (le fichier `%Temp%` porte la marque du web). Lever
l'avertissement demande un certificat de signature de code payant.
