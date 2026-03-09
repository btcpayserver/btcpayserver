using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Hosting;

public class SetCultureMiddleware(RequestDelegate next)
{

    public async Task Invoke(HttpContext httpContext)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        await next(httpContext);
    }
}
