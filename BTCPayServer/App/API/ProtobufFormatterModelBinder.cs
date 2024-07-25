using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.App.API;

public class ProtobufFormatterModelBinder : BodyModelBinder
{
    private static readonly IInputFormatter[] _inputFormatter = [new ProtobufInputFormatter()];

    public ProtobufFormatterModelBinder(ILoggerFactory loggerFactory, IHttpRequestStreamReaderFactory readerFactory) :
        base(_inputFormatter, readerFactory, loggerFactory)
    {
    }
}