using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altechap.Models;

namespace Altechap.Services;

public sealed class StorageService
{
    public static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Altechap", "config.json");

    private static readonly string TempPath   = DataPath + ".tmp";
    private static readonly string BackupPath = DataPath + ".bak";

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppData Load()
    {
        // Fichier courant, puis sauvegarde de secours si le premier est illisible
        foreach (var path in new[] { DataPath, BackupPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Opts);
                if (data != null)
                {
                    if (path == BackupPath)
                        Log.Warn($"config.json illisible — restauré depuis {Path.GetFileName(BackupPath)}");
                    return data;
                }
                Log.Warn($"{Path.GetFileName(path)} désérialisé en null.");
            }
            catch (Exception ex)
            {
                Log.Error($"Lecture de {Path.GetFileName(path)} impossible", ex);
            }
        }

        if (File.Exists(DataPath) || File.Exists(BackupPath))
            Log.Warn("Aucune config exploitable — démarrage sur une config vierge.");
        return new AppData();
    }

    /// <summary>
    /// Écriture atomique : on écrit dans un .tmp, on le vérifie, puis on le
    /// substitue au fichier réel (l'ancien devient .bak). Un crash pendant
    /// l'écriture ne peut plus détruire la configuration.
    /// </summary>
    public void Save(AppData d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);

            var json = JsonSerializer.Serialize(d, Opts);
            File.WriteAllText(TempPath, json);

            // Relecture de contrôle : on ne remplace jamais une bonne config
            // par un fichier tronqué.
            if (JsonSerializer.Deserialize<AppData>(File.ReadAllText(TempPath), Opts) == null)
            {
                Log.Error("Écriture annulée : le fichier temporaire est illisible.");
                return;
            }

            if (File.Exists(DataPath))
                File.Replace(TempPath, DataPath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(TempPath, DataPath);
        }
        catch (Exception ex)
        {
            Log.Error("Sauvegarde de la configuration impossible", ex);
            try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { }
        }
    }
}
