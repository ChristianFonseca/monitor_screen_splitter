using System.Diagnostics;

namespace VscSplitter.Core.Services;

/// <summary>Opens folders/workspaces in a new window of VS Code or one of its forks (Devin IDE…).</summary>
public static class VsCodeLauncher
{
    /// <summary>
    /// Executable for an editor process (<c>Code</c>, <c>Devin</c>…): the path remembered by the profile, then a
    /// running window of that editor, then its default install locations. Without a process name (profiles saved
    /// before the editor was recorded) any running editor will do, then VS Code.
    /// </summary>
    public static string? FindExecutable(string? rememberedPath, string? processName, IEnumerable<string?> runningExecutables)
    {
        if (!string.IsNullOrEmpty(rememberedPath) && File.Exists(rememberedPath) &&
            (string.IsNullOrEmpty(processName) || IsProcess(rememberedPath, processName)))
        {
            return rememberedPath;
        }

        foreach (var path in runningExecutables)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path) &&
                (string.IsNullOrEmpty(processName) || IsProcess(path, processName)))
            {
                return path;
            }
        }

        return InstallLocations(string.IsNullOrEmpty(processName) ? "Code" : processName).FirstOrDefault(File.Exists);
    }

    private static bool IsProcess(string path, string processName) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), processName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> InstallLocations(string processName)
    {
        var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return processName.ToLowerInvariant() switch
        {
            "code" =>
            [
                Path.Combine(localPrograms, "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
            ],
            "code - insiders" =>
            [
                Path.Combine(localPrograms, "Microsoft VS Code Insiders", "Code - Insiders.exe"),
                Path.Combine(programFiles, "Microsoft VS Code Insiders", "Code - Insiders.exe"),
            ],
            // The Devin IDE installs over Windsurf: %LOCALAPPDATA%\Programs\Windsurf\Devin.exe.
            "devin" =>
            [
                Path.Combine(localPrograms, "Windsurf", "Devin.exe"),
                Path.Combine(localPrograms, "Devin", "Devin.exe"),
            ],
            _ => [],
        };
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
