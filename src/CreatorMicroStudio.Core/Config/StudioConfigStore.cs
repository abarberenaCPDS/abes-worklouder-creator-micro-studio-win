namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Atomic read/modify/write access to Studio's host-side config file.</summary>
public sealed class StudioConfigStore
{
    private static readonly object ProcessGate = new();

    public StudioConfigStore(string path, string? legacyPath = null)
    {
        Path = path;
        LegacyPath = legacyPath;
    }

    public string Path { get; }
    public string? LegacyPath { get; }
    public string LockPath => Path + ".lock";

    public static StudioConfigStore Standard => new(KeyBindings.ConfigPath(), KeyBindings.LegacyConfigPath());

    public JsonObject Read()
    {
        var source = File.Exists(Path) ? Path : LegacyPath is { } legacy && File.Exists(legacy) ? legacy : null;
        if (source is null) return new JsonObject();
        var parsed = JsonNode.Parse(File.ReadAllText(source));
        return parsed as JsonObject
            ?? throw new InvalidDataException($"The config at {source} must be a JSON object.");
    }

    /// <summary>Updates only selected properties and preserves everything else.</summary>
    public void Update(Action<JsonObject> edit)
    {
        lock (ProcessGate)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var fileLock = AcquireFileLock();
            var json = Read();
            edit(json);

            var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                MoveIntoPlace(temp, Path);
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private FileStream AcquireFileLock()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(25); }
        }
    }

    private static void MoveIntoPlace(string temp, string destination)
    {
        // Antivirus/indexing can briefly hold a just-written config file open
        // on Windows. Retry the atomic same-volume replacement, but still fail
        // visibly if another process keeps the destination locked.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 20 && (ex is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(50);
            }
        }
    }
}
