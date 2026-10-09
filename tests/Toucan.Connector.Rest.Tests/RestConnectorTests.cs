using Toucan.Plugins;
using Toucan.Plugins.Testing;
using Xunit;

namespace Toucan.Connector.Rest.Tests;

public sealed class RestConnectorTests : IAsyncDisposable
{
    private const string Id = "toucan-plugins.rest-connector";
    private readonly FakeServer _server = new();
    private readonly PluginTestHost _host = new(Id);

    public RestConnectorTests() => _host.Register(new RestConnectorPlugin(_server));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task OpenAsync(bool configured = true)
    {
        _host.Services.InMemory.Open("en", ["en", "de"], new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["app.save"] = new Dictionary<string, string> { ["en"] = "Save", ["de"] = "" },
            ["app.cancel"] = new Dictionary<string, string> { ["en"] = "Cancel", ["de"] = "Abbrechen" },
        });
        if (configured)
        {
            await _host.Services.Configuration.SetAsync(RestConnectorPlugin.ServerUrl, "https://i18n.example.com/api/");
            await _host.Services.Configuration.SetAsync(RestConnectorPlugin.ApiToken, FakeServer.Token);
        }
        await _host.ActivateAsync(PluginLifetime.Workspace, workspaceId: "test-workspace");
    }

    [Fact]
    public void RegistrationNeedsNoNetworkAndNoUi()
    {
        Assert.Empty(_server.Requests);
        Assert.Equal(2, _host.Commands.Count);
        Assert.Empty(CliCompatibility.UiReferences(typeof(RestConnectorPlugin).Assembly));
    }

    [Fact]
    public async Task CommandsFollowTheProjectAndTheServerAddress()
    {
        Assert.Equal(CommandAvailability.Unavailable, _host.StateOf(PullCommand.CommandId).Availability);

        await OpenAsync(configured: false);
        Assert.Equal(CommandAvailability.Disconnected, _host.StateOf(PullCommand.CommandId).Availability);

        await _host.Services.Configuration.SetAsync(RestConnectorPlugin.ServerUrl, "https://i18n.example.com/api/");
        Assert.Equal(CommandAvailability.Available, _host.StateOf(PullCommand.CommandId).Availability);

        await _host.CloseAsync(PluginLifetime.Workspace, workspaceId: "test-workspace");
        Assert.Equal(CommandAvailability.Unavailable, _host.StateOf(PullCommand.CommandId).Availability);
    }

    [Fact]
    public async Task PullUpdatesChangedTranslationsAsOneEdit()
    {
        await OpenAsync();
        _server.Seed("app.save", "de", "Speichern", 3);
        _server.Seed("app.cancel", "de", "Abbrechen", 1);
        _server.Seed("not.in.project", "de", "x", 1);
        var before = (await _host.Services.Workspace.SnapshotAsync())!.Revision;

        await _host.RunAsync(PullCommand.CommandId);

        var snapshot = (await _host.Services.Workspace.SnapshotAsync())!;
        Assert.Equal("Speichern", snapshot.Find("app.save", "de")!.Value);
        Assert.Equal(before + 1, snapshot.Revision);
        Assert.Contains(_host.Services.Notifications.Items, n => n is { Title: "Pulled", Severity: NotificationSeverity.Success });
    }

    [Fact]
    public async Task PushSendsModifiedValuesWithTheRevisionItRead()
    {
        await OpenAsync();
        _server.Seed("app.save", "de", "alt", 4);
        _host.Services.InMemory.UserEdit("app.save", "de", "Speichern");

        await _host.RunAsync(PushCommand.CommandId);

        Assert.Equal(new RemoteUnit("app.save", "de", "Speichern", 5), _server.Units[("app.save", "de")]);
        Assert.Contains(_host.Services.Notifications.Items, n => n.Title == "Pushed");
    }

    [Fact]
    public async Task PushNeverOverwritesWhatTheServerChangedBetweenReadAndWrite()
    {
        await OpenAsync();
        _server.Seed("app.save", "de", "alt", 4);
        _host.Services.InMemory.UserEdit("app.save", "de", "Speichern");
        _server.OnNextRead = () => _server.Seed("app.save", "de", "von jemand anderem", 5);

        await _host.RunAsync(PushCommand.CommandId);

        Assert.Equal("von jemand anderem", _server.Units[("app.save", "de")].Text);
        Assert.Contains(_host.Services.Notifications.Items, n => n is { Title: "Pushed with skipped items", ActionCommandId: PullCommand.CommandId });
    }

    [Fact]
    public async Task ABadTokenIsReportedWithoutLeakingIt()
    {
        await OpenAsync();
        await _host.Services.Configuration.SetAsync(RestConnectorPlugin.ApiToken, "wrong-token");

        await _host.RunAsync(PullCommand.CommandId);

        var note = Assert.Single(_host.Services.Notifications.Items, n => n.Title == "Pull failed");
        Assert.Contains("API token", note.Message);
        Assert.DoesNotContain("wrong-token", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APullStopsWhenItIsCancelled()
    {
        await OpenAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _host.RunAsync(PullCommand.CommandId, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task APullThatRacesAUserEditChangesNothing()
    {
        await OpenAsync();
        _server.Seed("app.save", "de", "Speichern", 1);
        _server.OnNextRead = () => _host.Services.InMemory.UserEdit("app.save", "de", "Meine Version");

        await _host.RunAsync(PullCommand.CommandId);

        Assert.Equal("Meine Version", (await _host.Services.Workspace.SnapshotAsync())!.Find("app.save", "de")!.Value);
        Assert.Contains(_host.Services.Notifications.Items, n => n.Title == "Nothing was changed");
    }
}
