using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Dotnet;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace BTCPayServer.PluginPacker
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: btcpay-plugin [directory of compiled plugin] [name of plugin] [packed plugin output directory]");
                return;
            }
            var directory = args[0];
            var name = args[1];
            var outputDir = Path.Combine(args[2], name);
            var outputFile = Path.Combine(outputDir, name);
            var rootDLLPath = Path.GetFullPath(Path.Combine(directory, name + ".dll"));
            if (!File.Exists(rootDLLPath))
            {
                throw new Exception($"{rootDLLPath} could not be found");
            }

            var plugin = PluginLoader.CreateFromAssemblyFile(rootDLLPath, false, new[] { typeof(IBTCPayServerPlugin) }, o => o.PreferSharedTypes = true);
            var assembly = plugin.LoadAssembly(name);
            var extension = GetAllExtensionTypesFromAssembly(assembly).FirstOrDefault();
            if (extension is null)
            {
                throw new Exception($"{rootDLLPath} is not a valid plugin");
            }

            var loadedPlugin = (IBTCPayServerPlugin)Activator.CreateInstance(extension);
            var json = JsonSerializer.Serialize(loadedPlugin);
            Directory.CreateDirectory(outputDir);
            outputDir = Path.Combine(outputDir, loadedPlugin.Version.ToString());
            Directory.CreateDirectory(outputDir);
            outputFile = Path.Combine(outputDir, name);
            if (File.Exists(outputFile + ".btcpay"))
            {
                File.Delete(outputFile + ".btcpay");
            }
            ZipFile.CreateFromDirectory(directory, outputFile + ".btcpay", CompressionLevel.Optimal, false);
            await File.WriteAllTextAsync(outputFile + ".btcpay.json", json);

            var sha256sums = new StringBuilder();
            sha256sums.AppendLine(
                $"{Encoders.Hex.EncodeData(Hashes.SHA256(Encoding.UTF8.GetBytes(json)))} {name}.btcpay.json");

            sha256sums.AppendLine(
                $"{Encoders.Hex.EncodeData(Hashes.SHA256(await File.ReadAllBytesAsync(outputFile + ".btcpay")))} {name}.btcpay");

            var sha256dirs = Path.Combine(outputDir, "SHA256SUMS");
            if (File.Exists(sha256dirs))
            {
                File.Delete(sha256dirs);
            }
            await File.WriteAllTextAsync(sha256dirs, sha256sums.ToString());

            Console.WriteLine($"Created {outputFile}.btcpay at {directory}");
        }

        private static Type[] GetAllExtensionTypesFromAssembly(Assembly assembly)
        {
            return GetLoadableTypes(assembly).Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) &&
                !type.IsAbstract).ToArray();
        }
        static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
        }
    }
}
