#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Abstractions.Models;

public class UploadImageResultModel
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public IStoredFile? StoredFile { get; set; }
}
