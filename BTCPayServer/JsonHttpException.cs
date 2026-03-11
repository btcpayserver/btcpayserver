#nullable enable
using System;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer
{
    public class JsonHttpException : Exception
    {
        public JsonHttpException(IActionResult actionResult)
        {
            ActionResult = actionResult;
        }

        public IActionResult ActionResult { get; }
    }
}
