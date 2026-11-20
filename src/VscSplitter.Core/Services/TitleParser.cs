using System.Text.RegularExpressions;

namespace VscSplitter.Core.Services;

public readonly record struct ParsedTitle(string WorkspaceKey, string ActiveEditor, string AppName);

/// <summary>
/// Extracts the folder/workspace name from the window title of VS Code and its forks.
/// <list type="bullet">
/// <item>VS Code and most forks: <c>[●] editor - folder - Visual Studio Code</c>. The folder is right before the
/// app name, at the end; the editor part changes constantly.</item>
/// <item>Devin IDE: <c>folder - Devin - agent session</c>. The app name sits in the middle, followed by the title
/// of the current agent session.</item>
/// </list>
/// </summary>
public static partial class TitleParser
{
    private static readonly string[] AppNames =
    [
        "Visual Studio Code - Insiders",
        "Visual Studio Code",
        "VSCodium - Insiders",
        "VSCodium",
        "Code - OSS",
        "Cursor",
        "Windsurf",
        "Devin",
    ];

    private const string Separator = " - ";

    public static ParsedTitle Parse(string? title)
    {
        var text = (title ?? "").Trim();
        if (text.Length == 0)
        {
            return new ParsedTitle("", "", "");
        }

        // "… - Visual Studio Code [Administrator]" and similar suffixes after the app name.
        var suffix = TrailingBracket().Match(text);
        if (suffix.Success)
        {
            var withoutSuffix = text[..suffix.Index].TrimEnd();
            if (AppNames.Any(a => withoutSuffix.EndsWith(a, StringComparison.OrdinalIgnoreCase)))
            {
                text = withoutSuffix;
            }
        }

        foreach (var app in AppNames)
        {
            if (text.Equals(app, StringComparison.OrdinalIgnoreCase))
            {
                return new ParsedTitle(app, "", app);
            }
        }

        // App name at the end: "editor - folder - App".
        foreach (var app in AppNames)
        {
            if (text.EndsWith(Separator + app, StringComparison.OrdinalIgnoreCase))
            {
                return Split(text[..^(Separator.Length + app.Length)], "", app, title!);
            }
        }

        // App name in the middle: "folder - Devin - agent session".
        foreach (var app in AppNames)
        {
            var marker = Separator + app + Separator;
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return Split(text[..index], text[(index + marker.Length)..].Trim(), app, title!);
            }
        }

        return Split(text, "", "", title!);
    }

    /// <summary>The folder is the last segment of <paramref name="beforeApp"/>; everything else is context.</summary>
    private static ParsedTitle Split(string beforeApp, string afterApp, string appName, string title)
    {
        // Unsaved-changes marker at the start of the title.
        var parts = beforeApp.TrimStart('●', '•', '*', ' ').Split(Separator);
        var key = parts[^1].Trim();
        var context = parts[..^1].Select(p => p.Trim()).Append(afterApp).Where(p => p.Length > 0);
        if (key.Length == 0)
        {
            key = title.Trim();
        }

        return new ParsedTitle(key, string.Join(Separator, context), appName);
    }

    [GeneratedRegex(@"\s\[[^\[\]]+\]$")]
    private static partial Regex TrailingBracket();
}

/// <summary>How a window remembered by a profile is recognised again.</summary>
public static class WindowIdentity
{
    /// <summary>
    /// Same folder name and, when both sides know it, the same editor process: the same folder can be open in
    /// VS Code and in Devin at the same time. Profiles saved before the editor was recorded match by folder only.
    /// </summary>
    public static bool Matches(string key, string? app, string otherKey, string? otherApp) =>
        string.Equals(key, otherKey, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrEmpty(app) || string.IsNullOrEmpty(otherApp) ||
         string.Equals(app, otherApp, StringComparison.OrdinalIgnoreCase));
}
