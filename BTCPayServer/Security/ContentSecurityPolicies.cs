using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        public void Add(string name, string value)
        {
            Add(new ConsentSecurityPolicy(name, value));
        }
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
            foreach (var group in Rules.GroupBy(r => r.Name))
            {
                if (!firstGroup)
                {
                    value.Append(';');
                }
                HashSet<string> values = new HashSet<string>();
                values.Add(group.Key);
                foreach (var v in group)
                {
                    values.Add(v.Value);
                }
                value.Append(String.Join(" ", values.OfType<object>().ToArray()));
                firstGroup = false;
            }
            return value.ToString();
        }

        internal void Clear()
        {
            _Policies.Clear();
        }
    }
}
