using Toucan.Plugins;

namespace Toucan.Connector.Rest;

/// <summary>
/// Registers the connector's settings, a per-project session and the Pull and Push commands. Registration reads and opens
/// nothing on the network: requests are made only when a command runs.
/// </summary>
public sealed class RestConnectorPlugin : IToucanPlugin
{
    public const string ServerUrl = "serverUrl";
    public const string ApiToken = "apiToken";

    private readonly HttpMessageHandler? _handler;

    public RestConnectorPlugin() { }

    /// <summary>For tests: send requests through <paramref name="handler"/> instead of the network.</summary>
    public RestConnectorPlugin(HttpMessageHandler handler) => _handler = handler;

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.SetConfiguration(new ConfigSchema
        {
            Version = 1,
            Fields =
            [
                new ConfigField { Key = ServerUrl, Label = "Server address", Type = ConfigFieldType.Url, Required = true, Description = "Base address of the service, for example https://i18n.example.com/api/." },
                new ConfigField { Key = ApiToken, Label = "API token", Type = ConfigFieldType.Secret, Required = true, Description = "Sent as a bearer token. Kept in the secret store, never in the settings file or the logs." },
            ],
        });

        var sessions = new Sessions();
        context.AddActivator("sessions", new SessionActivator(sessions));
        var client = new Lazy<HttpClient>(() => _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false));
        context.AddCommand(PullCommand.Definition, new PullCommand(context.Services, client, sessions));
        context.AddCommand(PushCommand.Definition, new PushCommand(context.Services, client, sessions));
    }
}

/// <summary>The projects that have a session open.</summary>
public sealed class Sessions
{
    private readonly HashSet<string> _open = [];
    public bool Any { get { lock (_open) return _open.Count > 0; } }
    internal void Open(string id) { lock (_open) _open.Add(id); }
    internal void Close(string id) { lock (_open) _open.Remove(id); }
}

internal sealed class SessionActivator(Sessions sessions) : IPluginActivator
{
    public PluginLifetime Lifetime => PluginLifetime.Workspace;

    public Task<IAsyncDisposable?> ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
    {
        var id = context.WorkspaceId ?? throw new InvalidOperationException("A workspace activator needs a workspace.");
        sessions.Open(id);
        return Task.FromResult<IAsyncDisposable?>(new Session(sessions, id));
    }

    private sealed class Session(Sessions sessions, string id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            sessions.Close(id);
            return ValueTask.CompletedTask;
        }
    }
}

internal static class Connection
{
    public static CommandState StateOf(ICommandContext context, Sessions sessions, IPluginServices services)
    {
        if (!context.HasWorkspace || !sessions.Any) return new CommandState(CommandAvailability.Unavailable, "Open a project first.");
        return TryAddress(services) is null
            ? new CommandState(CommandAvailability.Disconnected, "Set the server address in Settings → Plugins.")
            : CommandState.Available;
    }

    public static Uri? TryAddress(IPluginServices services)
    {
        var text = services.Configuration.GetValue<string>(RestConnectorPlugin.ServerUrl);
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var uri)) return null;
        return uri.Scheme is "http" or "https" ? uri : null;
    }

    public static async Task<RestRemote?> OpenAsync(IPluginServices services, HttpClient client, CancellationToken cancellationToken)
    {
        var address = TryAddress(services);
        var token = await services.Configuration.GetSecretAsync(RestConnectorPlugin.ApiToken, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (address is null || string.IsNullOrEmpty(token))
        {
            services.Notifier.Notify(new PluginNotification { Title = "Not connected", Message = "Set the server address and API token in Settings → Plugins.", Severity = NotificationSeverity.Warning });
            return null;
        }
        services.Diagnostics.RegisterSecret(token);
        return new RestRemote(client, address, token);
    }
}
