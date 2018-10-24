using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        public Language[] GetLanguages()
        {
            return new[]
            {
                new Language("en-US", "English"),
                new Language("de-DE", "Deutsch"),
                new Language("ja-JP", "日本語"),
                new Language("fr-FR", "Français"),
                new Language("es-ES", "Spanish"),
                new Language("pt-PT", "Portuguese"),
                new Language("pt-BR", "Portuguese (Brazil)"),
                new Language("nl-NL", "Dutch"),
                new Language("np-NP", "नेपाली"),
                new Language("cs-CZ", "Česky"),
                new Language("is-IS", "Íslenska"),
                new Language("hr-HR", "Croatian"),
                new Language("it-IT", "Italiano"),
                new Language("kk-KZ", "Қазақша"),
                new Language("ru-RU", "русский"),
                new Language("uk-UA", "Українська"),
                new Language("vi-VN", "Tiếng Việt"),
                new Language("zh-SP", "中文（简体）"),
            };
        }
    }
}
