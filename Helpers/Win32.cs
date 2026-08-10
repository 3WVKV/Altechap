using System.Runtime.InteropServices;
using System.Text;

namespace Altechap.Helpers;

/// <summary>
/// API Win32 — uniquement gestion fenêtres. Zéro injection, zéro mémoire.
/// FocusFast = SetForegroundWindow uniquement (+ SW_RESTORE si minimisée).
/// EnsureOpaque = enlève WS_EX_TRANSPARENT et WS_EX_LAYERED de la fenêtre Altéchap.
/// </summary>
public static class Win32
{
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc fn, nint lp);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint h, StringBuilder sb, int n);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint h, StringBuilder sb, int n);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint h);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint h);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint h);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint h, out uint pid);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint h);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetProcessImageFileName(nint hProc, StringBuilder sb, int n);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint h);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint h, int n);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(nint h, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint h, int id);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    // ── SetWindowLong pour forcer l'opacité ───────────────────────────────
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // Index pour Extended Window Style
    private const int  GWL_EXSTYLE       = -20;
    // Styles à retirer
    private const long WS_EX_TRANSPARENT = 0x00000020L; // clics passent au travers
    private const long WS_EX_LAYERED     = 0x00080000L; // nécessaire pour transparent

    // Constantes hotkey
    public const uint MOD_ALT   = 0x0001;
    public const uint MOD_CTRL  = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN   = 0x0008;
    public const int  WM_HOTKEY = 0x0312;

    // ── API publique ──────────────────────────────────────────────────────

    public static string GetTitle(nint h)
    {
        int n = GetWindowTextLength(h); if (n == 0) return "";
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClass(nint h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, 256);
        return sb.ToString();
    }

    public static string GetProcessExeName(nint h)
    {
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return "";
            nint p = OpenProcess(0x1000, false, pid);
            if (p == nint.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                if (GetProcessImageFileName(p, sb, sb.Capacity) > 0)
                    return System.IO.Path.GetFileName(sb.ToString());
            }
            finally { CloseHandle(p); }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Focus minimal : SetForegroundWindow uniquement.
    /// SW_RESTORE uniquement si minimisée. Jamais de resize/move.
    /// </summary>
    public static void FocusFast(nint h)
    {
        if (!IsWindow(h)) return;
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }

    /// <summary>
    /// Restore une fenêtre SANS lui voler le focus et en la forçant EN ARRIÈRE-PLAN.
    /// Toggle ON : la fenêtre redevient visible/active mais reste derrière la fenêtre courante.
    ///
    /// Pourquoi deux appels ?
    ///   SW_SHOWNOACTIVATE (4) restaure sans activer, MAIS Windows remonte quand même
    ///   la fenêtre au premier plan si elle était minimisée.
    ///   SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE) la repousse immédiatement derrière
    ///   toutes les autres sans changer le focus.
    /// </summary>
    public static void ShowWithoutFocus(nint h)
    {
        if (!IsWindow(h)) return;
        if (IsIconic(h))
        {
            ShowWindow(h, 4); // SW_SHOWNOACTIVATE : restore sans activer
            // Repousser immédiatement en arrière-plan sans toucher au focus
            SetWindowPos(h, (nint)1, // HWND_BOTTOM = 1
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        // Si déjà visible et non minimisé : rien à faire
    }

    /// <summary>
    /// Minimise une fenêtre sans changer le focus courant (SW_MINIMIZE).
    /// Utilisé pour Toggle OFF : la fenêtre disparaît de l'écran.
    /// </summary>
    public static void MinimizeNoFocus(nint h)
    {
        if (!IsWindow(h)) return;
        if (!IsIconic(h))
            ShowWindow(h, 6); // SW_MINIMIZE
    }

    // ── Réordonnancement de la barre des tâches ──────────────────────────
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private const uint SWP_NOMOVE        = 0x0002;
    private const uint SWP_NOSIZE        = 0x0001;
    private const uint SWP_NOACTIVATE    = 0x0010;
    private const uint SWP_SHOWWINDOW    = 0x0040;
    private static readonly nint HWND_TOP = nint.Zero;

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint hWnd, int x, int y, int cx, int cy, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Réordonne les fenêtres Dofus dans la barre des tâches.
    /// Technique : minimise toutes les fenêtres cibles, puis les restaure
    /// dans l'ordre souhaité. L'ordre de restauration = ordre barre des tâches.
    /// Délai suffisant entre chaque pour que le shell enregistre l'ordre.
    /// </summary>
    public static void ReorderTaskbar(IList<nint> handles, nint restoreFocus)
    {
        var valid = handles.Where(h => IsWindow(h)).ToList();
        if (valid.Count == 0) return;

        // Étape 1 : mémoriser l'état (minimisée ou non) de chaque fenêtre
        var wasMinimized = valid.ToDictionary(h => h, h => IsIconic(h));

        // Étape 2 : minimiser TOUTES les fenêtres cibles dans l'ordre inverse
        // (on minimise en sens inverse pour ne pas perturber l'ordre actuel inutilement)
        for (int i = valid.Count - 1; i >= 0; i--)
        {
            ShowWindow(valid[i], 6); // SW_MINIMIZE
            System.Threading.Thread.Sleep(40);
        }
        System.Threading.Thread.Sleep(80);

        // Étape 3 : restaurer dans l'ordre SOUHAITÉ (1 → N)
        // La première restaurée se retrouve à gauche dans la taskbar
        foreach (var h in valid)
        {
            ShowWindow(h, 9); // SW_RESTORE
            SetForegroundWindow(h);
            System.Threading.Thread.Sleep(60);
        }
        System.Threading.Thread.Sleep(100);

        // Étape 4 : re-minimiser celles qui étaient minimisées avant
        foreach (var h in valid)
        {
            if (wasMinimized.TryGetValue(h, out bool wasMin) && wasMin)
                ShowWindow(h, 6);
        }

        // Étape 5 : redonner le focus à Altéchap
        if (restoreFocus != nint.Zero && IsWindow(restoreFocus))
        {
            System.Threading.Thread.Sleep(50);
            ShowWindow(restoreFocus, 9);
            SetForegroundWindow(restoreFocus);
        }
    }

    /// <summary>
    /// PATCH 1 — Garantit que la fenêtre Altéchap est 100% opaque et capture
    /// tous les événements souris. Enlève WS_EX_TRANSPARENT et WS_EX_LAYERED
    /// du style étendu Win32. À appeler dans OnSourceInitialized.
    /// </summary>
    public static void EnsureOpaque(nint hwnd)
    {
        try
        {
            long exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            // Retirer les flags qui rendent la fenêtre "fantôme"
            exStyle &= ~WS_EX_TRANSPARENT;
            exStyle &= ~WS_EX_LAYERED;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)exStyle);
        }
        catch { /* Si ça échoue, pas grave — les attributs XAML suffisent */ }
    }

    // ── Parser hotkey ──────────────────────────────────────────────────────
    private static readonly Dictionary<string, uint> _vk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"]=0x70,["F2"]=0x71,["F3"]=0x72,["F4"]=0x73,["F5"]=0x74,["F6"]=0x75,
        ["F7"]=0x76,["F8"]=0x77,["F9"]=0x78,["F10"]=0x79,["F11"]=0x7A,["F12"]=0x7B,
        ["LEFT"]=0x25,["UP"]=0x26,["RIGHT"]=0x27,["DOWN"]=0x28,
        ["HOME"]=0x24,["END"]=0x23,["PAGEUP"]=0x21,["PRIOR"]=0x21,
        ["PAGEDOWN"]=0x22,["NEXT"]=0x22,["INSERT"]=0x2D,["DELETE"]=0x2E,["DEL"]=0x2E,
        ["RETURN"]=0x0D,["ENTER"]=0x0D,["ESCAPE"]=0x1B,["ESC"]=0x1B,
        ["SPACE"]=0x20,["TAB"]=0x09,["BACKSPACE"]=0x08,
        ["NUMPAD0"]=0x60,["NUMPAD1"]=0x61,["NUMPAD2"]=0x62,["NUMPAD3"]=0x63,
        ["NUMPAD4"]=0x64,["NUMPAD5"]=0x65,["NUMPAD6"]=0x66,["NUMPAD7"]=0x67,
        ["NUMPAD8"]=0x68,["NUMPAD9"]=0x69,
    };

    public static (uint mods, uint vk) ParseHotkey(string s)
    {
        uint mods = 0, vk = 0;
        foreach (var raw in s.Split('+'))
        {
            var p = raw.Trim().ToUpperInvariant();
            switch (p)
            {
                case "ALT":                  mods |= MOD_ALT;   break;
                case "CTRL": case "CONTROL": mods |= MOD_CTRL;  break;
                case "SHIFT":                mods |= MOD_SHIFT; break;
                case "WIN":                  mods |= MOD_WIN;   break;
                default:
                    if (p.Length == 1) vk = (uint)char.ToUpper(p[0]);
                    else if (_vk.TryGetValue(p, out var v)) vk = v;
                    break;
            }
        }
        return (mods, vk);
    }
}
