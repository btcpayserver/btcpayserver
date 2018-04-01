using Microsoft.AspNetCore.Mvc;
using NBitcoin.Payment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    public class PaymentRequestActionResult : IActionResult
    {
        PaymentRequest req;
        public PaymentRequestActionResult(PaymentRequest req)
        {
            this.req = req;
        }
        public Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers["Content-Transfer-Encoding"] = "binary";
            context.HttpContext.Response.ContentType = "application/bitcoin-paymentrequest";
            req.WriteTo(context.HttpContext.Response.Body);
            return Task.CompletedTask;
        }
    }
    public class PaymentAckActionResult : IActionResult
    {
        PaymentACK req;
        public PaymentAckActionResult(PaymentACK req)
        {
            this.req = req;
        }
        public Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers["Content-Transfer-Encoding"] = "binary";
            context.HttpContext.Response.ContentType = "application/bitcoin-paymentack";
            req.WriteTo(context.HttpContext.Response.Body);
            return Task.CompletedTask;
        }
    }
}
