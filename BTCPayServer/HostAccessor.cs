using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

public class HostAccessor
{
    private IWebHost _host;
    private TaskCompletionSource<IWebHost> _taskCompletionSource = new();
    public IWebHost Host
    {
        set
        {
            _host = value;
            _taskCompletionSource.SetResult(_host);
        }
    }

    public Task<IWebHost> GetHost()
    {
        return _taskCompletionSource.Task;

    }
}
