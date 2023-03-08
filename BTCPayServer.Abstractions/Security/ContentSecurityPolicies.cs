using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin.Crypto;

namespace BTCPayServer.Security
{
    public class ConsentSecurityPolicy
    {
        public ConsentSecurityPolicy(string name, string value)
        {
            if (value.Contains(';', StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
            _Value = value;
            _Name = name;
        }


        private readonly string _Name;
        public string Name
        {
            get
            {
                return _Name;
            }
        }

        private readonly string _Value;
        public string Value
        {
            get
            {
                return _Value;
            }
        }


        public override bool Equals(object obj)
        {
            ConsentSecurityPolicy item = obj as ConsentSecurityPolicy;
            if (item == null)
                return false;
            return GetHashCode().Equals(item.GetHashCode());
        }
        public static bool operator ==(ConsentSecurityPolicy a, ConsentSecurityPolicy b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.GetHashCode() == b.GetHashCode();
        }

        public static bool operator !=(ConsentSecurityPolicy a, ConsentSecurityPolicy b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Value);
        }
    }
    public class ContentSecurityPolicies
    {
        public ContentSecurityPolicies()
        {

        }

        readonly HashSet<ConsentSecurityPolicy> _Policies = new HashSet<ConsentSecurityPolicy>();

        /// <summary>
        /// Allow a specific script as event handler
        /// </summary>
        /// <param name="script"></param>
        public void AllowUnsafeHashes(string script = null)
        {
            if (!allowUnsafeHashes)
            {
                Add("script-src", $"'unsafe-hashes'");
                allowUnsafeHashes = true;
            }
            if (script != null)
            {
                var sha = GetSha256(script);
                Add("script-src", $"'sha256-{sha}'");
            }
        }

        bool allowUnsafeHashes = false;
        /// <summary>
        /// Allow the injection of script tag with the following script
        /// </summary>
        /// <param name="script"></param>
        public void AllowInline(string script)
        {
            ArgumentNullException.ThrowIfNull(script);
            var sha = GetSha256(script);
            Add("script-src", $"'sha256-{sha}'");
        }
        static string GetSha256(string script)
        {
            return Convert.ToBase64String(Hashes.SHA256(Encoding.UTF8.GetBytes(script.Replace("\r\n", "\n", StringComparison.Ordinal))));
        }

        public void Add(string name, string value)
        {
            Add(new ConsentSecurityPolicy(name, value));
        }
        public void Add(ConsentSecurityPolicy policy)
        {
            _Policies.Add(policy);
        }

        public void UnsafeEval()
        {
            Add("script-src", "'unsafe-eval'");
        }

        public IEnumerable<ConsentSecurityPolicy> Rules => _Policies;
        public bool HasRules => _Policies.Count != 0;

        public override string ToString()
        {
            StringBuilder value = new StringBuilder();
            bool firstGroup = true;
            foreach (var group in Rules.GroupBy(r => r.Name))
            {
                if (!firstGroup)
                {
                    value.Append(';');
                }
                HashSet<string> values = new HashSet<string>();
                List<string> valuesList = new List<string>();
                values.Add(group.Key);
                valuesList.Add(group.Key);
                foreach (var v in group)
                {
                    if (values.Add(v.Value))
                        valuesList.Add(v.Value);
                }
                value.Append(String.Join(" ", valuesList.OfType<object>().ToArray()));
                firstGroup = false;
            }
            return value.ToString();
        }
    }
}
