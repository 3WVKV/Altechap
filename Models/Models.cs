using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Altechap.Models;

// ════════════════════════════════════════
//  Character — 100% persisté sauf Handle
// ════════════════════════════════════════
public class Character : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

    private int _classId = 0;
    public int ClassId
    {
        get => _classId;
        set
        {
            _classId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClassName));
            OnPropertyChanged(nameof(ClassColor));
            OnPropertyChanged(nameof(ClassIconPath));
            OnPropertyChanged(nameof(ClassInitial));
        }
    }

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

    public string MatchPattern { get; set; } = string.Empty;
    public bool UseRegex { get; set; } = false;

    // Runtime — non persisté
    [JsonIgnore] private nint _handle;
    [JsonIgnore] public nint Handle
    {
        get => _handle;
        set { _handle = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLinked)); }
    }

    [JsonIgnore] private string _rawTitle = string.Empty;
    [JsonIgnore] public string RawTitle
    {
        get => _rawTitle;
        set { _rawTitle = value; OnPropertyChanged(); }
    }

    [JsonIgnore] private bool _isDragging;
    /// <summary>Vrai pendant que la carte est déplacée (retour visuel du drag &amp; drop).</summary>
    [JsonIgnore] public bool IsDragging
    {
        get => _isDragging;
        set { if (_isDragging == value) return; _isDragging = value; OnPropertyChanged(); }
    }

    [JsonIgnore] public bool IsLinked => Handle != nint.Zero;
    [JsonIgnore] public string ClassName     => ClassDefs.NameFromId(_classId) ?? string.Empty;
    [JsonIgnore] public string ClassColor    => ClassDefs.GetColor(_classId);
    [JsonIgnore] public string ClassIconPath => ClassDefs.GetIconPath(_classId);
    [JsonIgnore] public string ClassInitial  => _classId > 0
        ? (ClassDefs.NameFromId(_classId)?[..1].ToUpper() ?? "?") : "?";

    /// <summary>Appelé quand l'icône PNG est téléchargée — rafraîchit le binding.</summary>
    public void NotifyIconChanged() => OnPropertyChanged(nameof(ClassIconPath));
}

// ════════════════════════════════════════
//  ClassDefs — mapping officiel dofusdb.fr
//  URL : https://api.dofusdb.fr/img/breeds/symbol_X.png
// ════════════════════════════════════════
public static class ClassDefs
{
    private static readonly Dictionary<int, (string name, string color)> _byId = new()
    {
        [1]  = ("Féca",       "#3498DB"),
        [2]  = ("Osamodas",   "#16A085"),
        [3]  = ("Enutrof",    "#E67E22"),
        [4]  = ("Sram",       "#8E44AD"),
        [5]  = ("Xélor",      "#2980B9"),
        [6]  = ("Ecaflip",    "#F39C12"),
        [7]  = ("Eniripsa",   "#1ABC9C"),
        [8]  = ("Iop",        "#E74C3C"),
        [9]  = ("Crâ",        "#27AE60"),
        [10] = ("Sadida",     "#2ECC71"),
        [11] = ("Sacrieur",   "#C0392B"),
        [12] = ("Pandawa",    "#3498DB"),
        [13] = ("Roublard",   "#7F8C8D"),
        [14] = ("Zobal",      "#9B59B6"),
        [15] = ("Steamer",    "#95A5A6"),
        [16] = ("Éliotrope",  "#00BCD4"),
        [17] = ("Huppermage", "#673AB7"),
        [18] = ("Ouginak",    "#D35400"),
        // 19 n'existe pas
        [20] = ("Forgelance", "#455A64"),
    };

    private static readonly (string alias, int id)[] _aliases =
    {
        ("feca", 1), ("féca", 1),
        ("osamodas", 2), ("osa", 2),
        ("enutrof", 3), ("enu", 3),
        ("sram", 4),
        ("xélor", 5), ("xelor", 5), ("xel", 5),
        ("ecaflip", 6), ("eca", 6),
        ("eniripsa", 7), ("eni", 7),
        ("iop", 8),
        ("crâ", 9), ("cra", 9),
        ("sadida", 10), ("sadi", 10),
        ("sacrieur", 11), ("sacri", 11),
        ("pandawa", 12), ("panda", 12),
        ("roublard", 13), ("roub", 13), ("rogue", 13),
        ("zobal", 14),
        ("steamer", 15), ("steam", 15),
        ("éliotrope", 16), ("eliotrope", 16), ("elio", 16),
        ("huppermage", 17), ("hupper", 17),
        ("ouginak", 18), ("ougi", 18),
        ("forgelance", 20), ("forge", 20),
    };

    private static readonly string CacheDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Altechap", "icons");

    public static int IdFromTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        foreach (var (alias, id) in _aliases)
            if (text.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return id;
        return 0;
    }

    public static string? NameFromId(int id)
        => _byId.TryGetValue(id, out var v) ? v.name : null;

    public static string GetColor(int id)
        => _byId.TryGetValue(id, out var v) ? v.color : "#45475A";

    public static string GetIconPath(int id)
    {
        if (id <= 0) return string.Empty;
        return System.IO.Path.Combine(CacheDir, $"symbol_{id}.png");
    }

    public static string GetIconUrl(int id)
        => $"https://api.dofusdb.fr/img/breeds/symbol_{id}.png";

    public static bool IconCached(int id)
        => id > 0 && System.IO.File.Exists(GetIconPath(id));

    public static IReadOnlyList<(int id, string name, string color)> All =>
        _byId.OrderBy(k => k.Key).Select(k => (k.Key, k.Value.name, k.Value.color)).ToList();
}

// ════════════════════════════════════════
//  CombatProfile
// ════════════════════════════════════════
public class CombatProfile : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = "Profil";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public List<Guid> Order { get; set; } = new();

    /// <summary>
    /// Personnages désactivés dans ce profil (absent = actif).
    /// <c>null</c> = profil antérieur à la fonctionnalité, jamais configuré :
    /// il sera initialisé depuis l'état global des persos au premier chargement.
    /// </summary>
    public List<Guid>? Disabled { get; set; }
}

// ════════════════════════════════════════
//  HotkeyConfig
// ════════════════════════════════════════
public class HotkeyConfig
{
    public string Next         { get; set; } = "Alt+Right";
    public string Prev         { get; set; } = "Alt+Left";
    public string NextProfile  { get; set; } = ""; // raccourci changement de profil
    public string ToggleWindow { get; set; } = ""; // afficher / masquer Altéchap
    public Dictionary<Guid, string> Direct { get; set; } = new();
}

// ════════════════════════════════════════
//  AppData — tout persisté
// ════════════════════════════════════════
public class AppData
{
    public List<Character> Characters { get; set; } = new();
    public List<CombatProfile> Profiles { get; set; } = new();
    public Guid ActiveProfile { get; set; }
    public HotkeyConfig Hotkeys { get; set; } = new();
    public bool AlwaysOnTop { get; set; } = true; // épinglé par défaut
    public int RefreshSeconds { get; set; } = 2;  // période du watcher, borné 1-60s

    /// <summary>Position/taille de la fenêtre principale. null = jamais enregistrée.</summary>
    public WindowPlacement? Window { get; set; }

    // ── Mises à jour ──────────────────────────────────────────────────────
    /// <summary>Interroger GitHub au démarrage. Désactivable pour un poste hors ligne.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>
    /// Version explicitement refusée par l'utilisateur (« Ignorer cette version »).
    /// On ne la repropose plus au démarrage ; une version ultérieure, si.
    /// </summary>
    public string? SkippedUpdate { get; set; }
}

// ════════════════════════════════════════
//  WindowPlacement — géométrie fenêtre
// ════════════════════════════════════════
public sealed class WindowPlacement
{
    public double Left   { get; set; }
    public double Top    { get; set; }
    public double Width  { get; set; }
    public double Height { get; set; }
}

// ════════════════════════════════════════
//  DofusWindow — snapshot runtime
// ════════════════════════════════════════
public sealed record DofusWindow(
    nint Handle,
    string RawTitle,
    string CharName,
    int ClassId,
    string ProcessExe
);
