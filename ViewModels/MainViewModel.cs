using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Altechap.Helpers;
using Altechap.Models;
using Altechap.Services;

using WpfApp = System.Windows.Application;

namespace Altechap.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly StorageService  _store   = new();
    private readonly IconCacheService _icons  = new();
    public  readonly HotkeyService   Hotkeys  = new();

    private AppData _data = new();
    private System.Threading.Timer? _watchTimer;   // surveille les titres
    private volatile bool _scanRunning = false;

    [ObservableProperty] private ObservableCollection<Character>     _characters    = new();
    [ObservableProperty] private ObservableCollection<CombatProfile> _profiles      = new();

    /// <summary>
    /// Vue filtrée de Characters : uniquement ceux dont la fenêtre Dofus est ouverte (IsLinked).
    /// La ListBox se bind sur cette propriété → liste vide au démarrage sans Dofus.
    /// </summary>
    public ObservableCollection<Character> LinkedCharacters { get; } = new();

    /// <summary>
    /// Persos connus dont aucune fenêtre n'est ouverte. Sans cette vue ils sont
    /// invisibles — donc impossibles à supprimer — et s'accumulent indéfiniment.
    /// </summary>
    public ObservableCollection<Character> OfflineCharacters { get; } = new();

    /// <summary>Reconstruit LinkedCharacters depuis Characters (appelé après chaque MergeWindows).</summary>
    private void RebuildLinkedCharacters()
    {
        // Préserver l'ordre : garder les éléments existants en place, ajouter/retirer les delta
        var shouldBeLinked = Characters.Where(c => c.IsLinked).ToList();

        // Retirer ceux qui ne sont plus liés
        for (int i = LinkedCharacters.Count - 1; i >= 0; i--)
            if (!shouldBeLinked.Contains(LinkedCharacters[i]))
                LinkedCharacters.RemoveAt(i);

        // Ajouter/réordonner ceux qui manquent
        for (int i = 0; i < shouldBeLinked.Count; i++)
        {
            var ch = shouldBeLinked[i];
            int cur = LinkedCharacters.IndexOf(ch);
            if (cur < 0)
                LinkedCharacters.Insert(Math.Min(i, LinkedCharacters.Count), ch);
            else if (cur != i)
                LinkedCharacters.Move(cur, i);
        }

        // Miroir : tout ce qui n'est pas lié
        var offline = Characters.Where(c => !c.IsLinked).ToList();
        for (int i = OfflineCharacters.Count - 1; i >= 0; i--)
            if (!offline.Contains(OfflineCharacters[i]))
                OfflineCharacters.RemoveAt(i);
        for (int i = 0; i < offline.Count; i++)
        {
            var ch = offline[i];
            int cur = OfflineCharacters.IndexOf(ch);
            if (cur < 0) OfflineCharacters.Insert(Math.Min(i, OfflineCharacters.Count), ch);
            else if (cur != i) OfflineCharacters.Move(cur, i);
        }

        // Déplier la section « hors ligne » seulement quand elle sert à faire le
        // ménage : des comptes ouverts ET des restes à côté. Dofus fermé, tout le
        // roster est hors ligne — c'est normal, inutile de l'étaler.
        HasLinked = LinkedCharacters.Count > 0;
    }

    [ObservableProperty] private bool _hasLinked;
    [ObservableProperty] private CombatProfile? _activeProfile;
    [ObservableProperty] private Character?     _current;
    [ObservableProperty] private string         _initiativeText = string.Empty;
    [ObservableProperty] private string         _status         = "Prêt";
    [ObservableProperty] private bool           _alwaysOnTop    = true;  // épinglé par défaut
    [ObservableProperty] private bool           _scanning       = false;

    private Guid _currentId = Guid.Empty;

    // ── Init ──────────────────────────────────────────────────────────────
    public void Init(System.Windows.Window window)
    {
        _data = _store.Load();

        // Fusionner les doublons hérités des sessions précédentes AVANT tout le reste :
        // les raccourcis directs sont indexés par Guid, ils doivent pointer sur le
        // personnage survivant.
        int merged = CharacterIdentity.MergeDuplicates(_data);
        if (merged > 0)
            Log.Info($"Migration : {merged} doublon(s) fusionné(s) → {_data.Characters.Count} personnage(s).");

        AlwaysOnTop = _data.AlwaysOnTop;

        // Restaurer les personnages persistés
        Characters = new ObservableCollection<Character>(_data.Characters);

        // Profils
        Profiles = new ObservableCollection<CombatProfile>(_data.Profiles);
        if (!Profiles.Any()) Profiles.Add(new CombatProfile { Name = "Défaut" });
        ActiveProfile = Profiles.FirstOrDefault(p => p.Id == _data.ActiveProfile) ?? Profiles[0];

        // Hotkeys
        Hotkeys.Attach(window);
        Hotkeys.OnNext        += NavigateNext;
        Hotkeys.OnPrev        += NavigatePrev;
        Hotkeys.OnNextProfile += SwitchToNextProfile;
        Hotkeys.OnDirect += id => WpfApp.Current?.Dispatcher.Invoke(() => FocusById(id));
        Hotkeys.OnToggleWindow += () => WpfApp.Current?.Dispatcher.Invoke(ToggleMainWindow);
        // On passe la collection vivante : les persos liés après le démarrage
        // récupèrent aussi leur raccourci direct sans re-Apply manuel.
        Hotkeys.Apply(_data.Hotkeys, Characters);

        // Purger les persos auto-decouverts orphelins de la session precedente
        // (Handle=Zero + pas de MatchPattern + nom brut type "Dofus" ou "Pseudo - 3.4.x - Release")
        // Un perso porteur d'un raccourci direct n'est JAMAIS considéré comme orphelin.
        var orphans = Characters
            .Where(c => c.Handle == nint.Zero
                     && string.IsNullOrWhiteSpace(c.MatchPattern)
                     && IsAutoDiscoveredName(c.Name)
                     && !_data.Hotkeys.Direct.ContainsKey(c.Id))
            .ToList();
        foreach (var ch in orphans)
        {
            Characters.Remove(ch);
            foreach (var p in Profiles) p.Order.Remove(ch.Id);
        }
        Log.Info($"Config chargée : {Characters.Count} perso(s), {Profiles.Count} profil(s), " +
                 $"{_data.Hotkeys.Direct.Count} raccourci(s) direct(s), {orphans.Count} orphelin(s) purgé(s).");

        // Profils antérieurs à l'activation par profil : les initialiser depuis
        // l'état global actuel des personnages (sinon tout paraîtrait actif).
        foreach (var p in Profiles)
            p.Disabled ??= Characters.Where(c => !c.Enabled).Select(c => c.Id).ToList();

        // Reconstruire l'ordre depuis le profil actif + appliquer ses activations
        ApplyProfileOrder(rebuildText: false);
        ApplyProfileEnabled(touchWindows: false);

        // Restaurer Current — seulement si une fenêtre est déjà liée (sinon footer reste vide)
        var firstEnabled = Characters.FirstOrDefault(c => c.Enabled && c.IsLinked)
                        ?? Characters.FirstOrDefault(c => c.IsLinked);
        if (firstEnabled != null) { _currentId = firstEnabled.Id; Current = firstEnabled; }
        else { _currentId = Guid.Empty; Current = null; }

        RebuildInitText();
        RebuildLinkedCharacters(); // liste vide si aucune fenêtre Dofus ouverte

        // Rendre définitive la fusion des doublons / la purge des orphelins
        Save();

        // Télécharger les icônes manquantes
        _icons.EnsureAll(Characters.Select(c => c.ClassId), id =>
            WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));

        // Démarrer le watcher de titres (détecte Dofus lancé APRÈS l'app)
        StartWatcher();
    }

    // ── Navigation ────────────────────────────────────────────────────────

    /// <summary>
    /// Aligne le personnage courant sur la fenêtre réellement au premier plan.
    ///
    /// Sans ça, l'application ne connaissait que les déplacements qu'elle avait
    /// elle-même provoqués : cliquer une fenêtre à la souris la laissait sur son
    /// dernier index connu, et la navigation suivante repartait de cet index
    /// fantôme au lieu de l'écran affiché. Retourne vrai si le courant a bougé.
    /// </summary>
    private bool SyncCurrentFromForeground()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == 0) return false;

        // Fenêtre étrangère (Altéchap, navigateur, autre jeu) : on conserve le
        // dernier personnage connu plutôt que de perdre le fil.
        var ch = Characters.FirstOrDefault(c => c.Handle == fg);
        if (ch == null || ch.Id == _currentId) return false;

        SetCurrent(ch);
        return true;
    }

    /// <summary>Suivant dans l'ordre d'initiative (haut → bas).</summary>
    public void NavigateNext() => NavigateBy(+1);

    /// <summary>Précédent dans l'ordre d'initiative (bas → haut).</summary>
    public void NavigatePrev() => NavigateBy(-1);

    private void NavigateBy(int step)
    {
        // Repartir de la fenêtre affichée, pas du dernier saut enregistré.
        SyncCurrentFromForeground();

        var target = Navigation.Neighbour(Characters, _currentId, step);
        if (target == null) return;

        SetCurrent(target);
        WindowScanner.Focus(target);
    }

    [RelayCommand]
    public void FocusCharacter(Character ch)
    {
        SetCurrent(ch);
        WindowScanner.Focus(ch);
    }

    private void FocusById(Guid id)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == id);
        if (ch != null) FocusCharacterCommand.Execute(ch);
    }

    private void SetCurrent(Character ch)
    {
        _currentId = ch.Id;
        Current    = ch;
    }

    // ── Toggle Enabled ────────────────────────────────────────────────────
    [RelayCommand]
    public void ToggleEnabled(Character ch) => SetEnabled(ch, !ch.Enabled);

    public void SetEnabled(Character ch, bool value)
    {
        if (ch.Enabled == value) return; // pas de changement inutile
        ch.Enabled = value;

        // ── Gérer la fenêtre Dofus liée ─────────────────────────────────
        if (ch.Handle != nint.Zero && Altechap.Helpers.Win32.IsWindow(ch.Handle))
        {
            if (!value)
            {
                // Toggle OFF → minimiser la fenêtre Dofus (sans changer le focus)
                Altechap.Helpers.Win32.MinimizeNoFocus(ch.Handle);
            }
            else
            {
                // Toggle ON → restaurer la fenêtre SANS voler le focus
                // L'utilisateur reste sur sa fenêtre courante
                Altechap.Helpers.Win32.ShowWithoutFocus(ch.Handle);
            }
        }

        // Mémoriser l'état DANS le profil actif : « je joue à 4 ce soir » est une
        // propriété du profil, pas un réglage global.
        if (ActiveProfile != null)
        {
            ActiveProfile.Disabled ??= new List<Guid>();
            if (value) ActiveProfile.Disabled.Remove(ch.Id);
            else if (!ActiveProfile.Disabled.Contains(ch.Id)) ActiveProfile.Disabled.Add(ch.Id);
        }

        // Si le personnage courant vient d'être désactivé → passer au suivant
        // (sans focus, juste mettre à jour l'état interne)
        if (!ch.Enabled && _currentId == ch.Id)
        {
            var enabled = Characters.Where(c => c.Enabled && c.IsLinked).ToList();
            if (enabled.Count == 0) enabled = Characters.Where(c => c.Enabled).ToList();
            if (enabled.Count > 0)
                SetCurrent(enabled[0]); // mettre à jour sans focus
        }

        RebuildInitText();
        Save();
    }

    // ── D&D ───────────────────────────────────────────────────────────────
    public void MoveCharacter(int from, int to)
    {
        if (from == to || from < 0 || to < 0
            || from >= Characters.Count || to >= Characters.Count) return;
        Characters.Move(from, to);
        if (ActiveProfile != null)
            ActiveProfile.Order = Characters.Select(c => c.Id).ToList();
        Current = Characters.FirstOrDefault(c => c.Id == _currentId);
        RebuildLinkedCharacters();
        RebuildInitText();
        // Pas de Save() ici — appelé par DragDropBehavior.OnUp via SaveAfterDrag()
    }

    /// <summary>Appelé par DragDropBehavior après la fin du drag pour sauvegarder une seule fois.</summary>
    public void SaveAfterDrag() => Save();

    // ── Scan ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ScanNow() => await ScanAndSyncAsync(notify: true);

    private async Task ScanAndSyncAsync(bool notify = false)
    {
        if (_scanRunning) return;
        _scanRunning = true;
        Scanning = true;

        var windows = await System.Threading.Tasks.Task.Run(() => WindowScanner.ScanAll());

        WpfApp.Current?.Dispatcher.Invoke(() =>
        {
            bool changed = MergeWindows(windows);
            _scanRunning = false;
            Scanning = false;

            if (notify || changed)
            {
                Notify(_loadingCount > 0
                    ? $"{LinkedCharacters.Count} compte(s) détecté(s) · {_loadingCount} en chargement…"
                    : $"{LinkedCharacters.Count} compte(s) détecté(s)");
            }

        });
    }

    /// <summary>
    /// Fusionne les fenêtres détectées avec la liste en mémoire.
    /// - Invalide les handles morts
    /// - Relie les nouvelles fenêtres aux persos existants (par nom ou handle)
    /// - Crée de nouveaux persos pour les fenêtres inconnues
    /// - Met à jour le ClassId si le titre a changé (bug "lancé avant Dofus")
    /// </summary>
    private int _loadingCount;   // fenêtres encore sur l'écran de chargement

    private bool MergeWindows(List<DofusWindow> windows)
    {
        bool changed = false;
        _loadingCount = windows.Count(w => !IsResolved(w));

        // 1. Invalider handles morts + nettoyer les persos auto-détectés sans config
        var toRemove = new List<Character>();
        foreach (var ch in Characters)
        {
            if (ch.Handle != nint.Zero && !Win32.IsWindow(ch.Handle))
            {
                // Supprimer si le perso est purement auto-découvert (jamais configuré manuellement)
                // = pas de MatchPattern ET nom qui ressemble à un titre brut Dofus
                bool isAutoOnly = string.IsNullOrWhiteSpace(ch.MatchPattern)
                    && IsAutoDiscoveredName(ch.Name);

                if (isAutoOnly)
                    toRemove.Add(ch);
                else
                {
                    ch.Handle   = nint.Zero;
                    ch.RawTitle = string.Empty;
                }
                changed = true;
            }
        }
        foreach (var ch in toRemove)
        {
            Characters.Remove(ch);
            foreach (var p in Profiles) p.Order.Remove(ch.Id);
        }

        var linked = new HashSet<nint>(Characters.Select(c => c.Handle).Where(h => h != nint.Zero));

        foreach (var w in windows)
        {
            if (linked.Contains(w.Handle))
            {
                // Fenêtre déjà connue — vérifier si le titre a changé (Dofus lancé après)
                var existing = Characters.FirstOrDefault(c => c.Handle == w.Handle);
                if (existing != null && w.RawTitle != existing.RawTitle)
                {
                    var oldTitle = existing.RawTitle;
                    existing.RawTitle = w.RawTitle;

                    // Le titre vient de se résoudre (fin de l'écran de chargement) :
                    // si un perso déjà connu porte ce nom, on lui REND sa fenêtre au lieu
                    // de renommer la carte courante — sinon son Guid (et donc son raccourci
                    // direct) serait perdu à chaque session.
                    if (IsResolved(w))
                    {
                        var owner = Characters.FirstOrDefault(c =>
                            !ReferenceEquals(c, existing) && c.Handle == nint.Zero && NameMatches(c, w));

                        if (owner != null)
                        {
                            owner.Handle   = w.Handle;
                            owner.RawTitle = w.RawTitle;
                            if (w.ClassId > 0) owner.ClassId = w.ClassId;

                            Log.Info($"Ré-identification : fenêtre {w.Handle} rendue à « {owner.Name} » " +
                                     $"({owner.Id}) — carte provisoire « {existing.Name} » libérée.");

                            existing.Handle   = nint.Zero;
                            existing.RawTitle = string.Empty;
                            if (IsDisposable(existing))
                            {
                                Characters.Remove(existing);
                                foreach (var p in Profiles) p.Order.Remove(existing.Id);
                            }

                            if (w.ClassId > 0)
                                _icons.EnsureAll([w.ClassId], id =>
                                    WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
                            changed = true;
                            continue;
                        }
                    }

                    // Mettre à jour le nom si :
                    // - le nom actuel est vide OU
                    // - le nom actuel était l'ancien titre brut (ex: "Dofus") OU
                    // - le nom actuel contient "Dofus" sans pseudo (titre de chargement)
                    bool nameIsStale = string.IsNullOrWhiteSpace(existing.Name)
                        || existing.Name == oldTitle
                        || existing.Name.Equals("Dofus", StringComparison.OrdinalIgnoreCase)
                        || existing.Name.StartsWith("Dofus ", StringComparison.OrdinalIgnoreCase)
                        // Nom qui contient encore la version Dofus → à rafraîchir
                        || System.Text.RegularExpressions.Regex.IsMatch(
                               existing.Name, @"\d+\.\d+\.\d+");

                    if (!string.IsNullOrWhiteSpace(w.CharName) && nameIsStale)
                        existing.Name = w.CharName;

                    // Mettre à jour la classe si on l'a maintenant
                    if (w.ClassId > 0 && existing.ClassId != w.ClassId)
                    {
                        existing.ClassId = w.ClassId;
                        _icons.EnsureAll([w.ClassId], id =>
                            WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
                    }
                    changed = true;
                }
                continue;
            }

            // Chercher un perso persisté avec le même nom
            var byName = Characters.FirstOrDefault(c =>
                c.Handle == nint.Zero && NameMatches(c, w));

            if (byName != null)
            {
                byName.Handle   = w.Handle;
                byName.RawTitle = w.RawTitle;
                if (!string.IsNullOrWhiteSpace(w.CharName) && string.IsNullOrWhiteSpace(byName.Name))
                    byName.Name = w.CharName;
                if (w.ClassId > 0 && byName.ClassId == 0)
                {
                    byName.ClassId = w.ClassId;
                    _icons.EnsureAll([w.ClassId], id =>
                        WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
                }
                linked.Add(w.Handle);
                changed = true;
            }
            else
            {
                // Fenêtre encore sur l'écran de chargement (titre "Dofus", pas de pseudo) :
                // ne RIEN créer. Sinon un perso fantôme naît à chaque lancement de Dofus,
                // se fait renommer quand le titre se résout, et le vrai perso persisté
                // (avec ses raccourcis) reste orphelin.
                if (!IsResolved(w)) continue;

                // Nouvelle fenêtre inconnue
                var newChar = new Character
                {
                    Name    = string.IsNullOrWhiteSpace(w.CharName) ? w.RawTitle : w.CharName,
                    Handle  = w.Handle,
                    RawTitle = w.RawTitle,
                    ClassId = w.ClassId,
                    Enabled = true,
                };
                Characters.Add(newChar);
                foreach (var p in Profiles) p.Order.Add(newChar.Id);
                linked.Add(w.Handle);
                Log.Info($"Nouveau personnage « {newChar.Name} » ({newChar.Id}) — titre : {w.RawTitle}");

                if (w.ClassId > 0)
                    _icons.EnsureAll([w.ClassId], id =>
                        WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
                changed = true;
            }
        }

        if (changed)
        {
            ApplyProfileOrder(rebuildText: true);
            FixCurrentAfterChange();
            Save();
        }

        // Toujours reconstruire la vue filtrée (même sans changement structurel,
        // un handle qui vient d'être assigné doit apparaître)
        RebuildLinkedCharacters();

        // Le pied de fenêtre doit désigner le personnage sous les yeux de
        // l'utilisateur, y compris quand il change de fenêtre à la souris.
        SyncCurrentFromForeground();

        // Filet de sécurité des raccourcis globaux : si l'état des inscriptions
        // a divergé de la réalité (Deactivated manqué en masquant la fenêtre),
        // il se répare ici au lieu de rester faux jusqu'à la fin de la session.
        Hotkeys.Reconcile();

        return changed;
    }

    private void FixCurrentAfterChange()
    {
        if (_currentId == Guid.Empty || !Characters.Any(c => c.Id == _currentId))
        {
            var first = Characters.FirstOrDefault(c => c.Enabled) ?? Characters.FirstOrDefault();
            if (first != null) SetCurrent(first);
            else { Current = null; _currentId = Guid.Empty; }
        }
    }

    // ── Identité des personnages ──────────────────────────────────────────
    // Logique déportée dans Services/CharacterIdentity (pure → testable).
    private static bool NameMatches(Character ch, DofusWindow w) => CharacterIdentity.NameMatches(ch, w);
    private static bool IsResolved(DofusWindow w)                => CharacterIdentity.IsResolved(w);
    private static bool IsAutoDiscoveredName(string name)        => CharacterIdentity.IsAutoDiscoveredName(name);

    /// <summary>Un perso auto-découvert jamais configuré peut être supprimé sans rien perdre.</summary>
    private bool IsDisposable(Character ch)
        => string.IsNullOrWhiteSpace(ch.MatchPattern)
        && IsAutoDiscoveredName(ch.Name)
        && !_data.Hotkeys.Direct.ContainsKey(ch.Id);

    // ── Watcher de titres (PATCH 3 — détection si Dofus lancé après) ──────
    private void StartWatcher()
    {
        _watchTimer?.Dispose();
        // Période configurable via RefreshSeconds dans config.json (bornée 1-60s)
        var period = TimeSpan.FromSeconds(Math.Clamp(_data.RefreshSeconds, 1, 60));
        Log.Info($"Watcher démarré — scan toutes les {period.TotalSeconds:0}s.");
        _watchTimer = new System.Threading.Timer(_ =>
        {
            if (_scanRunning) return;
            var windows = WindowScanner.ScanAll();
            WpfApp.Current?.Dispatcher.Invoke(() => MergeWindows(windows));
        }, null, period, period);
    }

    // ── Rafraîchir les icônes PNG dans l'UI après téléchargement ─────────
    private void RefreshClassIcons(int classId)
    {
        foreach (var ch in Characters.Where(c => c.ClassId == classId))
            ch.NotifyIconChanged();
    }

    // ── Lien manuel ───────────────────────────────────────────────────────
    public void LinkManual(Character ch, nint handle, string title)
    {
        ch.Handle   = handle;
        ch.RawTitle = title;
        var (name, classId) = WindowScanner.ParseTitle(title);
        if (!string.IsNullOrWhiteSpace(name) &&
            (string.IsNullOrWhiteSpace(ch.Name) || ch.Name == "Nouveau"))
            ch.Name = name;
        if (classId > 0 && ch.ClassId == 0)
        {
            ch.ClassId = classId;
            _icons.EnsureAll([classId], id =>
                WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
        }
        Notify($"Lié : {ch.Name}");
        Save();
    }

    // ── Changement de profil (tray + raccourci) ─────────────────────────
    public void SwitchToProfile(Guid profileId)
    {
        var p = Profiles.FirstOrDefault(x => x.Id == profileId);
        if (p != null && p != ActiveProfile)
        {
            ActiveProfile = p;
            Notify($"Profil : {p.Name}");
        }
    }

    public void SwitchToNextProfile()
    {
        if (Profiles.Count < 2) return;
        int idx = Profiles.IndexOf(ActiveProfile!);
        ActiveProfile = Profiles[(idx + 1) % Profiles.Count];
        Notify($"Profil : {ActiveProfile.Name}");
    }

    // ── Profils ───────────────────────────────────────────────────────────
    partial void OnActiveProfileChanged(CombatProfile? value)
    {
        if (value == null) return;
        _data.ActiveProfile = value.Id;
        value.Disabled ??= new List<Guid>();
        ApplyProfileOrder(rebuildText: true);
        ApplyProfileEnabled(touchWindows: true);
        Save();

        int off = value.Disabled.Count(id => Characters.Any(c => c.Id == id && c.IsLinked));
        Notify(off > 0 ? $"Profil : {value.Name} ({off} compte(s) mis de côté)"
                       : $"Profil : {value.Name}");
    }

    public void RenameProfile(CombatProfile p, string newName)
    {
        p.Name = newName;
        Save();
    }

    [RelayCommand] private void AddProfile()
    {
        // Nouveau profil = tout le monde actif (Disabled vide)
        var p = new CombatProfile
        {
            Name     = $"Profil {Profiles.Count + 1}",
            Order    = Characters.Select(c => c.Id).ToList(),
            Disabled = new List<Guid>(),
        };
        Profiles.Add(p); ActiveProfile = p; Save();
    }

    [RelayCommand] private void DuplicateProfile()
    {
        if (ActiveProfile == null) return;
        var p = new CombatProfile
        {
            Name     = ActiveProfile.Name + " (copie)",
            Order    = [..ActiveProfile.Order],
            Disabled = [..(ActiveProfile.Disabled ?? new List<Guid>())],
        };
        Profiles.Add(p); ActiveProfile = p; Save();
    }

    [RelayCommand] private void DeleteProfile()
    {
        if (ActiveProfile == null || Profiles.Count <= 1) return;
        Profiles.Remove(ActiveProfile); ActiveProfile = Profiles.First(); Save();
    }

    [RelayCommand] private void SaveOrderToProfile()
    {
        if (ActiveProfile == null) return;
        ActiveProfile.Order = Characters.Select(c => c.Id).ToList();
        Save(); Notify("Ordre sauvegardé.");
    }

    // ── Réordonnancement automatique silencieux ──────────────────────────
    private async Task AutoReorderTaskbarAsync()
    {
        var linked = Characters.Where(c => c.IsLinked).ToList();
        if (linked.Count < 2) return; // pas assez de fenêtres

        Hotkeys.SuspendAutoToggle();
        try
        {
            var handles = linked.Select(c => c.Handle).ToList();
            await Altechap.Helpers.TaskbarOrder.ReorderAsync(handles, _selfHwnd);
        }
        catch (Exception ex) { Log.Warn("Réordonnancement automatique de la barre des tâches échoué", ex); }
        finally
        {
            Hotkeys.ResumeAutoToggle();
        }
    }

    // ── Réordonnancement manuel (bouton ⊞) ───────────────────────────────
    [ObservableProperty] private bool _reordering;

    [RelayCommand]
    private async Task ReorderTaskbar()
    {
        // Deux séquences simultanées s'entrelacent et produisent un ordre faux :
        // le bouton est désactivé pendant toute l'opération (cf. IsEnabled dans le XAML)
        // et ce garde-fou couvre aussi l'appel depuis le menu du tray.
        if (Reordering) { Notify("Réordonnancement déjà en cours…"); return; }

        var ordered = Characters.Where(c => c.Enabled && c.IsLinked).ToList();
        if (ordered.Count < 2) { Notify("Moins de 2 comptes actifs et liés."); return; }

        Reordering = true;
        Notify("Réordonnancement barre des tâches…");

        // Minimiser Altéchap via WPF — pas via Win32 SW_HIDE.
        // WPF gère son propre minimize/restore sans corrompre sa surface de rendu.
        // On passe nint.Zero à ReorderAsync : il ne touchera jamais au hwnd WPF.
        var mainWin   = WpfApp.Current?.MainWindow;
        var prevState = mainWin?.WindowState ?? System.Windows.WindowState.Normal;
        if (mainWin != null)
            mainWin.WindowState = System.Windows.WindowState.Minimized;

        Hotkeys.SuspendAutoToggle();
        try
        {
            var handles = ordered.Select(c => c.Handle).ToList();
            await Altechap.Helpers.TaskbarOrder.ReorderAsync(handles, nint.Zero);
        }
        finally
        {
            Hotkeys.ResumeAutoToggle();
            Reordering = false;
        }

        // Restaurer Altéchap via WPF
        if (mainWin != null)
        {
            mainWin.WindowState = prevState;
            mainWin.Activate();
        }

        Notify($"Barre des tâches réordonnée ({ordered.Count} fenêtres).");
    }

    // Handle de la fenêtre principale (pour restaurer le focus après reorder)
    private nint _selfHwnd;
    public void SetSelfHwnd(nint hwnd) => _selfHwnd = hwnd;

    /// <summary>
    /// Applique l'activation mémorisée dans le profil actif.
    /// <paramref name="touchWindows"/> : minimise/restaure réellement les fenêtres
    /// Dofus concernées (vrai lors d'un changement de profil, faux au démarrage
    /// où rien n'est encore lié).
    /// </summary>
    private void ApplyProfileEnabled(bool touchWindows)
    {
        var profile = ActiveProfile;
        if (profile?.Disabled == null) return;

        int changed = 0;
        foreach (var ch in Characters)
        {
            bool shouldBeEnabled = !profile.Disabled.Contains(ch.Id);
            if (ch.Enabled == shouldBeEnabled) continue;

            ch.Enabled = shouldBeEnabled;
            changed++;

            if (touchWindows && ch.Handle != nint.Zero && Win32.IsWindow(ch.Handle))
            {
                if (shouldBeEnabled) Win32.ShowWithoutFocus(ch.Handle);
                else                 Win32.MinimizeNoFocus(ch.Handle);
            }
        }

        if (changed > 0)
        {
            Log.Info($"Profil « {profile.Name} » : {changed} activation(s) appliquée(s).");
            var still = Characters.Where(c => c.Enabled && c.IsLinked).ToList();
            if (Current != null && !Current.Enabled && still.Count > 0) SetCurrent(still[0]);
            RebuildInitText();
        }
    }

    private void ApplyProfileOrder(bool rebuildText)
    {
        if (ActiveProfile == null) { if (rebuildText) RebuildInitText(); return; }
        var ordered = ActiveProfile.Order
            .Select(id => Characters.FirstOrDefault(c => c.Id == id))
            .Where(c => c != null).Cast<Character>().ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = Characters.IndexOf(ordered[i]);
            if (cur >= 0 && cur != i) Characters.Move(cur, i);
        }
        if (rebuildText) RebuildInitText();
    }

    // ── Personnages ───────────────────────────────────────────────────────
    [RelayCommand] private void AddCharacter()
    {
        var ch = new Character { Name = "Nouveau" };
        Characters.Add(ch);
        foreach (var p in Profiles) p.Order.Add(ch.Id);
        RebuildInitText(); Save();
    }

    [RelayCommand] private void RemoveCharacter(Character ch)
    {
        if (_currentId == ch.Id) { _currentId = Guid.Empty; Current = null; }
        Characters.Remove(ch);
        foreach (var p in Profiles) p.Order.Remove(ch.Id);
        RebuildLinkedCharacters();
        RebuildInitText(); Save();
    }

    public void SaveCharacter()
    {
        // Après édition dans dialog : télécharger icône si nécessaire
        _icons.EnsureAll(Characters.Select(c => c.ClassId), id =>
            WpfApp.Current?.Dispatcher.Invoke(() => RefreshClassIcons(id)));
        RebuildInitText();
        Save();
    }

    // ── Mises à jour ──────────────────────────────────────────────────────
    public bool CheckUpdatesEnabled => _data.CheckUpdates;

    /// <summary>Vrai si l'utilisateur a déjà refusé explicitement cette version.</summary>
    public bool IsUpdateSkipped(string version)
        => string.Equals(_data.SkippedUpdate, version, StringComparison.OrdinalIgnoreCase);

    public void SkipUpdate(string version)
    {
        _data.SkippedUpdate = version;
        Save();
        Log.Info($"Mise à jour {version} ignorée à la demande de l'utilisateur.");
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────
    public HotkeyConfig GetHotkeys() => _data.Hotkeys;

    public void ApplyHotkeys(HotkeyConfig cfg)
    {
        _data.Hotkeys = cfg;
        Hotkeys.Apply(cfg, Characters);
        Save();

        // Remonter les combinaisons refusées par Windows (déjà prises par une autre app)
        var failed = Hotkeys.Validate(cfg);
        Log.Info($"Raccourcis appliqués : next={cfg.Next} prev={cfg.Prev} profil={cfg.NextProfile} " +
                 $"directs={cfg.Direct.Count}" +
                 (failed.Count > 0 ? $" — REFUSÉS par Windows : {string.Join(", ", failed)}" : ""));
        Notify(failed.Count == 0
            ? "Raccourcis enregistrés."
            : $"Enregistré — refusé par Windows : {string.Join(", ", failed)}");
    }

    // ── AlwaysOnTop ───────────────────────────────────────────────────────
    // La fenêtre se lie à AlwaysOnTop via Topmost dans le XAML ; la persistance
    // passe par ce hook, quel que soit le déclencheur (bouton 📌 ou commande).
    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (_data.AlwaysOnTop == value) return;
        _data.AlwaysOnTop = value;
        Save();
    }

    [RelayCommand] private void ToggleAlwaysOnTop() => AlwaysOnTop = !AlwaysOnTop;

    // ── Afficher / masquer la fenêtre (raccourci global) ──────────────────
    public void ToggleMainWindow()
    {
        var w = WpfApp.Current?.MainWindow;
        if (w == null) return;

        if (w.IsVisible && w.WindowState != System.Windows.WindowState.Minimized)
        {
            w.Hide();
        }
        else
        {
            w.Show();
            w.WindowState = System.Windows.WindowState.Normal;
            w.Activate();
        }
    }

    // ── Géométrie de la fenêtre ───────────────────────────────────────────
    /// <summary>Restaure position/taille, en garantissant que la fenêtre reste à l'écran.</summary>
    public void ApplyWindowPlacement(System.Windows.Window w)
    {
        var p = _data.Window;
        if (p == null || p.Width <= 0 || p.Height <= 0) return;

        double width  = Math.Max(p.Width,  w.MinWidth);
        double height = Math.Max(p.Height, w.MinHeight);

        // Bornes de l'ensemble des écrans (multi-moniteurs inclus)
        double vl = System.Windows.SystemParameters.VirtualScreenLeft;
        double vt = System.Windows.SystemParameters.VirtualScreenTop;
        double vw = System.Windows.SystemParameters.VirtualScreenWidth;
        double vh = System.Windows.SystemParameters.VirtualScreenHeight;

        // Ne jamais dépasser l'écran : c'est ce qui faisait sortir le footer du cadre
        width  = Math.Min(width,  vw);
        height = Math.Min(height, vh);
        double left = Math.Clamp(p.Left, vl, vl + vw - width);
        double top  = Math.Clamp(p.Top,  vt, vt + vh - height);

        w.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        w.Left = left; w.Top = top; w.Width = width; w.Height = height;
    }

    /// <summary>Mémorise position/taille (état restauré, pas minimisé/maximisé).</summary>
    public void SaveWindowPlacement(System.Windows.Window w)
    {
        var r = w.WindowState == System.Windows.WindowState.Normal
            ? new System.Windows.Rect(w.Left, w.Top, w.Width, w.Height)
            : w.RestoreBounds;

        if (double.IsNaN(r.Width) || r.Width <= 0 || double.IsNaN(r.Height) || r.Height <= 0) return;

        var p = _data.Window;
        if (p != null && p.Left == r.Left && p.Top == r.Top
            && p.Width == r.Width && p.Height == r.Height) return; // rien de neuf

        _data.Window = new WindowPlacement { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height };
        Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void RebuildInitText()
    {
        InitiativeText = string.Join(" → ", Characters
            .Where(c => c.Enabled && c.IsLinked)
            .Select(c => string.IsNullOrWhiteSpace(c.ClassName)
                ? c.Name
                : $"{c.Name} ({c.ClassName})"));
    }

    public void Notify(string msg)
    {
        Status = msg;
        System.Threading.Tasks.Task.Delay(3500).ContinueWith(_ =>
            WpfApp.Current?.Dispatcher.Invoke(() =>
            { if (Status == msg) Status = "Prêt"; }));
    }

    private void Save()
    {
        _data.Characters = Characters.ToList();
        _data.Profiles   = Profiles.ToList();
        _store.Save(_data);
    }

    public void Dispose()
    {
        _watchTimer?.Dispose();
        Hotkeys.Dispose();
    }
}
