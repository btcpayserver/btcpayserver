#nullable  enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace BTCPayServer.Abstractions.Contracts;

public abstract class VersionCondition
{
    public class Multiple(VersionCondition[] conditions) : VersionCondition
    {
        public VersionCondition[] Conditions { get; set; } = conditions;
        public override string ToString() => string.Join(" || ", Conditions.Select(c => c.ToString()));
        public override bool IsFulfilled(Version version) => Conditions.Any(c => c.IsFulfilled(version));
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
        {
            switch (Operation)
            {
                case { } xx when xx.StartsWith(">="):
                    return version >= Version;
                case { } xx when xx.StartsWith("<="):
                    return version <= Version;
                case { } xx when xx.StartsWith(">"):
                    return version > Version;
                case { } xx when xx.StartsWith("<"):
                    return version < Version;
                case { } xx when xx.StartsWith("^"):
                    return version >= Version && version.Major == Version.Major;
                case { } xx when xx.StartsWith("~"):
                    return version >= Version && version.Major == Version.Major &&
                           version.Minor == Version.Minor;
                case { } xx when xx.StartsWith("!="):
                    return version != Version;
                case { } xx when xx.StartsWith("=="):
                default:
                    return version == Version;
            }
        }

        public override string ToString() => $"{Operation} {Version}";
    }

    public static bool TryParse(string str, [MaybeNullWhen(false)] out VersionCondition condition)
    {
        condition = null;
        var any = str.Trim().Split("||", StringSplitOptions.RemoveEmptyEntries);
        if (any.Length != 1)
        {
            var conditions = new VersionCondition[any.Length];
            var i = 0;
            foreach (var item in any)
            {
                if (!TryParse(item, out var subCondition))
                    return false;
                conditions[i++] = subCondition;
            }
            condition = new Multiple(conditions);
            return true;
        }

        any[0] = any[0].Trim();
        if (any[0] == "!")
        {
            condition = new Not();
            return true;
        }

        if (any[0].Length == 0)
        {
            condition = new Yes();
            return true;
        }

        var opLen = any[0] switch
        {
            { Length: >= 2 } when any[0].Substring(2) is ">=" or "<=" or "!=" or "==" => 2,
            { Length: >= 1 } when any[0].Substring(1) is ">" or "<" or "^" or "~" => 1,
            _ => 0
        };
        if (opLen == 0)
            return false;
        var op = any[0].Substring(0, opLen);
        var ver = any[0].Substring(opLen).Trim();
        if (Version.TryParse(ver, out var v))
        {
            condition = new Op(op, v);
            return true;
        }
        return false;
    }

    public abstract bool IsFulfilled(Version version);
}
