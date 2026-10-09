using Toucan.Plugins;

namespace Toucan.Connector.Rest;

/// <summary>Copies the service's translations into the project as one undoable edit that refuses to overwrite what the user typed.</summary>
public sealed class PullCommand(IPluginServices services, Lazy<HttpClient> client, Sessions sessions) : ICommandHandler
{
    public const string CommandId = "toucan-plugins.rest-connector.pull";

    public static CommandDefinition Definition { get; } = new()
    {
        Id = CommandId,
        Title = "Pull From Server",
        Category = "REST Connector",
        Description = "Copies changed translations from the server into the open project. One undo step.",
        DefaultShortcut = "Mod+Alt+L",
        Placements = CommandPlacement.CommandPalette | CommandPlacement.Menu,
    };

    public CommandState GetState(ICommandContext context) => Connection.StateOf(context, sessions, services);

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        if (await Connection.OpenAsync(services, client.Value, cancellationToken).ConfigureAwait(false) is not { } remote) return;
        try
        {
            // Snapshot first: if the user types while the server is being read, the edit is refused as stale instead of overwriting them.
            var snapshot = await services.Workspace.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null) return;

            invocation.Progress.Report(new CommandProgressInfo("Reading from the server", 0.1));
            var units = await remote.PullAsync(cancellationToken).ConfigureAwait(false);

            var edit = new WorkspaceEdit { Label = "Pulled from server", BasedOnRevision = snapshot.Revision };
            int unknown = 0, kept = 0;
            foreach (var unit in units)
            {
                if (snapshot.Find(unit.Key, unit.Language) is not { } local) { unknown++; continue; }
                if (local.IsModified && local.Value != unit.Text) { kept++; continue; }
                if (local.Value != unit.Text) edit.SetValue(unit.Key, unit.Language, unit.Text, expectedValue: local.Value);
            }

            if (edit.Operations.Count == 0)
            {
                services.Notifier.Notify(new PluginNotification { Title = "Up to date", Message = Remarks(unknown, kept), Severity = NotificationSeverity.Info });
                return;
            }

            invocation.Progress.Report(new CommandProgressInfo("Applying", 0.8));
            var result = await services.Workspace.ApplyAsync(edit, cancellationToken).ConfigureAwait(false);
            services.Notifier.Notify(result.Applied
                ? new PluginNotification { Title = "Pulled", Message = $"{result.ChangedUnits} translations updated. Undo restores them.{(kept == 0 ? string.Empty : $" {kept} you changed were kept.")}", Severity = NotificationSeverity.Success }
                : new PluginNotification { Title = "Nothing was changed", Message = result.Issues.FirstOrDefault()?.Message ?? "The project changed while reading. Run Pull again.", Severity = NotificationSeverity.Warning });
        }
        catch (RemoteException ex)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Pull failed", Message = ex.Message, Severity = NotificationSeverity.Error });
        }
        catch (HttpRequestException ex)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Pull failed", Message = services.Diagnostics.Redact($"Cannot reach the server: {ex.Message}"), Severity = NotificationSeverity.Error });
        }
    }

    private static string? Remarks(int unknown, int kept) =>
        (unknown, kept) switch
        {
            (0, 0) => null,
            (_, 0) => $"{unknown} server translations are not in this project and were left out.",
            (0, _) => $"{kept} translations you changed were kept.",
            _ => $"{kept} translations you changed were kept; {unknown} server translations are not in this project.",
        };
}
