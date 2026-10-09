using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Toucan.Plugins.Desktop;

namespace Toucan.Connector.Rest.Desktop;

/// <summary>The connector's side panel: connection state, and Pull and Push as buttons that run the same commands as the palette.</summary>
public sealed class RestConnectorDesktop : IToucanDesktopPlugin
{
    private const string ServerUrl = "serverUrl";
    private const string Pull = "toucan-plugins.rest-connector.pull";
    private const string Push = "toucan-plugins.rest-connector.push";

    public void InitializeDesktop(IDesktopPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddSidePanel(new SidePanelContribution
        {
            Id = "toucan-plugins.rest-connector.panel",
            Title = "Server",
            Slot = PanelSlot.Right,
            Icon = "Sync",
            Actions = [new PanelAction(Pull, "Download", "Pull from server"), new PanelAction(Push, "Upload", "Push to server")],
            CreateContent = ws =>
            {
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var address = new TextBlock { TextWrapping = TextWrapping.Wrap };
                address.Bind(TextBlock.ForegroundProperty, address.GetResourceObservable(DesktopTheme.MutedTextBrush));
                var pull = new Button { Content = "Pull from server" };
                var push = new Button { Content = "Push to server" };
                pull.Click += async (_, _) => await ws.ExecuteCommandAsync(Pull);
                push.Click += async (_, _) => await ws.ExecuteCommandAsync(Push);

                void Refresh()
                {
                    var configured = context.Services.Configuration.GetValue<string>(ServerUrl);
                    status.Text = !ws.HasWorkspace ? "Open a project to sync it."
                        : string.IsNullOrWhiteSpace(configured) ? "Not connected. Set the server address and token in Settings → Plugins."
                        : "Ready.";
                    address.Text = string.IsNullOrWhiteSpace(configured) ? string.Empty : configured;
                    pull.IsEnabled = push.IsEnabled = ws.HasWorkspace && !string.IsNullOrWhiteSpace(configured);
                }
                ws.Changed += (_, _) => Refresh();
                context.Services.Configuration.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
                Refresh();

                return new StackPanel { Margin = new Thickness(12), Spacing = 8, Children = { status, address, pull, push } };
            },
        });
    }
}
