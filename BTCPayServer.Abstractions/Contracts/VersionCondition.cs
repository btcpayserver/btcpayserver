#nullable  enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace BTCPayServer.Abstractions.Contracts;

public abstract class VersionCondition
{
    public class Any(VersionCondition[] conditions) : VersionCondition
    {
        public static bool TryParse(string str, [MaybeNullWhen(false)] out Any condition)
        {
            condition = null;
            var any = str.Trim().Split("||", StringSplitOptions.RemoveEmptyEntries);
            if (any.Length is 0 or 1)
            {
                return false;
            }
            var conditions = new VersionCondition[any.Length];
            var i = 0;
            foreach (var item in any)
            {
                if (!VersionCondition.TryParse(item, out var subCondition))
                    return false;
                conditions[i++] = subCondition;
            }
            condition = new Any(conditions);
            return true;
        }

        public VersionCondition[] Conditions { get; set; } = conditions;
        public override string ToString() => string.Join(" || ", Conditions.Select(c => c.ToString()));
        public override bool IsFulfilled(Version version) => Conditions.Any(c => c.IsFulfilled(version));
    }
    public class All(VersionCondition[] conditions) : VersionCondition
    {
        public static bool TryParse(string str, [MaybeNullWhen(false)] out All condition)
        {
            condition = null;
            var any = str.Trim().Split("&&", StringSplitOptions.RemoveEmptyEntries);
            if (any.Length is 0 or 1)
            {
                return false;
            }
            var conditions = new VersionCondition[any.Length];
            var i = 0;
            foreach (var item in any)
            {
                if (!VersionCondition.TryParse(item, out var subCondition))
                    return false;
                conditions[i++] = subCondition;
            }
            condition = new All(conditions);
            return true;
        }
        public VersionCondition[] Conditions { get; set; } = conditions;
        public override string ToString() => string.Join(" && ", Conditions.Select(c => c.ToString()));
        public override bool IsFulfilled(Version version) => Conditions.All(c => c.IsFulfilled(version));
    }

    public class Not : VersionCondition
    {
        public override bool IsFulfilled(Version version) => false;
        public override string ToString() => "!";
    }
    public class Yes : VersionCondition
    {
        public override bool IsFulfilled(Version version) => true;
        public override string ToString() => "";
    }

    public class Op(string op, Version ver) : VersionCondition
    {
        public string Operation { get; set; } = op;
        public Version Version { get; set; } = ver;

        public override bool IsFulfilled(Version version)
            => Operation switch
            {
                ">=" => version >= Version,
                "<=" => version <= Version,
                ">" => version > Version,
                "<" => version < Version,
                "^" => version >= Version && version.Major == Version.Major,
                "~" => version >= Version && version.Major == Version.Major &&
                       version.Minor == Version.Minor,
                "!=" => version != Version,
                _ => version == Version, // "==" is the default
            };

        public override string ToString() => $"{Operation} {Version}";
    }

    public static bool TryParse(string str, [MaybeNullWhen(false)] out VersionCondition condition)
    {
        condition = null;
        if (Any.TryParse(str, out var anyCond))
        {
            condition = anyCond;
            return true;
        }
        if (All.TryParse(str, out var allCond))
        {
            condition = allCond;
            return true;
        }

        str = str.Trim();
        if (str == "!")
        {
            condition = new Not();
            return true;
        }

        if (str.Length == 0)
        {
            condition = new Yes();
            return true;
        }

        var opLen = str switch
        {
            { Length: >= 2 } when str.Substring(0, 2) is ">=" or "<=" or "!=" or "==" => 2,
            { Length: >= 1 } when str.Substring(0, 1) is ">" or "<" or "^" or "~" => 1,
            _ => 0
        };
        if (opLen == 0)
            return false;
        var op = str.Substring(0, opLen);
        var ver = str.Substring(opLen).Trim();
        if (Version.TryParse(ver, out var v))
        {
            condition = new Op(op, v);
            return true;
        }
        return false;
    }

    public abstract bool IsFulfilled(Version version);
}
