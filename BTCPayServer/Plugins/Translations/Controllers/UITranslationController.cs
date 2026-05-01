using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Translations.Views;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations.Controllers;

[Authorize(Policy = Client.Policies.CanModifyServerSettings,
    AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(TranslationsPlugin.Area)]
public class UITranslationController(
    PoliciesSettings policiesSettings,
    IStringLocalizer stringLocalizer,
    LocalizerService localizer,
    LanguagePackUpdateService languagePackUpdateService,
    SettingsRepository settingsRepository,
    BTCPayServerEnvironment environment) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet("server/translations")]
    public async Task<IActionResult> ListTranslations()
    {
        var translations = await localizer.GetTranslations();
        var vm = new ListTranslationsViewModel
        {
            AvailableLanguages = await languagePackUpdateService.GetAvailableLanguages()
        };

        foreach (var translation in translations)
        {
            var isSelected = policiesSettings.LangTranslation == translation.TranslationName ||
                             (policiesSettings.LangTranslation is null && translation.Source == "Default");
            var isDownloadedPack = translation.Source == "Custom";
            var updateAvailable = false;

            if (isDownloadedPack)
                updateAvailable = await languagePackUpdateService.CheckForLanguagePackUpdateCached(translation.TranslationName, translation.Metadata);

            var translationVm = new ListTranslationsViewModel.TranslationViewModel
            {
                Editable = translation.Source == "Custom",
                Source = translation.Source,
                TranslationName = translation.TranslationName,
                Fallback = translation.Fallback,
                IsSelected = isSelected,
                IsDownloadedLanguagePack = isDownloadedPack,
                UpdateAvailable = updateAvailable
            };
            if (isSelected)
                vm.Translations.Insert(0, translationVm);
            else
                vm.Translations.Add(translationVm);
        }

        return View(vm);
    }

    [HttpGet("server/translations/create")]
    public async Task<IActionResult> CreateTranslation(string fallback = null)
    {
        var translations = await localizer.GetTranslations();
        return View(new CreateTranslationViewModel
        {
            Name = fallback is not null ? $"Clone of {fallback}" : "",
            Fallback = fallback ?? Translations.DefaultLanguage,
        }.SetTranslations(translations));
    }

    [HttpPost("server/translations/create")]
    public async Task<IActionResult> CreateTranslation(CreateTranslationViewModel viewModel)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await localizer.CreateTranslation(viewModel.Name, viewModel.Fallback, "Custom");
            }
            catch (DbException)
            {
                ModelState.AddModelError(nameof(viewModel.Name), StringLocalizer["'{0}' already exists", viewModel.Name]);
            }
        }

        if (!ModelState.IsValid)
            return View(viewModel.SetTranslations(await localizer.GetTranslations()));
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Translation created"].Value;
        return RedirectToAction(nameof(EditTranslation), new { translation = viewModel.Name });
    }

    [HttpGet("server/translations/{translation}")]
    public async Task<IActionResult> EditTranslation(string translation)
    {
        if ((await localizer.GetTranslation(translation)) is null)
            return NotFound();
        var translations = await localizer.GetTranslations(translation);
        return View(new EditTranslationViewModel().SetTranslations(translations.Translations));
    }

    [HttpPost("server/translations/{translation}")]
    public async Task<IActionResult> EditTranslation(string translation, EditTranslationViewModel viewModel)
    {
        var d = await localizer.GetTranslation(translation);
        if (d is null)
            return NotFound();
        if (environment.CheatMode && viewModel.Command == "Fake")
        {
            var t = await localizer.GetTranslations(translation);
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

        await localizer.Save(d, translations);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Translation updated"].Value;
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpPost("server/translations/{translation}/select")]
    public async Task<IActionResult> SelectTranslation(string translation)
    {
        if ((await localizer.GetTranslation(translation)) is null)
            return NotFound();
        var settings = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new();
        settings.LangTranslation = translation;
        await settingsRepository.UpdateSetting(settings);
        await localizer.Load();
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Default translation changed to {0}", translation].Value;
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpPost("server/translations/{translation}/delete")]
    public async Task<IActionResult> DeleteTranslation(string translation)
    {
        await localizer.DeleteTranslation(translation);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Translation {0} deleted", translation].Value;
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpPost("server/translations/download")]
    public async Task<IActionResult> DownloadLanguagePack(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Please select a language"].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        string translationsJson;
        string version;
        try
        {
            (translationsJson, version) = await languagePackUpdateService.FetchLanguagePackFromRepository(language);
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Failed to download language pack: {0}", ex.Message].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        var translations = Translations.CreateFromJson(translationsJson);
        var existingTranslation = await localizer.GetTranslation(language);
        if (existingTranslation is null)
        {
            existingTranslation = await localizer.CreateTranslation(language, Translations.DefaultLanguage, "Custom");
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' downloaded successfully", language].Value;
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' updated successfully", language].Value;
        }

        await localizer.Save(existingTranslation, translations);
        await localizer.UpdateVersion(language, version);
        languagePackUpdateService.InvalidateCache(language);
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpGet("server/dictionaries")]
    public IActionResult RedirectToTranslation()
    {
        // Redirect to the new translation endpoint for backward compatibility.
        return RedirectPermanent(nameof(ListTranslations));
    }
}
