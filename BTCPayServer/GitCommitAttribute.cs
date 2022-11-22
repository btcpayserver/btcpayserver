using System;

namespace BTCPayServer
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class GitCommitAttribute : Attribute
    {
        public string SHA
        {
            get;
        }
        public string ShortSHA => SHA.Substring(0, 9);
        public GitCommitAttribute(string sha)
        {
            SHA = sha;
        }
    }
}
