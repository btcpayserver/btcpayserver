using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using McMaster.NETCore.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace BTCPayServer
{
    public static class ExtensionManager
    {
        public  const string BTCPayExtensionSuffix =".btcpay";
        private static readonly List<Assembly> _pluginAssemblies = new List<Assembly>();
        private static ILogger _logger;

        public static IMvcBuilder AddExtensions(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(typeof(ExtensionManager));
            var extensionsFolder = config.GetExtensionDir(DefaultConfiguration.GetNetworkType(config));
            var extensions = new List<IBTCPayServerExtension>();

            _logger.LogInformation($"Loading extensions from {extensionsFolder}");
            Directory.CreateDirectory(extensionsFolder);
            ExecuteCommands(extensionsFolder);
            List<(PluginLoader, Assembly, IFileProvider)> plugins = new List<(PluginLoader, Assembly, IFileProvider)>();
            foreach (var dir in Directory.GetDirectories(extensionsFolder))
            {
                var pluginName = Path.GetFileName(dir);

                var plugin = PluginLoader.CreateFromAssemblyFile(
                    Path.Combine(dir, pluginName + ".dll"), // create a plugin from for the .dll file
                    config =>
                        // this ensures that the version of MVC is shared between this app and the plugin
                        config.PreferSharedTypes = true);

                mvcBuilder.AddPluginLoader(plugin);
                var pluginAssembly = plugin.LoadDefaultAssembly();
                _pluginAssemblies.Add(pluginAssembly);
                var fileProvider = CreateEmbeddedFileProviderForAssembly(pluginAssembly);
                plugins.Add((plugin, pluginAssembly, fileProvider));
                extensions.AddRange(GetAllExtensionTypesFromAssembly(pluginAssembly)
                    .Select(GetExtensionInstanceFromType));
            }

            foreach (var extension in extensions)
            {
                _logger.LogInformation($"Adding and executing extension {extension.Identifier} - {extension.Version}");
                serviceCollection.AddSingleton(extension);
                extension.Execute(serviceCollection);
            }

            return mvcBuilder;
        }

        public static void UseExtensions(this IApplicationBuilder applicationBuilder)
        {
            foreach (var extension in applicationBuilder.ApplicationServices
                .GetServices<IBTCPayServerExtension>())
            {
                extension.Execute(applicationBuilder,
                    applicationBuilder.ApplicationServices);
            }

            var webHostEnvironment = applicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            List<IFileProvider> providers = new List<IFileProvider>() {webHostEnvironment.WebRootFileProvider};
            providers.AddRange(
                _pluginAssemblies
                    .Select(CreateEmbeddedFileProviderForAssembly));
            webHostEnvironment.WebRootFileProvider = new CompositeFileProvider(providers);
        }

        private static Type[] GetAllExtensionTypesFromAssembly(Assembly assembly)
        {
            return assembly.GetTypes().Where(type =>
                typeof(IBTCPayServerExtension).IsAssignableFrom(type) &&
                !type.IsAbstract).ToArray();
        }

        private static IBTCPayServerExtension GetExtensionInstanceFromType(Type type)
        {
            return (IBTCPayServerExtension)Activator.CreateInstance(type, Array.Empty<object>());
        }

        private static IFileProvider CreateEmbeddedFileProviderForAssembly(Assembly assembly)
        {
            return new EmbeddedFileProvider(assembly);
        }

        private static void ExecuteCommands(string extensionsFolder)
        {
            var pendingCommands = GetPendingCommands(extensionsFolder);
            foreach (var command in pendingCommands)
            {
                ExecuteCommand(command, extensionsFolder);
            }
            File.Delete(Path.Combine(extensionsFolder, "commands"));
        }

        private static void ExecuteCommand((string command, string extension) command, string extensionsFolder)
        {
            var dirName = Path.Combine(extensionsFolder, command.extension);
            switch (command.command)
            {
                case "delete":
                    if (Directory.Exists(dirName))
                    {
                        Directory.Delete(dirName, true);
                    }
                    break;
                case "install":
                    var fileName = dirName + BTCPayExtensionSuffix;
                    if (File.Exists(fileName))
                    {
                        ZipFile.ExtractToDirectory(fileName, dirName, true);
                        File.Delete(fileName);
                    }
                    break;
            }
        }

        public static (string command, string extension)[] GetPendingCommands(string extensionsFolder)
        {
            if (!File.Exists(Path.Combine(extensionsFolder, "commands")))
                return Array.Empty<(string command, string extension)>();
            var commands = File.ReadAllLines(Path.Combine(extensionsFolder, "commands"));
            return commands.Select(s =>
            {
                var split = s.Split(':');
                return (split[0].ToLower(CultureInfo.InvariantCulture), split[1]);
            }).ToArray();
        }

        public static void QueueCommands(string extensionsFolder, params ( string action, string extension)[] commands)
        {
            File.AppendAllLines(Path.Combine(extensionsFolder, "commands"),
                commands.Select((tuple) => $"{tuple.action}:{tuple.extension}"));
        }

        public static void CancelCommands(string extensionDir, string extension)
        {
            var cmds = GetPendingCommands(extensionDir).Where(tuple =>
                !tuple.extension.Equals(extension, StringComparison.InvariantCultureIgnoreCase)).ToArray();

            File.Delete(Path.Combine(extensionDir, "commands"));
            QueueCommands(extensionDir, cmds);

        }
    }
}
