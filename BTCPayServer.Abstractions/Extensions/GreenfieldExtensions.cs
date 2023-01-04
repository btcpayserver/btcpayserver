using System.Collections.Generic;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Abstractions.Extensions;

public static class GreenfieldExtensions
{
    public static IActionResult CreateValidationError(this ControllerBase controller, ModelStateDictionary modelState)
    {
        return controller.UnprocessableEntity(modelState.ToGreenfieldValidationError());
    }

    public static List<GreenfieldValidationError> ToGreenfieldValidationError(this ModelStateDictionary modelState)
    {
        List<GreenfieldValidationError> errors = new List<GreenfieldValidationError>();
        foreach (var error in modelState)
        {
            foreach (var errorMessage in error.Value.Errors)
            {
                errors.Add(new GreenfieldValidationError(error.Key, errorMessage.ErrorMessage));
            }
        }

        return errors;
    }

    public static IActionResult CreateAPIError(this ControllerBase controller, string errorCode, string errorMessage)
    {
        return controller.BadRequest(new GreenfieldAPIError(errorCode, errorMessage));
    }

    public static IActionResult CreateAPIError(this ControllerBase controller, int httpCode, string errorCode, string errorMessage)
    {
        return controller.StatusCode(httpCode, new GreenfieldAPIError(errorCode, errorMessage));
    }

    public static IActionResult CreateAPIPermissionError(this ControllerBase controller, string missingPermission, string message = null)
    {
        return controller.StatusCode(403, new GreenfieldPermissionAPIError(missingPermission, message));
    }
}
