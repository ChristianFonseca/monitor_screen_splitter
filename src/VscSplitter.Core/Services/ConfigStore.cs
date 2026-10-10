using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.Core.Services;

/// <summary>Loads and saves <see cref="AppConfig"/> as JSON in %APPDATA%\VscSplitter\config.json.</summary>
public sealed class ConfigStore
{
    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VscSplitter", "config.json");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; }

    /// <summary>False on the very first run (nothing saved yet).</summary>
    public bool Exists => File.Exists(FilePath);

    public AppConfig Load()
    {
        AppConfig? config = null;
        try
        {
            if (File.Exists(FilePath))
            {
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            BackupUnreadableFile();
        }

        config ??= new AppConfig();
        Sanitize(config);
        return config;
    }

    /// <summary>Writes atomically (temp file + rename) so a crash never leaves a half-written config.</summary>
    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static Profile Clone(Profile profile) =>
        JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, JsonOptions), JsonOptions)!;

    /// <summary>Repairs anything missing or invalid in a loaded config.</summary>
    public static void Sanitize(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        config.Settings.ProcessNames = (config.Settings.ProcessNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (config.Settings.ProcessNames.Count == 0)
        {
            config.Settings.ProcessNames = ["Code", "Code - Insiders"];
        }

        config.Profiles ??= [];
        config.Profiles.RemoveAll(p => p is null);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in config.Profiles)
        {
            var name = string.IsNullOrWhiteSpace(profile.Name) ? "Perfil" : profile.Name.Trim();
            var unique = name;
            for (var i = 2; !names.Add(unique); i++)
            {
                unique = $"{name} ({i})";
            }

            profile.Name = unique;
            profile.Monitors ??= [];
            profile.Monitors.RemoveAll(m => m is null);
            foreach (var monitor in profile.Monitors)
            {
                monitor.MonitorId ??= "";
                monitor.DeviceName ??= "";
                monitor.FriendlyName ??= "";
                monitor.Grid ??= new GridLayout();
                monitor.Gap = Math.Clamp(monitor.Gap, 0, 200);
                monitor.Margin = Math.Clamp(monitor.Margin, 0, 400);
                LayoutEngine.Normalize(monitor.Grid);
            }
        }

        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(new Profile { Name = "Principal" });
        }

        if (!config.Profiles.Any(p => string.Equals(p.Name, config.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
        {
            config.ActiveProfile = config.Profiles[0].Name;
        }
    }

    private void BackupUnreadableFile()
    {
        try
        {
            File.Copy(FilePath, FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
