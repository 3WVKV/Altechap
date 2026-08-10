using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Altechap.Models;
using Altechap.ViewModels;

namespace Altechap.Views;

public partial class HotkeySettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<DirectItem> _items = new();

    private enum Target { None, Next, Prev, NextProfile, ToggleWindow, Direct }
    private Target      _target = Target.None;
    private DirectItem? _targetItem;

    public HotkeySettingsWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        var cfg = _vm.GetHotkeys();
        TxtNext.Text        = cfg.Next;
        TxtPrev.Text        = cfg.Prev;
        TxtNextProfile.Text = cfg.NextProfile;
        TxtToggleWindow.Text = cfg.ToggleWindow;

        // Uniquement les fenêtres actives : la liste doit refléter ce qui est jouable
        // maintenant, pas l'historique de tout ce qui a été lancé un jour.
        // Les raccourcis des persos non listés sont préservés à l'enregistrement
        // (voir OK_Click) — ils ne sont pas effacés parce qu'ils sont absents d'ici.
        foreach (var ch in _vm.LinkedCharacters)
        {
            cfg.Direct.TryGetValue(ch.Id, out var hk);
            _items.Add(new DirectItem
            {
                CharId   = ch.Id,
                CharName = ch.Name,
                Hotkey   = hk ?? "",
                Linked   = true,
            });
        }
        LstDirect.ItemsSource = _items;
        if (_items.Count == 0) TxtNoDirect.Visibility = Visibility.Visible;

        PreviewKeyDown += OnKey;
        _vm.Hotkeys.BeginCapture();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Hotkeys.EndCapture();
        base.OnClosed(e);
    }

    private void CaptureNext_Click(object s, RoutedEventArgs e)        => StartCapture(Target.Next);
    private void CapturePrev_Click(object s, RoutedEventArgs e)        => StartCapture(Target.Prev);
    private void CaptureNextProfile_Click(object s, RoutedEventArgs e) => StartCapture(Target.NextProfile);
    private void CaptureToggleWindow_Click(object s, RoutedEventArgs e) => StartCapture(Target.ToggleWindow);
    private void CaptureDirect_Click(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.Tag is DirectItem item)
        { _targetItem = item; StartCapture(Target.Direct); }
    }

    private void StartCapture(Target t)
    {
        _target = t;
        TxtCaptured.Text = "…";
        CaptureOverlay.Visibility = Visibility.Visible;
        Focus();
    }

    private void CancelCapture_Click(object s, RoutedEventArgs e) => EndCapture(null);

    // ── Suppression des raccourcis ────────────────────────────────────────
    private void ClearNext_Click(object s, RoutedEventArgs e)
        { TxtNext.Text = string.Empty; }
    private void ClearPrev_Click(object s, RoutedEventArgs e)
        { TxtPrev.Text = string.Empty; }
    private void ClearNextProfile_Click(object s, RoutedEventArgs e)
        { TxtNextProfile.Text = string.Empty; }
    private void ClearToggleWindow_Click(object s, RoutedEventArgs e)
        { TxtToggleWindow.Text = string.Empty; }
    private void ClearDirect_Click(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.Tag is DirectItem item)
        {
            item.Hotkey = string.Empty;
            LstDirect.Items.Refresh();
        }
    }

    private void EndCapture(string? hotkey)
    {
        if (hotkey != null)
        {
            switch (_target)
            {
                case Target.Next:        TxtNext.Text        = hotkey; break;
                case Target.Prev:        TxtPrev.Text        = hotkey; break;
                case Target.NextProfile:  TxtNextProfile.Text  = hotkey; break;
                case Target.ToggleWindow: TxtToggleWindow.Text = hotkey; break;
                case Target.Direct:
                    if (_targetItem != null) { _targetItem.Hotkey = hotkey; LstDirect.Items.Refresh(); }
                    break;
            }
        }
        _target     = Target.None;
        _targetItem = null;
        CaptureOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnKey(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (_target == Target.None) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var mods  = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var keyName = key switch
        {
            Key.Left   => "Left",
            Key.Right  => "Right",
            Key.Up     => "Up",
            Key.Down   => "Down",
            Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Space  => "Space",
            Key.Tab    => "Tab",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home   => "Home",
            Key.End    => "End",
            Key.Prior  => "PageUp",
            Key.Next   => "PageDown",
            >= Key.F1 and <= Key.F12 => key.ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Numpad" + (int)(key - Key.NumPad0),
            _ => key.ToString()
        };
        parts.Add(keyName);

        var hotkey = string.Join("+", parts);
        TxtCaptured.Text = hotkey;
        EndCapture(hotkey);
    }

    private void OK_Click(object s, RoutedEventArgs e)
    {
        var cfg = new HotkeyConfig
        {
            Next        = TxtNext.Text.Trim(),
            Prev        = TxtPrev.Text.Trim(),
            NextProfile  = TxtNextProfile.Text.Trim(),
            ToggleWindow = TxtToggleWindow.Text.Trim()
        };

        // Repartir de l'existant : on ne perd pas les raccourcis de persos
        // qui ne seraient pas affichés dans cette liste.
        foreach (var kv in _vm.GetHotkeys().Direct)
            cfg.Direct[kv.Key] = kv.Value;

        foreach (var item in _items)
        {
            if (string.IsNullOrWhiteSpace(item.Hotkey)) cfg.Direct.Remove(item.CharId);
            else cfg.Direct[item.CharId] = item.Hotkey.Trim();
        }

        // Refuser deux fois la même combinaison : sinon un seul des deux marche
        // et l'utilisateur ne comprend pas pourquoi.
        var dup = new[] { cfg.Next, cfg.Prev, cfg.NextProfile, cfg.ToggleWindow }
            .Concat(cfg.Direct.Values)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            MessageBox.Show(this,
                $"La combinaison « {dup.Key} » est utilisée par plusieurs actions.\n" +
                "Chaque raccourci doit être unique.",
                "Conflit de raccourci", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.ApplyHotkeys(cfg);
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => Close();
}

public class DirectItem
{
    public Guid   CharId   { get; set; }
    public string CharName { get; set; } = "";
    public string Hotkey   { get; set; } = "";
    public bool   Linked   { get; set; }
    public string StateText => Linked ? "" : "hors ligne";
}
