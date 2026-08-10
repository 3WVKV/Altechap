using System.Text.RegularExpressions;
using Altechap.Helpers;
using Altechap.Models;

namespace Altechap.Services;

/// <summary>
/// Détection fenêtres Dofus.
/// Formats de titres connus :
///   - Nouveau client : "Pseudo - Classe - 3.4.18.19 - Release"
///   - Ancien client  : "Pseudo - Classe - Dofus 3.4" ou "Dofus (Classe)"
/// </summary>
public static class WindowScanner
{
    private const string UnityClass = "UnityWndClass";

    private static readonly Regex ProcRx = new(
        "^dofus", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Séparateur : tiret ASCII (U+002D) ou en-dash (U+2013)
    // Nouveau format : "Pseudo - Classe - X.Y.Z..." (version numérique après la classe)
    private static readonly Regex NewFmtRx = new(
        @"^(?<n>[^\-\u2013]+?)\s*[\-\u2013]\s*(?<cls>[^\-\u2013]+?)\s*[\-\u2013]\s*\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Ancien format avec le mot "Dofus" : "Pseudo - Classe - Dofus X.Y" ou "Pseudo - Dofus"
    private static readonly Regex OldFmtRx = new(
        @"^(?<n>[^\-\u2013]+?)\s*[\-\u2013]\s*(?<cls>[^\-\u2013]+?)\s*[\-\u2013]\s*Dofus",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Format très court : "Dofus (Classe)" ou "Dofus"
    private static readonly Regex LoadingRx = new(
        @"^Dofus(\s*\((?<cls>[^)]+)\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<DofusWindow> ScanAll()
    {
        var results = new List<DofusWindow>(8);
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;

            var cls = Win32.GetClass(hWnd);
            bool isUnity = cls.Equals(UnityClass, StringComparison.OrdinalIgnoreCase);
            if (!isUnity)
            {
                var proc = Win32.GetProcessExeName(hWnd);
                if (!ProcRx.IsMatch(proc)) return true;
            }

            var title = Win32.GetTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            var (charName, classId) = ParseTitle(title);
            results.Add(new DofusWindow(hWnd, title, charName, classId, ""));
            return true;
        }, nint.Zero);
        return results;
    }

    public static (string name, int classId) ParseTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return ("", 0);
        var t = title.Trim();

        // ── Nouveau format : "Pseudo - Classe - 3.4.18.19 - Release" ──────────
        var m = NewFmtRx.Match(t);
        if (m.Success)
        {
            var name = m.Groups["n"].Value.Trim();
            var cls  = m.Groups["cls"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return (name, ClassDefs.IdFromTitle(cls));
        }

        // ── Ancien format : "Pseudo - Classe - Dofus X.Y" ────────────────────
        var m2 = OldFmtRx.Match(t);
        if (m2.Success)
        {
            var name = m2.Groups["n"].Value.Trim();
            var cls  = m2.Groups["cls"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !cls.Equals("dofus", StringComparison.OrdinalIgnoreCase))
                return (name, ClassDefs.IdFromTitle(cls));
        }

        // ── Écran de chargement : "Dofus" ou "Dofus (Pandawa)" ───────────────
        var mLoad = LoadingRx.Match(t);
        if (mLoad.Success)
        {
            var cls = mLoad.Groups["cls"].Value.Trim();
            return ("Dofus", ClassDefs.IdFromTitle(cls));
        }

        // Fallback : titre brut tronqué
        return (t.Length > 60 ? t[..60] : t, ClassDefs.IdFromTitle(t));
    }

    public static string ExtractName(string title) => ParseTitle(title).name;

    public static void Focus(Character ch)
    {
        if (ch.Handle == nint.Zero) return;
        if (!Win32.IsWindow(ch.Handle)) { ch.Handle = nint.Zero; return; }
        Win32.FocusFast(ch.Handle);
    }
}
