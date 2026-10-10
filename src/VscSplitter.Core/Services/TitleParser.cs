using System.Text.RegularExpressions;

namespace VscSplitter.Core.Services;

public readonly record struct ParsedTitle(string WorkspaceKey, string ActiveEditor, string AppName);

/// <summary>
/// Extracts the folder/workspace name from a VS Code window title. With the default
/// <c>window.title</c> setting titles look like <c>[●] editor - folder - Visual Studio Code</c>;
/// the folder part is stable while the editor part changes constantly.
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

        var appName = "";
        foreach (var app in AppNames)
        {
            if (text.Equals(app, StringComparison.OrdinalIgnoreCase))
            {
                return new ParsedTitle(app, "", app);
            }

            if (text.EndsWith(Separator + app, StringComparison.OrdinalIgnoreCase))
            {
                appName = app;
                text = text[..^(Separator.Length + app.Length)];
                break;
            }
        }

        // Unsaved-changes marker at the start of the title.
        text = text.TrimStart('●', '•', '*', ' ');

        var parts = text.Split(Separator);
        var key = parts[^1].Trim();
        var editor = parts.Length > 1 ? string.Join(Separator, parts[..^1]).Trim() : "";
        if (key.Length == 0)
        {
            key = title!.Trim();
        }

        return new ParsedTitle(key, editor, appName);
    }

    [GeneratedRegex(@"\s\[[^\[\]]+\]$")]
    private static partial Regex TrailingBracket();
}
