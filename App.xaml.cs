using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Altechap.Services;
using Altechap.Views;
using Altechap.ViewModels;

namespace Altechap;

public partial class App : Application
{
    private TaskbarIcon? _tray;

    // ── Instance unique ───────────────────────────────────────────────────
    // Deux Altéchap simultanés se disputent les mêmes raccourcis globaux
    // (le second RegisterHotKey échoue) et s'écrasent mutuellement config.json.
    private const string MutexName  = @"Local\Altechap.SingleInstance";
    private const string SignalName = @"Local\Altechap.ShowWindow";

    private Mutex?            _instanceMutex;
    private EventWaitHandle?  _showSignal;
    private CancellationTokenSource? _signalCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Déjà lancé : on réveille l'instance existante et on se retire.
            Log.Info("Instance déjà en cours — réveil de la fenêtre existante, arrêt de ce processus.");
            try
            {
                if (EventWaitHandle.TryOpenExisting(SignalName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch (Exception ex) { Log.Warn("Réveil de l'instance existante impossible", ex); }

            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        StartShowSignalListener();

        Log.Info($"──────── Démarrage d'Altéchap {BuildInfo.Display} ────────");

        // Donner à Altéchap son propre slot taskbar dès le démarrage
        // → empêche le groupage avec les fenêtres Dofus
        Altechap.Helpers.TaskbarOrder.SetAltechapProcessAumid();
        _tray = (TaskbarIcon)FindResource("TrayIcon");
    }

    /// <summary>Écoute les demandes d'ouverture émises par un second lancement.</summary>
    private void StartShowSignalListener()
    {
        try
        {
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            _signalCts  = new CancellationTokenSource();
            var token   = _signalCts.Token;

            var thread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (!_showSignal.WaitOne(500)) continue;
                    if (token.IsCancellationRequested) return;
                    Dispatcher.Invoke(ShowMain);
                }
            })
            { IsBackground = true, Name = "Altechap.ShowSignal" };
            thread.Start();
        }
        catch (Exception ex) { Log.Warn("Écoute du signal d'ouverture indisponible", ex); }
    }

    private void Tray_DoubleClick(object sender, RoutedEventArgs e) => ShowMain();

    /// <summary>
    /// Clic droit sur l'icône tray → menu contextuel dynamique
    /// (profils + réorganiser + ouvrir + quitter)
    /// </summary>
    private void Tray_RightClick(object sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm == null) return;

        var menu = new ContextMenu();

        // ── En-tête : nom du profil actif ─────────────────────────────
        menu.Items.Add(new MenuItem
        {
            Header    = $"Altéchap  —  {vm.ActiveProfile?.Name ?? "…"}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        });
        menu.Items.Add(new Separator());

        // ── Choix du profil ────────────────────────────────────────────
        var profilesSub = new MenuItem { Header = "🗂  Profil…" };
        foreach (var p in vm.Profiles)
        {
            var pCopy = p; // capture locale pour le lambda
            var item  = new MenuItem
            {
                Header    = pCopy.Name,
                IsChecked = (pCopy == vm.ActiveProfile),
                IsCheckable = false
            };
            if (pCopy == vm.ActiveProfile)
                item.FontWeight = FontWeights.SemiBold;
            item.Click += (_, _) => vm.SwitchToProfile(pCopy.Id);
            profilesSub.Items.Add(item);
        }
        menu.Items.Add(profilesSub);
        menu.Items.Add(new Separator());

        // ── Réorganiser taskbar ─────────────────────────────────────────
        var reorderItem = new MenuItem { Header = "⊞  Réorganiser la barre des tâches" };
        reorderItem.Click += (_, _) => vm.ReorderTaskbarCommand.Execute(null);
        menu.Items.Add(reorderItem);
        menu.Items.Add(new Separator());

        // ── Ouvrir / Quitter ───────────────────────────────────────────
        var openItem = new MenuItem { Header = "↗  Ouvrir Altéchap" };
        openItem.Click += (_, _) => ShowMain();
        menu.Items.Add(openItem);

        var updateItem = new MenuItem { Header = "⟳  Vérifier les mises à jour" };
        updateItem.Click += (_, _) =>
        {
            if (MainWindow is Altechap.Views.MainWindow w)
                _ = w.CheckUpdatesAsync(silent: false);
        };
        menu.Items.Add(updateItem);

        var quitItem = new MenuItem { Header = "✕  Quitter" };
        quitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(quitItem);

        // Afficher le menu
        if (_tray != null)
        {
            _tray.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }

    private void ShowMain()
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    private MainViewModel? GetVm()
        => (MainWindow as Altechap.Views.MainWindow) is { } w
            ? w.DataContext as MainViewModel
            : null;

    protected override void OnExit(ExitEventArgs e)
    {
        (MainWindow as Altechap.Views.MainWindow)?.PersistPlacement();
        _signalCts?.Cancel();
        _showSignal?.Dispose();
        _tray?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        if (_instanceMutex != null) Log.Info("Arrêt d'Altéchap.");
        base.OnExit(e);
    }
}
