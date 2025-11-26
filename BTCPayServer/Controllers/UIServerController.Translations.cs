using System;
using System.Data.Common;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/dictionaries")]
        public async Task<IActionResult> ListDictionaries()
        {
            var dictionaries = await _localizer.GetDictionaries();
            var vm = new ListDictionariesViewModel();
            foreach (var dictionary in dictionaries)
            {
                var isSelected = _policiesSettings.LangDictionary == dictionary.DictionaryName ||
                                  (_policiesSettings.LangDictionary is null && dictionary.Source == "Default");
                var dict = new ListDictionariesViewModel.DictionaryViewModel
                {
                    Editable = dictionary.Source == "Custom",
                    Source = dictionary.Source,
                    DictionaryName = dictionary.DictionaryName,
                    Fallback = dictionary.Fallback,
                    IsSelected = isSelected
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
            try
            {
                translationsJson = await FetchLanguagePackFromRepository(language);
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
            return RedirectToAction(nameof(ListDictionaries));
        }

        private async Task<string> FetchLanguagePackFromRepository(string language)
        {
            var fileName = language.ToLowerInvariant();
            var url = $"https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/{fileName}.json";
            
            var httpClient = HttpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadAsStringAsync();
        }
    }
}
