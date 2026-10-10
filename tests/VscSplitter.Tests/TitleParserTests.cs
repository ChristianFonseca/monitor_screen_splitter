using VscSplitter.Core.Services;

namespace VscSplitter.Tests;

public class TitleParserTests
{
    [Theory]
    [InlineData("Gestor de ventanas de VS… - monitor_vsc_splitter - Visual Studio Code", "monitor_vsc_splitter", "Gestor de ventanas de VS…")]
    [InlineData("1. ¿Hago commit de los cam… - api-gateway - Visual Studio Code", "api-gateway", "1. ¿Hago commit de los cam…")]
    [InlineData("Web-App mejoras UI UX… - web-app - Visual Studio Code", "web-app", "Web-App mejoras UI UX…")]
    [InlineData(".mcp.json - infra - Visual Studio Code", "infra", ".mcp.json")]
    [InlineData("notes - Visual Studio Code", "notes", "")]
    [InlineData("● main.ts - api - Visual Studio Code", "api", "main.ts")]
    [InlineData("main.ts - api - Visual Studio Code - Insiders", "api", "main.ts")]
    [InlineData("main.ts - api - Visual Studio Code [Administrator]", "api", "main.ts")]
    [InlineData("app.py - project [WSL: Ubuntu] - Visual Studio Code", "project [WSL: Ubuntu]", "app.py")]
    [InlineData("settings.json - My Space (Workspace) - Visual Studio Code", "My Space (Workspace)", "settings.json")]
    [InlineData("a - b - c - repo - Cursor", "repo", "a - b - c")]
    public void ExtractsFolderAndEditor(string title, string expectedKey, string expectedEditor)
    {
        var parsed = TitleParser.Parse(title);

        Assert.Equal(expectedKey, parsed.WorkspaceKey);
        Assert.Equal(expectedEditor, parsed.ActiveEditor);
    }

    [Fact]
    public void BareAppNameIsItsOwnKey()
    {
        Assert.Equal("Visual Studio Code", TitleParser.Parse("Visual Studio Code").WorkspaceKey);
    }

    [Fact]
    public void EmptyTitle()
    {
        Assert.Equal("", TitleParser.Parse("  ").WorkspaceKey);
        Assert.Equal("", TitleParser.Parse(null).WorkspaceKey);
    }

    [Fact]
    public void UnknownAppKeepsLastSegment()
    {
        Assert.Equal("Notes", TitleParser.Parse("draft - Notes").WorkspaceKey);
    }
}
