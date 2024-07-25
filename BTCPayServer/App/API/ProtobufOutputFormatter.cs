using System;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace BTCPayServer.App.API;

public class ProtobufOutputFormatter :  OutputFormatter
{
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var response = context.HttpContext.Response;
        var responseHeaders = response.Headers;
        var responseContentType = response.ContentType;
        if (string.IsNullOrEmpty(responseContentType))
        {
            responseContentType = "application/octet-stream";
        }

        responseHeaders[HeaderNames.ContentType] = responseContentType;

        if (context.Object is IMessage v)
        {
            var responseBytes = v.ToByteArray();
            await response.Body.WriteAsync(responseBytes, 0, responseBytes.Length);
        }
        else if (context.Object is byte[] bytes)
        {
            await response.Body.WriteAsync(bytes, 0, bytes.Length);
        }
        else if (context.Object is Stream stream)
        {
            await stream.CopyToAsync(response.Body);
        }
    }
}