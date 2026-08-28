using System.Windows;
using System.Windows.Interop;
using Altechap.Helpers;
using Altechap.Models;

namespace Altechap.Services;

/// <summary>
/// Raccourcis globaux via RegisterHotKey / WM_HOTKEY.
/// 
/// Points clés :
/// - MOD_NOREPEAT absent → répétition native OS (scroll auto si touche maintenue)
/// - Debounce 30ms seulement — évite le double-tir sans bloquer la répétition
/// - Pause auto quand la fenêtre Altéchap est au premier plan
/// - Reprise auto quand elle perd le focus
/// - Hotkeys désactivés pendant capture de touche
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int ID_NEXT          = 1000;
    private const int ID_PREV          = 1001;
    private const int ID_NEXT_PROFILE  = 1002;
    private const int ID_TOGGLE_WINDOW = 1003;
    private const int ID_DIRECT_BASE   = 2000;

    // 30ms debounce = évite double-tir physique sans bloquer la répétition OS
    private const long DEBOUNCE_TICKS = 30 * TimeSpan.TicksPerMillisecond;

    private nint        _hwnd;
    private HwndSource? _source;

    private readonly List<int>             _registeredIds = new();
    private readonly Dictionary<int, Guid> _directMap     = new();
    private readonly List<string>          _failed        = new();
    private          HotkeyConfig?         _currentConfig;
    private          IList<Character>?     _currentChars;

    private const int ID_PROBE = 9999;

    /// <summary>
    /// Teste réellement chaque combinaison auprès de Windows (enregistrement puis
    /// libération immédiate) et retourne celles qui sont refusées — typiquement
    /// déjà réservées par une autre application.
    /// </summary>
    public IReadOnlyList<string> Validate(HotkeyConfig cfg)
    {
        if (_hwnd == nint.Zero) return Array.Empty<string>();

        // Libérer nos propres inscriptions, sinon on se refuserait nous-mêmes.
        Unregister();
        var bad = new List<string>();
        foreach (var hk in new[] { cfg.Next, cfg.Prev, cfg.NextProfile, cfg.ToggleWindow }
                     .Concat(cfg.Direct.Values)
                     .Where(h => !string.IsNullOrWhiteSpace(h))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (mods, vk) = Win32.ParseHotkey(hk);
            if (vk == 0) { bad.Add(hk); continue; }
            if (Win32.RegisterHotKey(_hwnd, ID_PROBE, mods, vk))
                Win32.UnregisterHotKey(_hwnd, ID_PROBE);
            else
                bad.Add(hk);
        }
        Reload();
        return bad;
    }

    private long _lastFire   = 0;
    private bool _paused     = false;  // true quand Altéchap est au premier plan
    private bool _capturing  = false;  // true pendant capture de touche

    public event Action?       OnNext;
    public event Action?       OnPrev;
    public event Action?       OnNextProfile;
    public event Action?       OnToggleWindow;
    public event Action<Guid>? OnDirect;

    // ── Attach à la fenêtre ─────────────────────────────────────────────
    public void Attach(Window window)
    {
        _hwnd   = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Activated/Deactivated donnent la réaction immédiate, mais ne sont pas
        // fiables à eux seuls : masquer une fenêtre active (✕ ou raccourci
        // afficher/masquer) ne garantit pas de Deactivated. On passe donc par
        // Reconcile, qui relit l'état réel au lieu de tenir un compteur.
        window.Activated        += (_, _) => Reconcile();
        window.Deactivated      += (_, _) => Reconcile();
        // Couvre explicitement le chemin Hide() : la visibilité change même
        // quand l'activation, elle, ne bouge pas.
        window.IsVisibleChanged += (_, _) => Reconcile();
    }

    // ── Appliquer config ────────────────────────────────────────────────
    public void Apply(HotkeyConfig cfg, IList<Character> chars)
    {
        _currentConfig = cfg;
        _currentChars  = chars;
        Reload();
    }

    /// <summary>Dernier ensemble d'échecs journalisé — évite de répéter la même ligne à chaque focus.</summary>
    private string _lastLoggedFailures = string.Empty;

    private void Reload()
    {
        if (_currentConfig == null) return;
        Unregister();
        _failed.Clear();
        if (_capturing) return; // aucune inscription pendant une capture de touche

        // Afficher/masquer Altéchap doit fonctionner MÊME quand Altéchap a le focus :
        // sinon le raccourci ne pourrait jamais servir à la masquer.
        Reg(ID_TOGGLE_WINDOW, _currentConfig.ToggleWindow);

        if (_paused) return; // les autres restent en pause tant que l'app est au premier plan

        Reg(ID_NEXT, _currentConfig.Next);
        Reg(ID_PREV, _currentConfig.Prev);
        Reg(ID_NEXT_PROFILE, _currentConfig.NextProfile);

        if (_currentChars != null)
        {
            int i = 0;
            foreach (var ch in _currentChars)
            {
                if (_currentConfig.Direct.TryGetValue(ch.Id, out var hk) && !string.IsNullOrWhiteSpace(hk))
                {
                    int id = ID_DIRECT_BASE + i;
                    Reg(id, hk);
                    _directMap[id] = ch.Id;
                }
                i++;
            }
        }

        LogFailures();
    }

    /// <summary>
    /// Un raccourci refusé par Windows ne se voyait nulle part : ni à l'écran, ni
    /// dans le journal. Un conflit apparu en cours de session (une autre
    /// application s'empare de la combinaison) restait donc indiagnosticable.
    /// On ne trace que les changements, Reload étant appelé à chaque focus.
    /// </summary>
    private void LogFailures()
    {
        var current = _failed.Count == 0 ? string.Empty : string.Join(", ", _failed);
        if (current == _lastLoggedFailures) return;
        _lastLoggedFailures = current;

        if (current.Length > 0)
            Log.Warn($"Raccourcis refusés par Windows : {current} — déjà pris par une autre application ?");
        else
            Log.Info("Raccourcis : toutes les combinaisons sont de nouveau inscrites.");
    }

    private void Reg(int id, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return;
        var (mods, vk) = Win32.ParseHotkey(hotkey);
        if (vk == 0) { _failed.Add(hotkey); return; }
        if (Win32.RegisterHotKey(_hwnd, id, mods, vk))
            _registeredIds.Add(id);
        else
            _failed.Add(hotkey);
    }

    private void Unregister()
    {
        foreach (var id in _registeredIds) Win32.UnregisterHotKey(_hwnd, id);
        _registeredIds.Clear();
        _directMap.Clear();
    }

    // ── Pause / Resume (auto via Activated/Deactivated) ────────────────
    private bool _autoSuspended = false; // vrai pendant ReorderTaskbar

    /// <summary>
    /// Suspend le Pause/Resume automatique le temps d'une opération (ex: reorder taskbar).
    /// Pendant ce temps, les hotkeys restent enregistrés même si Altéchap perd brièvement le focus.
    /// </summary>
    public void SuspendAutoToggle()
    {
        _autoSuspended = true;
        // S'assurer que les hotkeys sont actifs pendant l'opération
        if (_paused) { _paused = false; Reload(); }
    }

    /// <summary>Reprend le comportement automatique après une opération.</summary>
    public void ResumeAutoToggle()
    {
        _autoSuspended = false;
        // Remettre en état cohérent : si Altéchap a le focus → pause
        // Si non → actif
        if (_paused) { _paused = false; Reload(); }
    }

    /// <summary>
    /// Aligne l'état des inscriptions sur la réalité : les raccourcis de
    /// navigation sont en pause si et seulement si Altéchap est la fenêtre au
    /// premier plan. Idempotent — ne touche à rien quand l'état est déjà bon.
    ///
    /// C'est le filet de sécurité du service. Auparavant, <c>_paused</c> n'était
    /// écrit que par Activated/Deactivated ; masquer la fenêtre pendant qu'elle
    /// était active pouvait laisser le booléen bloqué à vrai, et les raccourcis
    /// désinscrits pour le reste de la session. Rien ne le corrigeait jamais, et
    /// il fallait rouvrir puis refermer Altéchap pour provoquer le Deactivated
    /// manquant. Appelé aussi à chaque scan (§ watcher), l'écart se répare seul.
    /// </summary>
    public void Reconcile()
    {
        // Pendant une capture de touche ou un réordonnancement, l'état voulu est
        // délibérément différent de l'état naturel : ne pas le contrarier.
        if (_autoSuspended || _capturing) return;
        if (_hwnd == nint.Zero) return;

        bool shouldPause = Win32.GetForegroundWindow() == _hwnd;
        if (shouldPause == _paused) return;

        _paused = shouldPause;
        Reload();
    }

    public void Pause()
    {
        if (_autoSuspended) return; // ignorer pendant une opération en cours
        if (_paused) return;
        _paused = true;
        Reload(); // ne garde que le raccourci afficher/masquer
    }

    public void Resume()
    {
        if (_autoSuspended) return; // ignorer pendant une opération en cours
        if (!_paused) return;
        _paused = false;
        Reload(); // réinscrire
    }

    /// <summary>Désactiver pendant la capture de touche.</summary>
    public void BeginCapture()
    {
        _capturing = true;
        Unregister();
    }

    /// <summary>Réactiver après capture.</summary>
    public void EndCapture()
    {
        _capturing = false;
        // Toujours recharger : même en pause, le raccourci afficher/masquer
        // doit être réinscrit.
        Reload();
    }

    // ── WndProc ─────────────────────────────────────────────────────────
    private nint WndProc(nint hw, int msg, nint wp, nint lp, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return nint.Zero;
        if (_capturing) return nint.Zero;

        // Debounce minimal
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastFire < DEBOUNCE_TICKS) return nint.Zero;
        _lastFire = now;

        int id = (int)wp;

        // Seul raccourci actif quand Altéchap est au premier plan
        if (id == ID_TOGGLE_WINDOW) { OnToggleWindow?.Invoke(); handled = true; return nint.Zero; }
        if (_paused) return nint.Zero;

        if      (id == ID_NEXT)                       { OnNext?.Invoke();              handled = true; }
        else if (id == ID_PREV)                       { OnPrev?.Invoke();              handled = true; }
        else if (id == ID_NEXT_PROFILE)               { OnNextProfile?.Invoke();       handled = true; }
        else if (_directMap.TryGetValue(id, out var g)){ OnDirect?.Invoke(g);    handled = true; }

        return nint.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
    }
}
