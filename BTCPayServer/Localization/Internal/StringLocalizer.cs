using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Localization.Internal
{
    internal class StringLocalizer : IStringLocalizer
    {
        private IStringLocalizer _localizer;

        public StringLocalizer(IHostingEnvironment env, IStringLocalizerFactory factory)
        {
            _localizer = factory.Create(string.Empty, PathHelpers.GetApplicationRoot());
        }

        public LocalizedString this[string name] => _localizer[name];

        public LocalizedString this[string name, params object[] arguments] => _localizer[name, arguments];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _localizer.GetAllStrings(includeParentCultures);

        public IStringLocalizer WithCulture(CultureInfo culture) => _localizer.WithCulture(culture);
    }
}
