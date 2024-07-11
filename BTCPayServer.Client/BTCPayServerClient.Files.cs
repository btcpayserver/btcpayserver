using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<FileData[]> GetFiles(CancellationToken token = default)
    {
        return await SendHttpRequest<FileData[]>("api/v1/files", null, HttpMethod.Get, token);
    }

    public virtual async Task<FileData> GetFile(string fileId, CancellationToken token = default)
    {
        return await SendHttpRequest<FileData>($"api/v1/files/{fileId}", null, HttpMethod.Get, token);
    }

    public virtual async Task<FileData> UploadFile(string filePath, string mimeType, CancellationToken token = default)
    {
        return await UploadFileRequest<FileData>("api/v1/files", filePath, mimeType, "file", HttpMethod.Post, token);
    }

    public virtual async Task DeleteFile(string fileId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/files/{fileId}", null, HttpMethod.Delete, token);
    }
}
