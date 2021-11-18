dotnet publish -c Release -o bin/publish/BTCPayServer.Plugins.LNbank
dotnet run -p ../BTCPayServer.PluginPacker bin/publish/BTCPayServer.Plugins.LNbank BTCPayServer.Plugins.LNbank bin/packed
