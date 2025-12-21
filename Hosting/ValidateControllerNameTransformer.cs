using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Hosting
{
    public class ValidateControllerNameTransformer : IOutboundParameterTransformer
    {
        public static void Register(IServiceCollection services)
        {
            services.AddRouting(opts =>
            {
                opts.ConstraintMap["validate"] = typeof(ValidateControllerNameTransformer);
            });
            services.AddTransient<IApplicationModelProvider, ApplicitionModelProvider>();
            services.AddSingleton<ControllerNameList>();
        }
        public class ControllerNotFoundException : Exception
        {
            public ControllerNotFoundException(string controllerName) : base($"The controller {controllerName} has not been found")
            {

            }
        }
        public class ControllerNameList : HashSet<string>
        {

        }
        public class ApplicitionModelProvider : IApplicationModelProvider
        {
            public ApplicitionModelProvider(ControllerNameList list)
            {
                List = list;
            }
            public int Order => 0;

            public ControllerNameList List { get; }

            public void OnProvidersExecuted(ApplicationModelProviderContext context)
            {
                if (List.Count != 0)
                    return;
                lock (List)
                {
                    foreach (var controller in context.Result.Controllers)
                    {
                        List.Add(controller.ControllerName);
                    }
                }
            }

            public void OnProvidersExecuting(ApplicationModelProviderContext context)
            {

            }
        }

        private readonly ControllerNameList list;
        public ValidateControllerNameTransformer(ControllerNameList list)
        {
            this.list = list;
        }
        public string TransformOutbound(object value)
        {
            if (value is not string str)
                return null;
            if (!list.Contains(str))
                throw new ControllerNotFoundException(str);
            return str;
        }
    }


}
