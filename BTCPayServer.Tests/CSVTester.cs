using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Tests;

public class CSVTester
{
    protected readonly Dictionary<string, int> _indexes;
    protected  readonly List<string[]> _lines;

    public CSVTester(string text)
    {
        var lines = text.Split("\r\n").ToList();
        var headers = lines[0].Split(',');
        _indexes = headers.Select((h,i) => (h,i)).ToDictionary(h => h.h, h => h.i);
        _lines = lines.Skip(1).ToList().Select(l => l.Split(',')).ToList();
    }
}
