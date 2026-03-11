// We don't want to break plugins, so let's not fix the namespace.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTCPayServer.Services;

public interface IDefaultTranslationProvider
{
    Task<KeyValuePair<string, string>[]> GetDefaultTranslations();
}
