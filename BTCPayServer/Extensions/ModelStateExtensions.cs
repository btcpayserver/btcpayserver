using System;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
#if NETCOREAPP21
using Microsoft.AspNetCore.Mvc.ViewFeatures.Internal;
#else
using Microsoft.AspNetCore.Mvc.ViewFeatures;
#endif

namespace BTCPayServer
{
    public static class ModelStateExtensions
    {
        
        public static void AddModelError<TModel, TProperty>(this TModel source,        
            Expression<Func<TModel, TProperty>> ex, 
            string message,
            Controller controller)
        {
#if NETCOREAPP21
            var key = ExpressionHelper.GetExpressionText(ex);
#else
            var provider = (ModelExpressionProvider)controller.HttpContext.RequestServices.GetService(typeof(ModelExpressionProvider));
            var key = provider.GetExpressionText(ex);
#endif
            controller.ModelState.AddModelError(key, message);
        }
    }
}
