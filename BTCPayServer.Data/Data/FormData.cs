using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Data;

public class FormData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Config { get; set; }
}
