using System.Runtime.InteropServices;

namespace Altechap.Helpers;

/// <summary>
/// Réordonnancement de la barre des tâches Windows.
///
/// Stratégie : AppUserModelID unique par fenêtre Dofus → brise le groupage.
/// L'ordre taskbar est ancré par minimize+restore séquentiel.
///
/// Écran noir WPF : on masque Altéchap pendant toute l'opération puis on la
/// réaffiche. Les WM_NCACTIVATE de SetForegroundWindow ne peuvent pas corrompre
/// la surface de rendu d'une fenêtre masquée. C'est l'approche standard des
/// outils de gestion de taskbar professionnels.
/// </summary>
public static class TaskbarOrder
{
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint h, int n);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        nint hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint   pszVal;
    }

    private const int    SW_HIDE    = 0;
    private const int    SW_RESTORE = 9;
    private const int    SW_MINIMIZE = 6;
    private const ushort VT_LPWSTR  = 31;

    private static readonly PropertyKey PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly Guid IID_IPropertyStore =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>
    /// Appeler au démarrage : donne à Altéchap son propre slot taskbar,
    /// séparé des fenêtres Dofus.
    /// </summary>
    public static void SetAltechapProcessAumid()
        => SetCurrentProcessExplicitAppUserModelID("Altechap.Main.3WVKV");

    /// <summary>
    /// Réordonne les fenêtres Dofus dans la taskbar.
    /// handles[0] = position la plus à gauche = premier dans l'ordre d'initiative.
    /// </summary>
    // Jeton unique par session : évite qu'un AUMID d'une session précédente
    // (fenêtres Dofus survivant à un redémarrage d'Altéchap) soit réutilisé tel quel.
    private static readonly string SessionToken = Guid.NewGuid().ToString("N")[..6];
    private static int _runCounter;

    // Un seul réordonnancement à la fois : deux séquences hide/minimize/restore
    // entrelacées produisent un ordre aléatoire.
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task ReorderAsync(IList<nint> handles, nint altechapHwnd)
    {
        var valid = handles.Where(h => IsWindow(h)).ToList();
        if (valid.Count < 2) return;

        if (!await _gate.WaitAsync(0))
        {
            Services.Log.Warn("Réordonnancement déjà en cours — demande ignorée.");
            return;
        }

        int run = System.Threading.Interlocked.Increment(ref _runCounter);
        Services.Log.Info($"Réordonnancement #{run} — {valid.Count} fenêtre(s).");

        // ── Étape 1 : masquer Altéchap — protège le rendu WPF ───────────────
        // Sans ça, les WM_NCACTIVATE envoyés par SetForegroundWindow(dofus)
        // corrompent la surface DirectX de WPF → écran noir.
        bool hasAltechap = altechapHwnd != nint.Zero && IsWindow(altechapHwnd);
        if (hasAltechap)
            ShowWindow(altechapHwnd, SW_HIDE);

        await Task.Delay(80);

        try
        {
            // ── Étape 2 : AUMID unique → brise le groupage Windows ───────────
            //
            // Le numéro de run fait partie de l'AUMID : c'est le CHANGEMENT
            // d'AUMID qui pousse Explorer à détruire puis recréer le bouton de
            // taskbar (donc à le replacer). Avec un identifiant figé
            // (« Slot003 »), une fenêtre qui retombait sur le même index au
            // 2e appel gardait exactement son ancien AUMID → aucun événement,
            // aucun déplacement, et un ordre partiellement faux.
            //
            // Application séquentielle avec une pause : Explorer traite les
            // recréations dans l'ordre reçu, c'est lui qui fixe l'ordre final.
            for (int i = 0; i < valid.Count; i++)
            {
                if (!SetWindowAumid(valid[i], $"Altechap.Dofus.{SessionToken}.R{run}.S{i:D3}"))
                    Services.Log.Warn($"Slot {i} : AUMID refusé pour la fenêtre {valid[i]}.");
                await Task.Delay(40);
            }

            await Task.Delay(150);

            // ── Étape 3 : mémoriser l'état minimisé ──────────────────────────
            var wasMin = valid.ToDictionary(h => h, IsIconic);

            // ── Étape 4 : tout minimiser ──────────────────────────────────────
            foreach (var h in valid)
                if (!IsIconic(h)) ShowWindow(h, SW_MINIMIZE);

            await Task.Delay(200);

            // ── Étape 5 : restaurer dans l'ordre ─────────────────────────────
            // valid[0] en premier = ancre à gauche dans la taskbar
            foreach (var h in valid)
            {
                ShowWindow(h, SW_RESTORE);
                // Altéchap est masquée à cet instant : elle n'a plus les droits
                // de premier plan, donc cet appel échoue souvent. Sans effet sur
                // l'ordre (c'est l'AUMID qui le fixe), on le garde uniquement
                // pour l'état d'activation des fenêtres.
                SetForegroundWindow(h);
                await Task.Delay(110);
            }

            await Task.Delay(120);

            // ── Étape 6 : remettre en minimisé ceux qui l'étaient ────────────
            foreach (var h in valid)
                if (wasMin[h]) ShowWindow(h, SW_MINIMIZE);

            await Task.Delay(80);
        }
        finally
        {
            // ── Étape 7 : toujours réafficher Altéchap, même en cas d'erreur ─
            if (hasAltechap)
            {
                ShowWindow(altechapHwnd, SW_RESTORE);
                SetForegroundWindow(altechapHwnd);
            }
            Services.Log.Info($"Réordonnancement #{run} terminé.");
            _gate.Release();
        }
    }

    private static bool SetWindowAumid(nint hwnd, string aumid)
    {
        try
        {
            var iid = IID_IPropertyStore;
            int hr  = SHGetPropertyStoreForWindow(hwnd, ref iid, out object ppv);
            if (hr != 0) return false;

            var store = (IPropertyStore)ppv;
            var key   = PKEY_AppUserModel_ID;
            nint ptr  = Marshal.StringToCoTaskMemUni(aumid);
            try
            {
                var pv = new PropVariant { vt = VT_LPWSTR, pszVal = ptr };
                store.SetValue(ref key, ref pv);
                store.Commit();
                return true;
            }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        catch (Exception ex)
        {
            Altechap.Services.Log.Warn($"AUMID non appliqué à la fenêtre {hwnd}", ex);
            return false;
        }
    }
}
