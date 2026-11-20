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
    [InlineData("api-gateway - Devin - Refactor del login…", "api-gateway", "Refactor del login…")]
    [InlineData("notes - Devin", "notes", "")]
    [InlineData("main.ts - api - Devin - Revisión de la API", "api", "main.ts - Revisión de la API")]
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
        Assert.Equal("Devin", TitleParser.Parse("Devin").WorkspaceKey);
    }

    [Fact]
    public void EmptyTitle()
    {
        Assert.Equal("", TitleParser.Parse("  ").WorkspaceKey);
        Assert.Equal("", TitleParser.Parse(null).WorkspaceKey);
    }

    [Fact]
    public void DevinTitleReportsTheApp()
    {
        Assert.Equal("Devin", TitleParser.Parse("api-gateway - Devin - Session").AppName);
    }

    [Fact]
    public void UnknownAppKeepsLastSegment()
    {
        Assert.Equal("Notes", TitleParser.Parse("draft - Notes").WorkspaceKey);
    }
}

public class WindowIdentityTests
{
    [Theory]
    [InlineData("api", "Code", "API", "code", true)]
    [InlineData("api", "Code", "api", "Devin", false)] // same folder open in two editors
    [InlineData("api", null, "api", "Devin", true)]     // profile saved before the editor was recorded
    [InlineData("api", "Devin", "web", "Devin", false)]
    public void MatchesFolderAndEditor(string key, string? app, string otherKey, string? otherApp, bool expected)
    {
        Assert.Equal(expected, WindowIdentity.Matches(key, app, otherKey, otherApp));
    }
}
