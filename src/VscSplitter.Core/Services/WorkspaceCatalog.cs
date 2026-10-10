using System.Text.Json;

namespace VscSplitter.Core.Services;

/// <summary>A folder or .code-workspace that VS Code has open, with the name it shows in window titles.</summary>
public sealed record WorkspaceEntry(string Key, string Uri, bool IsWorkspaceFile);

/// <summary>
/// Reads VS Code's <c>storage.json</c> (windowsState / backupWorkspaces) to learn the full folder URI behind
/// each window title, so missing windows can be reopened after a restart.
/// </summary>
public static class WorkspaceCatalog
{
    private static readonly string[] Products = ["Code", "Code - Insiders", "VSCodium", "Cursor", "Windsurf"];

    public static IReadOnlyList<string> DefaultStorageFiles()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Products
            .Select(p => Path.Combine(appData, p, "User", "globalStorage", "storage.json"))
            .Where(File.Exists)
            .ToList();
    }

    public static IReadOnlyList<WorkspaceEntry> Load(IEnumerable<string>? storageFiles = null)
    {
        var entries = new List<WorkspaceEntry>();
        foreach (var file in storageFiles ?? DefaultStorageFiles())
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                entries.AddRange(Parse(document.RootElement));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        return entries
            .GroupBy(e => e.Uri, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Finds the entry for a window key; remote windows add a suffix such as " [WSL: Ubuntu]".</summary>
    public static WorkspaceEntry? Find(IEnumerable<WorkspaceEntry> entries, string windowKey)
    {
        WorkspaceEntry? prefixMatch = null;
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Key, windowKey, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            if (prefixMatch is null && windowKey.StartsWith(entry.Key + " [", StringComparison.OrdinalIgnoreCase))
            {
                prefixMatch = entry;
            }
        }

        return prefixMatch;
    }

    /// <summary>
    /// Converts a VS Code file URI to a Windows path. Done by hand because VS Code escapes the drive colon
    /// (<c>file:///f%3A/dir</c>), which System.Uri does not treat as a drive letter.
    /// </summary>
    public static string? ToLocalPath(string uri)
    {
        const string prefix = "file://";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = uri[prefix.Length..];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var path = Uri.UnescapeDataString(slash < 0 ? "" : rest[slash..]);
        if (authority.Length > 0)
        {
            return @"\\" + Uri.UnescapeDataString(authority) + path.Replace('/', '\\');
        }

        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
        {
            path = path[1..];
        }

        return path.Replace('/', '\\');
    }

    internal static IEnumerable<WorkspaceEntry> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("windowsState", out var state) && state.ValueKind == JsonValueKind.Object)
        {
            if (state.TryGetProperty("lastActiveWindow", out var last))
            {
                foreach (var entry in FromWindow(last))
                {
                    yield return entry;
                }
            }

            if (state.TryGetProperty("openedWindows", out var opened) && opened.ValueKind == JsonValueKind.Array)
            {
                foreach (var window in opened.EnumerateArray())
                {
                    foreach (var entry in FromWindow(window))
                    {
                        yield return entry;
                    }
                }
            }
        }

        if (root.TryGetProperty("backupWorkspaces", out var backups) && backups.ValueKind == JsonValueKind.Object)
        {
            if (backups.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Array)
            {
                foreach (var folder in folders.EnumerateArray())
                {
                    if (TryGetString(folder, "folderUri", out var uri) && Create(uri, false) is { } entry)
                    {
                        yield return entry;
                    }
                }
            }

            if (backups.TryGetProperty("workspaces", out var workspaces) && workspaces.ValueKind == JsonValueKind.Array)
            {
                foreach (var workspace in workspaces.EnumerateArray())
                {
                    if (TryGetString(workspace, "configURIPath", out var uri) && Create(uri, true) is { } entry)
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    internal static string KeyFromUri(string uri, bool isWorkspaceFile)
    {
        // Parsed by hand: System.Uri rejects remote authorities such as "wsl%2Bubuntu".
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        var pathStart = schemeEnd > 0 ? uri.IndexOf('/', schemeEnd + 3) : -1;
        if (pathStart < 0)
        {
            return "";
        }

        var path = uri[pathStart..];
        var queryStart = path.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        path = Uri.UnescapeDataString(path).TrimEnd('/');
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Length == 0)
        {
            return "";
        }

        return isWorkspaceFile ? Path.GetFileNameWithoutExtension(name) + " (Workspace)" : name;
    }

    private static IEnumerable<WorkspaceEntry> FromWindow(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (TryGetString(window, "folder", out var folder) && Create(folder, false) is { } folderEntry)
        {
            yield return folderEntry;
        }

        if (window.TryGetProperty("workspace", out var workspace) &&
            TryGetString(workspace, "configPath", out var configPath) &&
            Create(configPath, true) is { } workspaceEntry)
        {
            yield return workspaceEntry;
        }
    }

    private static WorkspaceEntry? Create(string uri, bool isWorkspaceFile)
    {
        var key = KeyFromUri(uri, isWorkspaceFile);
        return key.Length == 0 ? null : new WorkspaceEntry(key, uri, isWorkspaceFile);
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString() ?? "";
        return value.Length > 0;
    }
}
