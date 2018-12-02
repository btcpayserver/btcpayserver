using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Security
{
    public class ConsentSecurityPolicy
    {
        public ConsentSecurityPolicy(string name, string value)
        {
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
        HashSet<ConsentSecurityPolicy> _Policies = new HashSet<ConsentSecurityPolicy>();
        public void Add(ConsentSecurityPolicy policy)
        {
            if (_Policies.Any(p => p.Name == policy.Name && p.Value == policy.Name))
                return;
            _Policies.Add(policy);
        }

        public IEnumerable<ConsentSecurityPolicy> Rules => _Policies;
        public bool HasRules => _Policies.Count != 0;

        public override string ToString()
        {
            StringBuilder value = new StringBuilder();
            bool firstGroup = true;
            foreach(var group in Rules.GroupBy(r => r.Name))
            {
                if (!firstGroup)
                {
                    value.Append(';');
                }
                List<string> values = new List<string>();
                values.Add(group.Key);
                foreach (var v in group)
                {
                    values.Add(v.Value);
                }
                foreach(var i in authorized)
                {
                    values.Add(i);
                }
                value.Append(String.Join(" ", values.OfType<object>().ToArray()));
                firstGroup = false;
            }
            return value.ToString();
        }

        internal void Clear()
        {
            authorized.Clear();
            _Policies.Clear();
        }

        HashSet<string> authorized = new HashSet<string>();
        internal void AddAllAuthorized(string v)
        {
            authorized.Add(v);
        }

        public IEnumerable<string> Authorized => authorized;
    }
}
