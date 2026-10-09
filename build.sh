#!/bin/sh
# Builds the connector against the SDK packages in ./feed and lays it out as a Toucan plugin folder in dist/.
set -e
cd "$(dirname "$0")"
rm -rf dist
dotnet build src/Toucan.Connector.Rest -c Release -o dist/toucan-plugins.rest-connector
echo "Plugin folder: $(pwd)/dist/toucan-plugins.rest-connector"
echo "Install: copy it into Documents/Toucan/plugins (or set TOUCAN_PLUGINS_DIR to $(pwd)/dist), then trust it in Settings → Plugins."
