# toucan-plugins

Plugins for [Toucan](https://toucan.rasyid.dev) that live outside the application's repository. They are built only against the
published SDK packages (`Toucan.Plugins.Abstractions`, `Toucan.Plugins.Avalonia`, `Toucan.Plugins.Testing`), which is the same
position a third-party author is in.

## REST connector (`src/Toucan.Connector.Rest`)

Pulls translations from a JSON-over-HTTP service into the open project and pushes changes back.

- **Settings** (Settings → Plugins): server address and API token. The token is kept in Toucan's secret store.
- **Commands:** *Pull From Server* (`Mod+Alt+L`) and *Push To Server*, in the command palette and the Extensions menu, with
  shortcuts you can reassign. Both are unavailable until a project is open and disconnected until the server address is set.
- **Panel:** a *Server* panel on the right with the same actions (desktop only; the CLI never loads it).
- **Safety:** a pull is one undoable edit and never overwrites text you changed; a push writes each translation only if the
  server still holds the revision that was read.

Protocol: `GET {base}/translations` returns `[{key, language, text, revision}]`; `PUT {base}/translations/{key}/{language}` with
`{ "text": … }` and `If-Match: "{revision}"` writes one translation and answers `412` when the revision is stale.

## Build and test

The SDK packages come from `./feed` (`dotnet pack` in the Toucan repository writes there until they are published):

```sh
dotnet test Toucan.Plugins.slnx   # uses Toucan.Plugins.Testing: registration, activation, edits, cancellation
./build.sh                        # lays the plugin out in dist/toucan-plugins.rest-connector
```

Install by copying that folder to `Documents/Toucan/plugins` (or run the CLI with `TOUCAN_PLUGINS_DIR=$PWD/dist`) and trust it.
Needs Toucan 0.23 or newer.
