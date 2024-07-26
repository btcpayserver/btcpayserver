using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace BTCPayServer.App.API;

public class ProtobufInputFormatter : InputFormatter
{
    public ProtobufInputFormatter()
    {
        SupportedMediaTypes.Add("application/octet-stream");
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        throw new NotImplementedException();
    }

    public override bool CanRead(InputFormatterContext context)
    {
        return base.CanRead(context);
    }

    public override async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
  
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var request = context.HttpContext.Request;
        if (request.ContentLength == 0)
        {
            return await InputFormatterResult.SuccessAsync(null);
        }

        if (context.HttpContext.Request.ContentType != "application/octet-stream")
        {
            return await InputFormatterResult.FailureAsync();
        }

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var messageType = context.ModelType;
        var message = (IMessage)Activator.CreateInstance(messageType);
        message.MergeFrom(bytes);
        return await InputFormatterResult.SuccessAsync(message);
    }
}
