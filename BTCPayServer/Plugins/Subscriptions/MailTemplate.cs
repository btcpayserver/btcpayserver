#nullable  enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins;

public class MailTemplate(string template)
{

    static readonly Regex _interpolationRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);
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
                return token?.ToString() ?? $"<NotFound({initial})>";
            }
            catch
            {
                return $"<ParsingError({initial})>";
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
        else if (tok is JValue val)
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
                copy.Add(ToLowerCase(prop));
            }
            return copy;
        }
        return model.DeepClone();
    }
}
