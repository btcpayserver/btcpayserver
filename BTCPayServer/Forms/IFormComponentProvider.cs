using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public interface IFormComponentProvider
{
    public string CanHandle(Field field);
}
