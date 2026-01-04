// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Dotnet
{
    /// <summary>
    /// Extends the MVC builder.
    /// </summary>
    public static class MvcPluginExtensions
    {
        /// <summary>
        /// Loads controllers and razor pages from a plugin assembly.
        /// <para>
        /// This creates a loader with <see cref="PluginConfig.PreferSharedTypes" /> set to <c>true</c>.
        /// If you need more control over shared types, use <see cref="AddPluginLoader" /> instead.
        /// </para>
        /// </summary>
        /// <param name="mvcBuilder">The MVC builder</param>
        /// <param name="assemblyFile">Full path the main .dll file for the plugin.</param>
        /// <returns>The builder</returns>
        public static IMvcBuilder AddPluginFromAssemblyFile(this IMvcBuilder mvcBuilder, string assemblyFile)
        {
            var plugin = PluginLoader.CreateFromAssemblyFile(
                assemblyFile, // create a plugin from for the .dll file
                config =>
                    // this ensures that the version of MVC is shared between this app and the plugin
                    config.PreferSharedTypes = true);

            return mvcBuilder.AddPluginLoader(plugin);
        }

        /// <summary>
        /// Loads controllers and razor pages from a plugin loader.
        /// <para>
        /// In order for this to work, the PluginLoader instance must be configured to share the types
        /// <see cref="ProvideApplicationPartFactoryAttribute" /> and <see cref="RelatedAssemblyAttribute" />
        /// (comes from Microsoft.AspNetCore.Mvc.Core.dll). The easiest way to ensure that is done correctly
        /// is to set <see cref="PluginConfig.PreferSharedTypes" /> to <c>true</c>.
        /// </para>
        /// </summary>
        /// <param name="mvcBuilder">The MVC builder</param>
        /// <param name="pluginLoader">An instance of PluginLoader.</param>
        /// <returns>The builder</returns>
        public static IMvcBuilder AddPluginLoader(this IMvcBuilder mvcBuilder, PluginLoader pluginLoader)
        {
            var pluginAssembly = pluginLoader.LoadDefaultAssembly();

            // This loads MVC application parts from plugin assemblies
            var partFactory = ApplicationPartFactory.GetApplicationPartFactory(pluginAssembly);
            foreach (var part in partFactory.GetApplicationParts(pluginAssembly))
            {
                mvcBuilder.PartManager.ApplicationParts.Add(part);
            }

            // This piece finds and loads related parts, such as MvcAppPlugin1.Views.dll.
            var relatedAssembliesAttrs = pluginAssembly.GetCustomAttributes<RelatedAssemblyAttribute>();
            foreach (var attr in relatedAssembliesAttrs)
            {
                var assembly = pluginLoader.LoadAssembly(attr.AssemblyFileName);
                partFactory = ApplicationPartFactory.GetApplicationPartFactory(assembly);
                foreach (var part in partFactory.GetApplicationParts(assembly))
                {
                    mvcBuilder.PartManager.ApplicationParts.Add(part);
                }
            }

            return mvcBuilder;
        }
    }
}
