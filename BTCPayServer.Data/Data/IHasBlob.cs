using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public interface IHasBlob<T>
    {
        [Obsolete("Use Blob2 instead")]
        byte[] Blob { get; set; }
        string Blob2 { get; set; }
    }
    public interface IHasBlob
    {
        [Obsolete("Use Blob2 instead")]
        byte[] Blob { get; set; }
        string Blob2 { get; set; }
        public Type Type { get; set; }
    }
    public interface IHasBlobUntyped
    {
        [Obsolete("Use Blob2 instead")]
        byte[] Blob { get; set; }
        string Blob2 { get; set; }
    }
}
