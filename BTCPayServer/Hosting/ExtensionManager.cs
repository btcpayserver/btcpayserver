using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using BTCPayServer.Contracts;
using BTCPayServer.Controllers;
using McMaster.NETCore.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace BTCPayServer
{
    public static class ExtensionManager
    {
        private static readonly List<Assembly> _pluginAssemblies = new List<Assembly>();

        public static IMvcBuilder AddExtensions(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            string extensionsFolder)
        {
            var extensions = new List<IBTCPayServerExtension>();

            Console.WriteLine($"Loading extensions from {extensionsFolder}");
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
            List<IFileProvider> providers = new List<IFileProvider>() { webHostEnvironment.WebRootFileProvider};
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
            if (File.Exists(Path.Combine(extensionsFolder, "commands")))
            {
                var commands = File.ReadAllLines(Path.Combine(extensionsFolder, "commands"));
                foreach (var command in commands)
                {
                    ExecuteCommand(command, extensionsFolder);
                }
            }

            File.Delete(Path.Combine(extensionsFolder, "commands"));
        }

        private static void ExecuteCommand(string command, string extensionsFolder)
        {
            var split = command.Split(":");
            switch (split[0].ToLower())
            {
                case "delete":
                    if (Directory.Exists(Path.Combine(extensionsFolder, split[1])))
                    {
                        Directory.Delete(Path.Combine(extensionsFolder, split[1]), true);
                    }

                    break;
                case "install":
                    if (File.Exists(Path.Combine(extensionsFolder, split[1])))
                    {
                        var filedest = Path.Combine(extensionsFolder, split[1]);
                        ZipFile.ExtractToDirectory(filedest,
                            filedest.TrimEnd(".btcpay", StringComparison.InvariantCultureIgnoreCase), true);
                        System.IO.File.Delete(filedest);
                    }

                    break;
            }
        }

        public static void QueueCommands(string extensionsFolder, params ( string action, string val)[] commands)
        {
            File.AppendAllLines(Path.Combine(extensionsFolder, "commands"),
                commands.Select((tuple) => $"{tuple.action}:{tuple.val}"));
        }
    }
}
