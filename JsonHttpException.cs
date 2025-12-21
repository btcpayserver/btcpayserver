#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
