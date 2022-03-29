using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services;

public class DefaultSwaggerProvider: ISwaggerProvider
{
    private readonly IFileProvider _fileProvider;

    public DefaultSwaggerProvider(IWebHostEnvironment webHostEnvironment)
    {
                
        _fileProvider = webHostEnvironment.WebRootFileProvider;
    }
    public async Task<JObject> Fetch()
    {
                
        JObject json = new JObject();
        var directoryContents = _fileProvider.GetDirectoryContents("swagger/v1");
        foreach (IFileInfo fi in directoryContents)
        {
            await using var stream = fi.CreateReadStream();
            using var reader = new StreamReader(fi.CreateReadStream());
            json.Merge(JObject.Parse(await reader.ReadToEndAsync()));
        }

        return json;
    }
}
