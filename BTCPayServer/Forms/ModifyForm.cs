using System.ComponentModel;

namespace BTCPayServer.Forms;

public class ModifyForm
{
    public string Name { get; set; }

    [DisplayName("Form configuration (JSON)")]
    public string FormConfig { get; set; }

    [DisplayName("Allow form for public use")]
    public bool Public { get; set; }
}
