using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services
{
    public class LocalizerFactory : IStringLocalizerFactory, IHtmlLocalizerFactory
    {
        internal readonly Logs _logs;
        private readonly LocalizerService _localizerService;

        class StringLocalizer : IStringLocalizer, IHtmlLocalizer
        {
            private Type _resourceSource;
            private string _baseName;
            private string _location;
            private LocalizerFactory _Factory;

            public StringLocalizer(LocalizerFactory factory, Type resourceSource)
            {
                _Factory = factory;
                _resourceSource = resourceSource;
            }
            Logs Logs => _Factory._logs;


            public StringLocalizer(LocalizerFactory jsonStringLocalizerFactory, string baseName, string location)
            {
                _Factory = jsonStringLocalizerFactory;
                _baseName = baseName;
                _location = location;
            }
            Translations Translations => _Factory._localizerService.Translations;
            public LocalizedString this[string name]
            {
                get
                {
                    //Logs.PayServer.LogInformation($"this[name] with name:{name}, location:{_location}, baseName:{_baseName}, resource:{_resourceSource}");
                    Translations.Records.TryGetValue(name, out var result);
                    result = result ?? name;
                    return new LocalizedString(name, result);
                }
            }

            public LocalizedString this[string name, params object[] arguments]
            {
                get
                {
                    //var args = String.Join(", ", arguments.Select((a, i) => $"arg[{i}]:{a}").ToArray());
                    //Logs.PayServer.LogInformation($"this[name, arguments] with name:{name}, {args}, location:{_location}, baseName:{_baseName}, resource:{_resourceSource}");
                    Translations.Records.TryGetValue(name, out var result);
                    result = result ?? name;
                    return new LocalizedString(name, string.Format(result, arguments));
                }
            }

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            {
                //Logs.PayServer.LogInformation($"GetAllStrings");
                return Translations.Records.Select(r => new LocalizedString(r.Key, r.Value));
            }

            LocalizedHtmlString IHtmlLocalizer.this[string name]
            {
                get
                {
                    //Logs.PayServer.LogInformation($"[HTML]: this[name] with name:{name}, location:{_location}, baseName:{_baseName}, resource:{_resourceSource}");
                    Translations.Records.TryGetValue(name, out var result);
                    result = result ?? name;
                    return new LocalizedHtmlString(name, result);
                }
            }

            LocalizedHtmlString IHtmlLocalizer.this[string name, params object[] arguments]
            {
                get
                {
                    //var args = String.Join(", ", arguments.Select((a, i) => $"arg[{i}]:{a}").ToArray());
                    //Logs.PayServer.LogInformation($"[HTML]:this[name, arguments] with name:{name}, {args}, location:{_location}, baseName:{_baseName}, resource:{_resourceSource}");
                    Translations.Records.TryGetValue(name, out var result);
                    result = result ?? name;
                    return new LocalizedHtmlString(name, result, true, arguments);
                }
            }

            public LocalizedString GetString(string name)
            {
                //Logs.PayServer.LogInformation($"[HTML] GetString(name):");
                return this[name];
            }

            public LocalizedString GetString(string name, params object[] arguments)
            {
                //var args = String.Join(", ", arguments.Select((a, i) => $"arg[{i}]:{a}").ToArray());
                Logs.PayServer.LogInformation($"[HTML] GetString(name,args):");
                return this[name, arguments];
            }
        }
        public LocalizerFactory(Logs logs, LocalizerService localizerService)
        {
            _logs = logs;
            _localizerService = localizerService;
        }
        public IStringLocalizer Create(Type resourceSource)
        {
            return new StringLocalizer(this, resourceSource);
        }

        public IStringLocalizer Create(string baseName, string location)
        {
            return new StringLocalizer(this, baseName, location);
        }

        IHtmlLocalizer IHtmlLocalizerFactory.Create(Type resourceSource)
        {
            return new StringLocalizer(this, resourceSource);
        }

        IHtmlLocalizer IHtmlLocalizerFactory.Create(string baseName, string location)
        {
            return new StringLocalizer(this, baseName, location);
        }
    }
}
