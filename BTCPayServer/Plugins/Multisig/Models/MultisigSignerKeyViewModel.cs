namespace BTCPayServer.Plugins.Multisig.Models;

public class MultisigSignerKeyViewModel
{
    public string CryptoCode { get; set; }
    public string RequestId { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public string ScriptType { get; set; }
    public string DisplayAccountKey { get; set; }
    public string AccountKeyPath { get; set; }
    public string InputMethod { get; set; }
}
