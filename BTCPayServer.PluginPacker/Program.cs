using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.PluginPacker
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: btcpay-plugin [directory of compiled plugin] [name of plugin] [packed plugin output directory]");
                return;
            }
            var directory = args[0];
            var name = args[1];
            var outputDir = args[2];
            var outputFile = Path.Combine(outputDir, name);
            var rootDLLPath = Path.Combine(directory, name +".dll");
            if (!File.Exists(rootDLLPath) )
            {
                throw new Exception($"{rootDLLPath} could not be found");
            }

            var assembly = Assembly.LoadFrom(rootDLLPath);
            var extension = GetAllExtensionTypesFromAssembly(assembly).FirstOrDefault();
            if (extension is null)
            {
                throw new Exception($"{rootDLLPath} is not a valid plugin");
            }

            var loadedPlugin = (IBTCPayServerPlugin)Activator.CreateInstance(extension);
            var json = JsonSerializer.Serialize(loadedPlugin);
            Directory.CreateDirectory(outputDir);
            if (File.Exists(outputFile + ".btcpay"))
            {
                File.Delete(outputFile + ".btcpay");
            }
            ZipFile.CreateFromDirectory(directory, outputFile + ".btcpay", CompressionLevel.Optimal, false);
            File.WriteAllText(outputFile + ".btcpay.json", json);
        }
        
        private static Type[] GetAllExtensionTypesFromAssembly(Assembly assembly)
        {
            return assembly.GetTypes().Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) &&
                !type.IsAbstract).ToArray();
        }
    }
}
