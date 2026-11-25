using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadioClassic.Core;
using RadioClassic.Models;

namespace RadioClassic.Services;

/// <summary>Loads and saves settings.json. Saves are atomic (temp file + replace).</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _file;
    private readonly object _gate = new();

    public AppSettings Current { get; private set; } = new();

    /// <summary>Raised after settings were changed and saved (e.g. from the Options window).</summary>
    public event Action? Changed;

    public SettingsService(string? file = null) => _file = file ?? AppPaths.SettingsFile;

    public void Load()
    {
        try
        {
            if (File.Exists(_file))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A damaged settings file must not prevent startup; keep a copy for inspection.
            Log.Error("Could not read settings, using defaults", ex);
            try { File.Copy(_file, _file + ".bad", overwrite: true); } catch { }
            Current = new AppSettings();
        }
        Current.Sanitize();
    }

    public void Save()
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, _file, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public void SaveAndNotify()
    {
        Current.Sanitize();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Restores defaults but keeps data that is not a preference (last sync time).</summary>
    public void RestoreDefaults()
    {
        Current = new AppSettings { LastDatabaseSync = Current.LastDatabaseSync };
        SaveAndNotify();
    }
}
