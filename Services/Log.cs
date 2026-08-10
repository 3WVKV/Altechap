using System.IO;
using System.Text;

namespace Altechap.Services;

/// <summary>
/// Journal fichier minimal — %AppData%\Altechap\log.txt
///
/// Objectif : ne plus jamais avoir un catch silencieux. Rotation à 512 Ko
/// (log.txt → log.old.txt), écriture verrouillée, et surtout : le logger
/// lui-même n'échoue jamais bruyamment (il ne doit pas casser l'app).
/// </summary>
public static class Log
{
    private static readonly object _gate = new();

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Altechap");

    public static readonly string Path_ = Path.Combine(Dir, "log.txt");
    private static readonly string OldPath = Path.Combine(Dir, "log.old.txt");

    private const long MaxBytes = 512 * 1024;

    public static void Info (string msg)                    => Write("INFO ", msg, null);
    public static void Warn (string msg, Exception? ex = null) => Write("WARN ", msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    private static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Dir);
                Rotate();

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level).Append("] ").Append(msg);
                if (ex != null)
                    sb.AppendLine().Append("        ").Append(ex.GetType().Name)
                      .Append(": ").Append(ex.Message);
                sb.AppendLine();

                File.AppendAllText(Path_, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { /* le journal ne doit jamais faire tomber l'app */ }
    }

    private static void Rotate()
    {
        try
        {
            var fi = new FileInfo(Path_);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(Path_, OldPath);
        }
        catch { }
    }
}
