using Toucan.Plugins;

namespace Toucan.Connector.Rest;

/// <summary>Sends the translations changed since the project was opened or saved. Each is written only if the server still holds the revision that was read.</summary>
public sealed class PushCommand(IPluginServices services, Lazy<HttpClient> client, Sessions sessions) : ICommandHandler
{
    public const string CommandId = "toucan-plugins.rest-connector.push";

    public static CommandDefinition Definition { get; } = new()
    {
        Id = CommandId,
        Title = "Push To Server",
        Category = "REST Connector",
        Description = "Sends modified translations to the server. Anything the server changed meanwhile is skipped and reported.",
        Placements = CommandPlacement.CommandPalette | CommandPlacement.Menu,
    };

    public CommandState GetState(ICommandContext context) => Connection.StateOf(context, sessions, services);

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        if (await Connection.OpenAsync(services, client.Value, cancellationToken).ConfigureAwait(false) is not { } remote) return;
        int sent = 0, skipped = 0;
        try
        {
            var snapshot = await services.Workspace.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null) return;

            var revisions = (await remote.PullAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(u => (u.Key, u.Language), u => u.Revision);
            var modified = snapshot.Units.Where(u => u.IsModified && u.Value.Length > 0 && u.Language != snapshot.PrimaryLanguage).ToList();
            for (var i = 0; i < modified.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                invocation.Progress.Report(new CommandProgressInfo($"Sending {i + 1} of {modified.Count}", (double)i / modified.Count));
                var u = modified[i];
                var outcome = await remote.PushAsync(u.Key, u.Language, u.Value, revisions.GetValueOrDefault((u.Key, u.Language)), cancellationToken).ConfigureAwait(false);
                if (outcome == PushOutcome.Sent) sent++;
                else skipped++;
            }
        }
        catch (RemoteException ex)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Push failed", Message = $"{ex.Message} {sent} sent before it stopped.", Severity = NotificationSeverity.Error });
            return;
        }
        catch (HttpRequestException ex)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Push failed", Message = services.Diagnostics.Redact($"Cannot reach the server: {ex.Message}. {sent} sent before it stopped."), Severity = NotificationSeverity.Error });
            return;
        }

        services.Notifier.Notify(new PluginNotification
        {
            Title = skipped == 0 ? "Pushed" : "Pushed with skipped items",
            Message = skipped == 0 ? $"{sent} translations sent." : $"{sent} sent, {skipped} skipped because the server changed. Pull, then push again.",
            Severity = skipped == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning,
            ActionCommandId = skipped == 0 ? null : PullCommand.CommandId,
            ActionLabel = skipped == 0 ? null : "Pull",
        });
    }
}
