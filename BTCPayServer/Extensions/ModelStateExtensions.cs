using System;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer
{
    public static class ModelStateExtensions
    {
        
        public static void AddModelError<TModel, TProperty>(this TModel source,        
            Expression<Func<TModel, TProperty>> ex, 
            string message,
            Controller controller)
        {
            var provider = (ModelExpressionProvider)controller.HttpContext.RequestServices.GetService(typeof(ModelExpressionProvider));
            var key = provider.GetExpressionText(ex);
            controller.ModelState.AddModelError(key, message);
        }
    }
}
