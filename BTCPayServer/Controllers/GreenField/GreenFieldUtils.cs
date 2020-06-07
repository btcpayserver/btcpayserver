using System;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    public static class GreenFieldUtils
    {
        public static IActionResult GetValidationResponse(this ControllerBase controller)
        {
            return controller.UnprocessableEntity( new ValidationProblemDetails(controller.ModelState));
        }
        public static IActionResult GetExceptionResponse(this ControllerBase controller, Exception e)
        {
            return GetGeneralErrorResponse(controller, e.Message);
        }
        
        public static IActionResult GetGeneralErrorResponse(this ControllerBase controller, string error)
        {
            return controller.BadRequest( new ProblemDetails()
            {
                Detail = error
            });
        }
        
    }
}
