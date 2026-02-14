using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        private static readonly ConcurrentDictionary<string, (bool UpdateAvailable, DateTime CheckedAt)> _updateCheckCache = new();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        [HttpGet("server/dictionaries")]
        public async Task<IActionResult> ListDictionaries()
        {
            var dictionaries = await _localizer.GetDictionaries();
            var vm = new ListDictionariesViewModel();
            var downloadableLanguages = GetDownloadableLanguages();
            
            foreach (var dictionary in dictionaries)
            {
                var isSelected = _policiesSettings.LangDictionary == dictionary.DictionaryName ||
                                  (_policiesSettings.LangDictionary is null && dictionary.Source == "Default");
                var isDownloadedPack = downloadableLanguages.Contains(dictionary.DictionaryName);
                var updateAvailable = false;
                
                if (isDownloadedPack && dictionary.Source == "Custom")
                {
                    updateAvailable = await CheckForLanguagePackUpdateCached(dictionary.DictionaryName, dictionary.Metadata);
                }
                
                var dict = new ListDictionariesViewModel.DictionaryViewModel
                {
                    Editable = dictionary.Source == "Custom",
                    Source = dictionary.Source,
                    DictionaryName = dictionary.DictionaryName,
                    Fallback = dictionary.Fallback,
                    IsSelected = isSelected,
                    IsDownloadedLanguagePack = isDownloadedPack && dictionary.Source == "Custom",
                    UpdateAvailable = updateAvailable
                };
                if (isSelected)
                    vm.Dictionaries.Insert(0, dict);
                else
                    vm.Dictionaries.Add(dict);
            }
            return View(vm);
        }

        [HttpGet("server/dictionaries/create")]
        public async Task<IActionResult> CreateDictionary(string fallback = null)
        {
            var dictionaries = await _localizer.GetDictionaries();
            return View(new CreateDictionaryViewModel
            {
                Name = fallback is not null ? $"Clone of {fallback}" : "",
                Fallback = fallback ?? Translations.DefaultLanguage,
            }.SetDictionaries(dictionaries));
        }

        [HttpPost("server/dictionaries/create")]
        public async Task<IActionResult> CreateDictionary(CreateDictionaryViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _localizer.CreateDictionary(viewModel.Name, viewModel.Fallback, "Custom");
                }
                catch (DbException)
                {
                    ModelState.AddModelError(nameof(viewModel.Name), StringLocalizer["'{0}' already exists", viewModel.Name]);
                }
            }
            if (!ModelState.IsValid)
                return View(viewModel.SetDictionaries(await _localizer.GetDictionaries()));
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Dictionary created"].Value;
            return RedirectToAction(nameof(EditDictionary), new { dictionary = viewModel.Name });
        }

        [HttpGet("server/dictionaries/{dictionary}")]
        public async Task<IActionResult> EditDictionary(string dictionary)
        {
            if ((await _localizer.GetDictionary(dictionary)) is null)
                return NotFound();
            var translations = await _localizer.GetTranslations(dictionary);
            return View(new EditDictionaryViewModel().SetTranslations(translations.Translations));
        }

        [HttpPost("server/dictionaries/{dictionary}")]
        public async Task<IActionResult> EditDictionary(string dictionary, EditDictionaryViewModel viewModel)
        {
            var d = await _localizer.GetDictionary(dictionary);
            if (d is null)
                return NotFound();
            if (Environment.CheatMode && viewModel.Command == "Fake")
            {
                var t = await _localizer.GetTranslations(dictionary);
                var jobj = JObject.Parse(t.Translations.ToJsonFormat());
                foreach (var prop in jobj.Properties())
                {
                    prop.Value = "OK";
                    if (prop.Name.Contains("{0}")) prop.Value += " {0}";
                    if (prop.Name.Contains("{1}")) prop.Value += " {1}";
                    if (prop.Name.Contains("{2}")) prop.Value += " {2}";
                }
                viewModel.Translations = Translations.CreateFromJson(jobj.ToString()).ToJsonFormat();
            }
            if (!Translations.TryCreateFromJson(viewModel.Translations, out var translations))
            {
                ModelState.AddModelError(nameof(viewModel.Translations), StringLocalizer["Syntax error"]);
                return View(viewModel);
            }
            await _localizer.Save(d, translations);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Dictionary updated"].Value;
            return RedirectToAction(nameof(ListDictionaries));
        }

        [HttpGet("server/dictionaries/{dictionary}/select")]
        public async Task<IActionResult> SelectDictionary(string dictionary)
        {
            var settings = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new();
            settings.LangDictionary = dictionary;
            await _SettingsRepository.UpdateSetting(settings);
            await _localizer.Load();
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Default dictionary changed to {0}", dictionary].Value;
            return RedirectToAction(nameof(ListDictionaries));
        }

        [HttpPost("server/dictionaries/{dictionary}/delete")]
        public async Task<IActionResult> DeleteDictionary(string dictionary)
        {
            await _localizer.DeleteDictionary(dictionary);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Dictionary {0} deleted", dictionary].Value;
            return RedirectToAction(nameof(ListDictionaries));
        }

        [HttpPost("server/dictionaries/download")]
        public async Task<IActionResult> DownloadLanguagePack(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Please select a language"].Value;
                return RedirectToAction(nameof(ListDictionaries));
            }

            string translationsJson;
            string version;
            try
            {
                (translationsJson, version) = await FetchLanguagePackFromRepository(language);
            }
            catch (HttpRequestException ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Failed to download language pack: {0}", ex.Message].Value;
                return RedirectToAction(nameof(ListDictionaries));
            }

            var translations = Translations.CreateFromJson(translationsJson);
            var existingDictionary = await _localizer.GetDictionary(language);
            if (existingDictionary is null)
            {
                existingDictionary = await _localizer.CreateDictionary(language, Translations.DefaultLanguage, "Custom");
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' downloaded successfully", language].Value;
            }
            else
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' updated successfully", language].Value;
            }
            
            await _localizer.Save(existingDictionary, translations);
            await UpdateLanguagePackMetadata(language, version);
            return RedirectToAction(nameof(ListDictionaries));
        }

        [HttpPost("server/dictionaries/{dictionary}/update")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLanguagePack(string dictionary)
        {
            var existingDictionary = await _localizer.GetDictionary(dictionary);
            if (existingDictionary is null || !GetDownloadableLanguages().Contains(dictionary))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Dictionary not found or not a downloadable language pack"].Value;
                return RedirectToAction(nameof(ListDictionaries));
            }

            string translationsJson;
            string version;
            try
            {
                (translationsJson, version) = await FetchLanguagePackFromRepository(dictionary);
            }
            catch (HttpRequestException ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Failed to update language pack: {0}", ex.Message].Value;
                return RedirectToAction(nameof(ListDictionaries));
            }

            var translations = Translations.CreateFromJson(translationsJson);
            await _localizer.Save(existingDictionary, translations);
            await UpdateLanguagePackMetadata(dictionary, version);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' updated successfully", dictionary].Value;
            return RedirectToAction(nameof(ListDictionaries));
        }

        private async Task<(string translationsJson, string version)> FetchLanguagePackFromRepository(string language)
        {
            if (!GetDownloadableLanguages().Contains(language))
            {
                throw new ArgumentException($"Language '{language}' is not a valid downloadable language pack.", nameof(language));
            }
            
            var fileName = Uri.EscapeDataString(language.ToLowerInvariant());
            var url = $"https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/{fileName}.json";
            
            var httpClient = HttpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var translationsJson = await httpClient.GetStringAsync(url);
            
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(translationsJson));
            var version = Convert.ToHexString(hash);
            
            return (translationsJson, version);
        }

        private async Task<bool> CheckForLanguagePackUpdateCached(string language, JObject metadata)
        {
            var cacheKey = language;
            
            if (_updateCheckCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.CheckedAt < _cacheExpiration)
                {
                    return cached.UpdateAvailable;
                }
            }
            
            var updateAvailable = await CheckForLanguagePackUpdate(language, metadata);
            _updateCheckCache[cacheKey] = (updateAvailable, DateTime.UtcNow);
            
            return updateAvailable;
        }
        
        private async Task<bool> CheckForLanguagePackUpdate(string language, JObject metadata)
        {
            try
            {
                if (!GetDownloadableLanguages().Contains(language))
                {
                    return false;
                }
                
                var fileName = Uri.EscapeDataString(language.ToLowerInvariant());
                var url = $"https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/{fileName}.json";
                
                var httpClient = HttpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var remoteContent = await httpClient.GetStringAsync(url);
                
                var remoteHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(remoteContent));
                var remoteVersion = Convert.ToHexString(remoteHash);
                var localVersion = metadata["version"]?.ToString();
                
                if (string.IsNullOrEmpty(localVersion))
                    return true;
                
                return remoteVersion != localVersion;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        private async Task UpdateLanguagePackMetadata(string language, string version)
        {
            var metadata = new JObject { ["version"] = version };
            await _localizer.UpdateDictionaryMetadata(language, metadata);
        }

        private static string[] GetDownloadableLanguages()
        {
            return new[]
            {
                "Dutch",
                "French",
                "German",
                "Hindi",
                "Indonesian",
                "Italian",
                "Japanese",
                "Norwegian",
                "Korean",
                "Portuguese (Brazil)",
                "Russian",
                "Serbian",
                "Spanish",
                "Thai",
                "Turkish"
            };
        }
    }
}
