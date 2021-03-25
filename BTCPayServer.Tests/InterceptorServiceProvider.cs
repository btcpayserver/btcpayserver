using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Tests
{
    public class InterceptorServiceProvider : IServiceProvider
    {
        private IServiceProvider _serviceProvider;
        private DefaultHttpContext _context;
        class HttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext HttpContext { get; set; }
        }
        public InterceptorServiceProvider(IServiceProvider serviceProvider, DefaultHttpContext context)
        {
            _serviceProvider = serviceProvider;
            _context = context;
        }

        public object GetService(Type serviceType)
        {
            // The default ServiceProvider gives a IHttpContext tied to AsyncLocal, which has weird behavior
            // if the test code start using async/await...
            // SignInManager should be one of the only problematic dependencies relying on the IHttpContext
            if (serviceType == typeof(SignInManager<ApplicationUser>))
                return new SignInManager<ApplicationUser>(
                        _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                        new HttpContextAccessor() { HttpContext = _context },
                        _serviceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                        _serviceProvider.GetRequiredService<IOptions<IdentityOptions>>(),
                        _serviceProvider.GetRequiredService<ILogger<SignInManager<ApplicationUser>>>(),
                        _serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>(),
                        _serviceProvider.GetRequiredService<IUserConfirmation<ApplicationUser>>());
            return _serviceProvider.GetService(serviceType);
        }
    }
}
