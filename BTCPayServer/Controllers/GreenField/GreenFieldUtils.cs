using System.Collections.Generic;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Controllers.GreenField
{
    public static class GreenFieldUtils
    {
        public static IActionResult CreateValidationError(this ControllerBase controller, ModelStateDictionary modelState)
        {
            List<GreenfieldValidationError> errors = new List<GreenfieldValidationError>();
            foreach (var error in modelState)
            {
                foreach (var errorMessage in error.Value.Errors)
                {
                    errors.Add(new GreenfieldValidationError(error.Key, errorMessage.ErrorMessage));
                }
            }
            return controller.UnprocessableEntity(errors.ToArray());
        }
        public static IActionResult CreateAPIError(this ControllerBase controller, string errorCode, string errorMessage)
        {
            return controller.BadRequest(new GreenfieldAPIError(errorCode, errorMessage));
        }
    }
}
