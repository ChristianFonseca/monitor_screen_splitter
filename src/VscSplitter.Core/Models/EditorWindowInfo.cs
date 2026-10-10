namespace VscSplitter.Core.Models;

/// <summary>Snapshot of a top-level VS Code window.</summary>
public sealed record EditorWindowInfo
{
    public IntPtr Handle { get; init; }

    public string Title { get; init; } = "";

    /// <summary>Folder / workspace name parsed from the title; the stable identity used in profiles.</summary>
    public string WorkspaceKey { get; init; } = "";

    /// <summary>The part of the title before the folder name (active editor or chat tab).</summary>
    public string ActiveEditor { get; init; } = "";

    public string ProcessName { get; init; } = "";

    public string? ProcessPath { get; init; }

    public int ProcessId { get; init; }

    /// <summary>Visible frame bounds (without the invisible resize borders).</summary>
    public PixelRect FrameBounds { get; init; }

    public bool IsMinimized { get; init; }

    public bool IsMaximized { get; init; }
}
