using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Localization
{
    public class JsonStringLocalizerFactory : IStringLocalizerFactory
    {
        private JsonStringLocalizer _jsonStringLocalizer;
        private readonly string _wwwroot;

        public JsonStringLocalizerFactory(IHostingEnvironment hostingEnvironment)
        {
            _wwwroot = ((HostingEnvironment)hostingEnvironment).WebRootPath;
        }

        public IStringLocalizer Create(Type resourceSource)
        {
            if (resourceSource == null)
            {
                throw new ArgumentNullException(nameof(resourceSource));
            }

            var typeInfo = resourceSource.GetTypeInfo();
            var assembly = typeInfo.Assembly;
            var resourcesPath = Path.Combine(_wwwroot, GetResourcePath(assembly));

            return CreateOrGetJsonStringLocalizer(resourcesPath);
        }

        public IStringLocalizer Create(string baseName, string location)
        {
            if (baseName == null)
            {
                throw new ArgumentNullException(nameof(baseName));
            }

            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            var assemblyName = new AssemblyName(location);
            var assembly = Assembly.Load(assemblyName);
            var resourcesPath = Path.Combine(_wwwroot, GetResourcePath(assembly));

            return CreateOrGetJsonStringLocalizer(resourcesPath);
        }

        protected virtual JsonStringLocalizer CreateOrGetJsonStringLocalizer(
            string resourcesPath)
        {
            if (_jsonStringLocalizer == null)
            {
                _jsonStringLocalizer = new JsonStringLocalizer(resourcesPath);
            }

            return _jsonStringLocalizer;
        }

        private string GetResourcePath(Assembly assembly)
        {
            var resourceLocationAttribute = assembly.GetCustomAttribute<ResourceLocationAttribute>();

            return resourceLocationAttribute == null
                ? "Resources"
                : resourceLocationAttribute.ResourceLocation;
        }
    }
}
