using System.Windows;
using Altechap.Services;

using WpfApp    = System.Windows.Application;
using WpfMsgBox = System.Windows.MessageBox;

namespace Altechap.Views;

/// <summary>
/// Propose la mise à jour détectée : notes de version, puis téléchargement et
/// installation silencieuse. Le dialogue reste responsable de l'arrêt de
/// l'application — l'installeur ne peut pas remplacer un .exe verrouillé.
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateInfo _info;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Renseigné si l'utilisateur a choisi « Ignorer cette version ».</summary>
    public bool Skipped { get; private set; }

    public UpdateDialog(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;

        TxtVersions.Text = $"Version {info.Version} — vous utilisez la {UpdateService.Current}"
                         + (info.Size > 0 ? $"  ·  {info.Size / 1024d / 1024d:0.0} Mo" : "");
        TxtNotes.Text = string.IsNullOrWhiteSpace(info.Notes)
            ? "Aucune note de version publiée pour cette release."
            : info.Notes;
    }

    private void Skip_Click(object s, RoutedEventArgs e)
    {
        Skipped = true;
        DialogResult = false;
    }

    private void Later_Click(object s, RoutedEventArgs e) => DialogResult = false;

    private async void Install_Click(object s, RoutedEventArgs e)
    {
        ButtonArea.Visibility   = Visibility.Collapsed;
        ProgressArea.Visibility = Visibility.Visible;
        TxtProgress.Text        = "Téléchargement…";

        var progress = new Progress<double>(f =>
        {
            if (f < 0) { Bar.IsIndeterminate = true; return; }
            Bar.IsIndeterminate = false;
            Bar.Value = f;
            TxtProgress.Text = $"Téléchargement… {f:P0}";
        });

        try
        {
            var setup = await UpdateService.DownloadAsync(_info, progress, _cts.Token);

            TxtProgress.Text    = "Installation — Altéchap va redémarrer.";
            Bar.IsIndeterminate = true;

            UpdateService.LaunchInstaller(setup);

            // L'installeur a besoin que le processus rende la main : on ferme
            // tout de suite, il relance l'app grâce à /AUTOLAUNCH=1.
            DialogResult = true;
            WpfApp.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Mise à jour automatique impossible", ex);
            ProgressArea.Visibility = Visibility.Collapsed;
            ButtonArea.Visibility   = Visibility.Visible;

            var manual = WpfMsgBox.Show(
                $"Le téléchargement automatique a échoué :\n{ex.Message}\n\n" +
                "Ouvrir la page de téléchargement dans le navigateur ?",
                "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (manual == MessageBoxResult.Yes)
            {
                UpdateService.OpenPage(_info.PageUrl);
                DialogResult = false;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
