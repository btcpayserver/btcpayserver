using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class Language
    {
        public Language(string code, string displayName)
        {
            DisplayName = displayName;
            Code = code;
        }

        public string Code { get; set; }
        public string DisplayName { get; set; }
    }

    public class LanguageService
    {
        private readonly IEnumerable<Language> _languages;

        public LanguageService()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "locales");
            var files = Directory.GetFiles(path, "*.json");
            var result = new List<Language>();
            foreach (var file in files)
            {
                using (var stream = new StreamReader(file))
                {
                    var json = stream.ReadToEnd();

                    var name = JObject.Parse(json).GetValue("currentLanguage").ToString();
                    var code  =  Path.GetFileNameWithoutExtension(file);
                    result.Add(new Language(code, name));
                }
            }

            _languages = result;
        }

        public Language[] GetLanguages()
        {
            return _languages.ToArray();
//            return new[]
//            {
//                new Language("en-US", "English"),
//                new Language("de-DE", "Deutsch"),
//                new Language("ja-JP", "日本語"),
//                new Language("fr-FR", "Français"),
//                new Language("es-ES", "Spanish"),
//                new Language("pt-PT", "Portuguese"),
//                new Language("pt-BR", "Portuguese (Brazil)"),
//                new Language("nl-NL", "Dutch"),
//                new Language("np-NP", "नेपाली"),
//                new Language("cs-CZ", "Česky"),
//                new Language("is-IS", "Íslenska"),
//                new Language("hr-HR", "Croatian"),
//                new Language("it-IT", "Italiano"),
//                new Language("kk-KZ", "Қазақша"),
//                new Language("ru-RU", "русский"),
//                new Language("uk-UA", "Українська"),
//                new Language("vi-VN", "Tiếng Việt"),
//                new Language("zh-SP", "中文（简体）"),
//            };
        }
    }
}
