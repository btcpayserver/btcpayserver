namespace BTCPayServer.SSH
{
    public class SSHCommandResult
    {
        public int ExitStatus { get; internal set; }
        public string Output { get; internal set; }
        public string Error { get; internal set; }
    }
}
