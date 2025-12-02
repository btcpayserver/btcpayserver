#nullable  enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

public class TextTemplate(string template)
{
    static readonly Regex _interpolationRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public Func<string, string> NotFoundReplacement { get; set; } = path => $"[NotFound({path})]";
    public Func<string, string> ParsingErrorReplacement { get; set; } = path => $"[ParsingError({path})]";

    public Func<(string Path, string Value), string> Encode { get; set; } = v => v.Value;

    public string Render(JObject model)
    {
        model = (JObject)ToLowerCase(model);
        return _interpolationRegex.Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            var initial = path;
            if (!path.StartsWith("$."))
                path = $"$.{path}";
            path = path.ToLowerInvariant();
            try
            {
                var token = model.SelectToken(path);
                return Encode((initial, token?.ToString() ?? NotFoundReplacement(initial)));
            }
            catch
            {
                return Encode((initial, ParsingErrorReplacement(initial)));
            }
        });
    }

    public List<string> GetPaths(JObject model)
    {
        var paths = new List<List<string>>();
        GetAvailablePaths(model, paths, null);

        List<string> result = new List<string>();
        foreach (var path in paths)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('{');
            int i = 0;
            foreach (var p in path)
            {
                if (i != 0 && !p.StartsWith("["))
                    builder.Append('.');

                builder.Append(p);
                i++;
            }
            builder.Append('}');
            result.Add(builder.ToString());
        }
        return result;
    }

    private void GetAvailablePaths(JToken tok, List<List<string>> paths, List<string>? currentPath)
    {
        if (tok is JProperty prop)
        {
            if (currentPath is null)
            {
                currentPath = new List<string>();
            }
            currentPath.Add(prop.Name);
            GetAvailablePaths(prop.Value, paths, currentPath);
        }
        else if (tok is JValue)
        {
            if (currentPath is not null)
                paths.Add(currentPath);
        }
        if (tok is JObject obj)
        {
            foreach (var p in obj.Properties())
            {
                var newPath = currentPath is null ? new List<string>() : new List<string>(currentPath);
                GetAvailablePaths(p, paths, newPath);
            }
        }
        if (tok is JArray arr)
        {
            int i = 0;
            foreach (var tokChild in arr)
            {
                var newPath = currentPath is null ? new List<string>() : new List<string>(currentPath);
                newPath.Add($"[{i++}]");
                GetAvailablePaths(tokChild, paths, newPath);
            }
        }
    }

    private JToken ToLowerCase(JToken model)
    {
        if (model is JProperty obj)
            return new JProperty(obj.Name.ToLowerInvariant(), ToLowerCase(obj.Value));
        if (model is JArray arr)
        {
            var copy = new JArray();
            foreach (var item in arr)
            {
                copy.Add(ToLowerCase(item));
            }
            return copy;
        }
        if (model is JObject)
        {
            var copy = new JObject();
            foreach (var prop in model.Children<JProperty>())
            {
                var newProp = (JProperty)ToLowerCase(prop);
                if (copy.Property(newProp.Name) is { } existing)
                {
                    if (existing.Value is JObject exJobj)
                        exJobj.Merge(newProp.Value);
                }
                else
                    copy.Add(newProp);
            }
            return copy;
        }
        return model.DeepClone();
    }
}
