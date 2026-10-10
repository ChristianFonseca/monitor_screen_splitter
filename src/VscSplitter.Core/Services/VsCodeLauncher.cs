using System.Diagnostics;

namespace VscSplitter.Core.Services;

/// <summary>Opens folders/workspaces in a new VS Code window.</summary>
public static class VsCodeLauncher
{
    /// <summary>Prefers the executable of a running editor window, then the default install locations.</summary>
    public static string? FindExecutable(IEnumerable<string?> runningExecutables)
    {
        foreach (var path in runningExecutables)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Open(string executable, string uri)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add("--new-window");

        var localPath = WorkspaceCatalog.ToLocalPath(uri);
        if (localPath is not null)
        {
            info.ArgumentList.Add(localPath);
        }
        else
        {
            var isWorkspace = uri.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase);
            info.ArgumentList.Add(isWorkspace ? "--file-uri" : "--folder-uri");
            info.ArgumentList.Add(uri);
        }

        // When this app was started from a VS Code terminal, don't leak that session's variables.
        foreach (var name in info.Environment.Keys.Where(k =>
                     k.StartsWith("VSCODE_", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("ELECTRON_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            info.Environment.Remove(name);
        }

        Process.Start(info)?.Dispose();
    }
}
