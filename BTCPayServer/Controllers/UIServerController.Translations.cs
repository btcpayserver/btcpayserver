using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/dictionaries")]
        public async Task<IActionResult> ListDictionaries()
        {
            var dictionaries = await this._localizer.GetDictionaries();
            var vm = new ListDictionariesViewModel();
            foreach (var dictionary in dictionaries)
            {
                vm.Dictionaries.Add(new()
                {
                    Editable = dictionary.Source == "Custom",
                    Source = dictionary.Source,
                    DictionaryName = dictionary.DictionaryName,
                    Fallback = dictionary.Fallback,
                    IsSelected = 
                    _policiesSettings.LangDictionary == dictionary.DictionaryName ||
                    (_policiesSettings.LangDictionary is null && dictionary.Source == "Default")
                });
            }
            return View(vm);
        }

        [HttpGet("server/dictionaries/create")]
        public async Task<IActionResult> CreateDictionary(string fallback = null)
        {
            var dictionaries = await this._localizer.GetDictionaries();
            return View(new CreateDictionaryViewModel()
            {
                Name = fallback is not null ? $"{fallback} (Copy)" : "",
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
                    await this._localizer.CreateDictionary(viewModel.Name, viewModel.Fallback, "Custom");
                }
                catch (DbException)
                {
                    ModelState.AddModelError(nameof(viewModel.Name), $"'{viewModel.Name}' already exists");
                }
            }
            if (!ModelState.IsValid)
                return View(viewModel.SetDictionaries(await this._localizer.GetDictionaries()));
            TempData[WellKnownTempData.SuccessMessage] = "Dictionary created";
            return RedirectToAction(nameof(EditDictionary), new { dictionary = viewModel.Name });
        }

        [HttpGet("server/dictionaries/{dictionary}")]
        public async Task<IActionResult> EditDictionary(string dictionary)
        {
            if ((await this._localizer.GetDictionary(dictionary)) is null)
                return NotFound();
            var translations = await _localizer.GetTranslations(dictionary);
            return View(new EditDictionaryViewModel().SetTranslations(translations.Translations));
        }

        [HttpPost("server/dictionaries/{dictionary}")]
        public async Task<IActionResult> EditDictionary(string dictionary, EditDictionaryViewModel viewModel)
        {
            var d = await this._localizer.GetDictionary(dictionary);
            if (d is null)
                return NotFound();
            if (!Translations.TryCreateFromText(viewModel.Translations, out var translations))
            {
                ModelState.AddModelError(nameof(viewModel.Translations), "Syntax error");
                return View(viewModel);
            }
            await _localizer.Save(d, translations);
            TempData[WellKnownTempData.SuccessMessage] = "Dictionary updated";
            return RedirectToAction(nameof(ListDictionaries));
        }
        [HttpGet("server/dictionaries/{dictionary}/select")]
        public async Task<IActionResult> SelectDictionary(string dictionary)
        {
            var settings = await this._SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new();
            settings.LangDictionary = dictionary;
            await _SettingsRepository.UpdateSetting(settings);
            await _localizer.Load();
            TempData[WellKnownTempData.SuccessMessage] = $"Default dictionary changed to {dictionary}";
            return RedirectToAction(nameof(ListDictionaries));
        }
        [HttpPost("server/dictionaries/{dictionary}/delete")]
        public async Task<IActionResult> DeleteDictionary(string dictionary)
        {
            await _localizer.DeleteDictionary(dictionary);
            TempData[WellKnownTempData.SuccessMessage] = $"Dictionary {dictionary} deleted";
            return RedirectToAction(nameof(ListDictionaries));
        }
    }
}
