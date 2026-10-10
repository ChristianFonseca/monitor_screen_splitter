using System.IO;
using System.Text.Json;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;
using VscSplitter.Core.Services;

namespace VscSplitter.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VscSplitterTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void RoundTripsProfiles()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var grid = LayoutPresets.All.Single(p => p.Id == "main4").Create();
        grid.Zones[0].WindowKey = "api-gateway";
        grid.Zones[0].FolderUri = "file:///c%3A/dev/api-gateway";
        var config = new AppConfig
        {
            ActiveProfile = "Trabajo",
            Profiles =
            [
                new Profile
                {
                    Name = "Trabajo",
                    Monitors = [new MonitorLayout { MonitorId = "id", Bounds = new PixelRect(-3840, 0, 3840, 2160), Grid = grid, Gap = 6 }],
                },
            ],
        };

        store.Save(config);
        var loaded = store.Load();

        var monitor = Assert.Single(Assert.Single(loaded.Profiles).Monitors);
        Assert.Equal("Trabajo", loaded.ActiveProfile);
        Assert.Equal(new PixelRect(-3840, 0, 3840, 2160), monitor.Bounds);
        Assert.Equal(6, monitor.Gap);
        Assert.Equal(5, monitor.Grid.Zones.Count);
        Assert.Equal("api-gateway", monitor.Grid.Zones[0].WindowKey);
        Assert.Equal(grid.ColumnWeights, monitor.Grid.ColumnWeights);
        Assert.Contains("\"monitors\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void MissingFileGivesDefaultProfile()
    {
        var loaded = new ConfigStore(Path.Combine(_dir, "nope.json")).Load();

        Assert.Equal("Principal", Assert.Single(loaded.Profiles).Name);
        Assert.Equal("Principal", loaded.ActiveProfile);
        Assert.Contains("Code", loaded.Settings.ProcessNames);
    }

    [Fact]
    public void CorruptFileIsBackedUpAndReplacedByDefaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new ConfigStore(path).Load();

        Assert.Single(loaded.Profiles);
        Assert.Single(Directory.GetFiles(_dir, "config.json.corrupt-*"));
    }

    [Fact]
    public void SanitizeFixesDuplicateNamesAndActiveProfile()
    {
        var config = new AppConfig
        {
            ActiveProfile = "missing",
            Profiles = [new Profile { Name = "A" }, new Profile { Name = "a" }, new Profile { Name = " " }],
        };

        ConfigStore.Sanitize(config);

        Assert.Equal(["A", "a (2)", "Perfil"], config.Profiles.Select(p => p.Name));
        Assert.Equal("A", config.ActiveProfile);
    }
}

public class WorkspaceCatalogTests
{
    [Theory]
    [InlineData("file:///f%3A/dev/my_project", false, "my_project")]
    [InlineData("file:///c%3A/Users/me/My%20Project/", false, "My Project")]
    [InlineData("vscode-remote://wsl%2Bubuntu/home/me/api", false, "api")]
    [InlineData("file:///c%3A/work/team.code-workspace", true, "team (Workspace)")]
    public void KeyMatchesWindowTitleFolderName(string uri, bool isWorkspace, string expected)
    {
        Assert.Equal(expected, WorkspaceCatalog.KeyFromUri(uri, isWorkspace));
    }

    [Fact]
    public void ParsesStorageJson()
    {
        const string json = """
        {
          "windowsState": {
            "lastActiveWindow": { "folder": "file:///c%3A/dev/monitor_vsc_splitter" },
            "openedWindows": [
              { "folder": "file:///f%3A/dev/my_project", "uiState": { "mode": 1 } },
              { "workspace": { "id": "x", "configPath": "file:///c%3A/work/team.code-workspace" } }
            ]
          },
          "backupWorkspaces": { "folders": [ { "folderUri": "vscode-remote://wsl%2Bubuntu/home/me/api" } ] }
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var entries = WorkspaceCatalog.Parse(doc.RootElement).ToList();

        Assert.Equal(["monitor_vsc_splitter", "my_project", "team (Workspace)", "api"], entries.Select(e => e.Key));
        Assert.True(entries[2].IsWorkspaceFile);
        Assert.Equal(@"f:\dev\my_project", WorkspaceCatalog.ToLocalPath(entries[1].Uri));
        Assert.Same(entries[3], WorkspaceCatalog.Find(entries, "api [WSL: Ubuntu]"));
        Assert.Same(entries[1], WorkspaceCatalog.Find(entries, "MY_PROJECT"));
        Assert.Null(WorkspaceCatalog.Find(entries, "other"));
    }
}
