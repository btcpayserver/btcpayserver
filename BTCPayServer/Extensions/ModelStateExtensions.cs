using System;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Internal;

namespace BTCPayServer
{
    public static class ModelStateExtensions
    {
        public static void AddModelError<TModel, TProperty>(
            this ModelStateDictionary modelState, 
            Expression<Func<TModel, TProperty>> ex, 
            string message
        )
        {
            var key = ExpressionHelper.GetExpressionText(ex);
            modelState.AddModelError(key, message);
        }
        
        public static void AddModelError<TModel, TProperty>(this TModel source,        
            Expression<Func<TModel, TProperty>> ex, 
            string message,
            ModelStateDictionary modelState)
        {
            var key = ExpressionHelper.GetExpressionText(ex);
            modelState.AddModelError(key, message);
        }
    }
}
