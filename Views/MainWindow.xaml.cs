using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Altechap.Helpers;
using Altechap.Models;
using Altechap.Services;
using Altechap.ViewModels;

using WpfMsgBox = System.Windows.MessageBox;

namespace Altechap.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _profileEditing    = false;
    private bool _updateCheckRunning = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Click sur card = focus (via DragDropBehavior.ItemClicked)
        DragDropBehavior.AddItemClickedHandler(CharList, OnCardClicked);

        TxtVersion.Text = BuildInfo.Display;
        TxtVersion.ToolTip = "Cliquer pour vérifier les mises à jour";

        // NOTE: Topmost est lié à AlwaysOnTop (bouton 📌 dans l'en-tête).
        // On n'appelle PAS Activate() automatiquement — ça bloquerait les dialogs
        // et empiéterait sur le focus quand l'utilisateur clique une autre app.
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // ── Opacité Win32 totale + enregistrement du HWND ──────────────
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.EnsureOpaque(hwnd);
        _vm.SetSelfHwnd(hwnd);  // pour ReorderTaskbar (restaurer focus après)

        _vm.Init(this);
        _vm.ApplyWindowPlacement(this); // après Init : la config est chargée

        if (_vm.CheckUpdatesEnabled) _ = CheckUpdatesAtStartupAsync();
    }

    // ── Mises à jour ────────────────────────────────────────────────────
    // Laisser le démarrage se terminer avant de toucher au réseau : le premier
    // scan des fenêtres et l'enregistrement des raccourcis passent d'abord.
    private async Task CheckUpdatesAtStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        await CheckUpdatesAsync(silent: true);
    }

    /// <summary>
    /// Interroge GitHub puis propose l'installation. En mode <paramref name="silent"/>
    /// (démarrage), rien ne s'affiche s'il n'y a pas de nouveauté ou si la
    /// version a déjà été refusée.
    /// </summary>
    public async Task CheckUpdatesAsync(bool silent)
    {
        if (_updateCheckRunning) return;
        _updateCheckRunning = true;
        try
        {
            if (!silent) _vm.Notify("Recherche de mise à jour…");

            var info = await UpdateService.CheckAsync();

            if (info == null)
            {
                if (!silent)
                {
                    _vm.Notify($"Altéchap est à jour (v{UpdateService.Current}).");
                    WpfMsgBox.Show(this,
                        $"Vous utilisez déjà la dernière version disponible (v{UpdateService.Current}).",
                        "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var version = info.Version.ToString();
            if (silent && _vm.IsUpdateSkipped(version)) return;

            // La fenêtre peut être réduite dans le tray : sans ça, le dialogue
            // s'ouvrirait derrière tout le reste.
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();

            var dlg = new UpdateDialog(info) { Owner = this };
            dlg.ShowDialog();

            if (dlg.Skipped) _vm.SkipUpdate(version);
            else if (dlg.DialogResult != true) _vm.Notify($"Mise à jour v{version} disponible.");
        }
        finally { _updateCheckRunning = false; }
    }

    private void Version_Click(object sender, MouseButtonEventArgs e) => _ = CheckUpdatesAsync(silent: false);

    // ── PATCH 6 : Click sur card = focus, pas drag ─────────────────────
    private void OnCardClicked(object sender, ItemClickedEventArgs e)
    {
        _vm.FocusCharacter(e.Character);
    }

    // ── Fermeture → Tray ────────────────────────────────────────────────
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        _vm.SaveWindowPlacement(this); // avant Hide() : après, la géométrie n'est plus fiable
        Hide();
    }

    // ── Footer navigation ────────────────────────────────────────────────
    private void Prev_Click(object s, RoutedEventArgs e) => _vm.NavigatePrev();
    private void Next_Click(object s, RoutedEventArgs e) => _vm.NavigateNext();

    // ── Toggle ON/OFF ────────────────────────────────────────────────────
    private void Toggle_Click(object s, RoutedEventArgs e)
    {
        e.Handled = true; // stoppe toute propagation vers DragDropBehavior
        if (Ch(s) is Character ch)
            _vm.ToggleEnabled(ch);
    }

    // ── Boutons card ──────────────────────────────────────────────────────
    private void Pick_Click(object s, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Ch(s) is not Character ch) return;
        var windows = WindowScanner.ScanAll();
        if (!windows.Any())
        {
            WpfMsgBox.Show("Aucune fenêtre Dofus détectée.\nVérifiez que le client est lancé.",
                "Non trouvé", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new WindowPickerDialog(windows) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Chosen != null)
            _vm.LinkManual(ch, dlg.Chosen.Handle, dlg.Chosen.RawTitle);
    }

    private void Edit_Click(object s, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Ch(s) is not Character ch) return;
        var dlg = new CharacterEditDialog(ch) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.SaveCharacter();
    }

    private void Del_Click(object s, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Ch(s) is not Character ch) return;
        if (WpfMsgBox.Show($"Supprimer {ch.Name} ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.RemoveCharacterCommand.Execute(ch);
    }

    private void Hotkeys_Click(object s, RoutedEventArgs e)
        => new HotkeySettingsWindow(_vm) { Owner = this }.ShowDialog();

    // ── PATCH 5 : Renommage profil inline ───────────────────────────────
    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveProfile == null) return;
        TxtProfileName.Text = _vm.ActiveProfile.Name;
        ProfileDisplay.Visibility = Visibility.Collapsed;
        ProfileEdit.Visibility    = Visibility.Visible;
        TxtProfileName.Focus();
        TxtProfileName.SelectAll();
        _profileEditing = true;
    }

    private void ProfileDisplay_Click(object sender, MouseButtonEventArgs e)
    {
        // Gardé pour compatibilité double-clic (optionnel)
        if (e.ClickCount >= 2) RenameProfile_Click(sender, e);
    }

    private void ProfileName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) CommitProfileRename();
        else if (e.Key == Key.Escape) CancelProfileRename();
    }

    private void ProfileName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_profileEditing) CommitProfileRename();
    }

    private void CommitProfileRename()
    {
        _profileEditing = false;
        var name = TxtProfileName.Text.Trim();
        if (!string.IsNullOrEmpty(name) && _vm.ActiveProfile != null)
            _vm.RenameProfile(_vm.ActiveProfile, name);
        ProfileDisplay.Visibility = Visibility.Visible;
        ProfileEdit.Visibility    = Visibility.Collapsed;
    }

    private void CancelProfileRename()
    {
        _profileEditing = false;
        ProfileDisplay.Visibility = Visibility.Visible;
        ProfileEdit.Visibility    = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Appelé par App.OnExit : « Quitter » depuis le tray ne passe pas par Closing.</summary>
    public void PersistPlacement()
    {
        try { _vm.SaveWindowPlacement(this); } catch { }
    }

    // ── Scroll universel — fonctionne depuis n'importe où dans la zone liste ─
    private void ListArea_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (CharScrollViewer == null) return;
        CharScrollViewer.ScrollToVerticalOffset(
            CharScrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true; // stoppe la propagation vers le ListBox
    }

    private static Character? Ch(object s) => (s as FrameworkElement)?.DataContext as Character;
}
