using System;
using System.Collections.Generic;
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
        LanguagePackUpdateService.LanguageManifestEntry[] manifestLanguages;
        var degradedMode = false;
        try
        {
            manifestLanguages = await languagePackUpdateService.GetManifestLanguages();
        }
        catch (Exception)
        {
            manifestLanguages = [];
            degradedMode = true;
        }
        var manifestByName = new Dictionary<string, LanguagePackUpdateService.LanguageManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifestLanguages)
            manifestByName.TryAdd(entry.Name, entry);
        var installedNames = new HashSet<string>(translations.Select(t => t.TranslationName), StringComparer.OrdinalIgnoreCase);
        var vm = new ListTranslationsViewModel
        {
            ManifestFetchFailed = degradedMode
        };

        foreach (var translation in translations)
        {
            var isSelected = policiesSettings.LangTranslation == translation.TranslationName ||
                             (policiesSettings.LangTranslation is null && translation.Source == "Default");
            var isDownloadedPack = translation.Source == "LanguagePack";
            var updateAvailable = false;
            manifestByName.TryGetValue(translation.TranslationName, out var manifestEntry);

            if (!degradedMode && isDownloadedPack)
                updateAvailable = await languagePackUpdateService.CheckForLanguagePackUpdateCached(translation.TranslationName, translation.Metadata);

            var translationVm = new ListTranslationsViewModel.TranslationViewModel
            {
                Installed = true,
                Editable = translation.Source == "Custom",
                Source = translation.Source,
                TranslationName = translation.TranslationName,
                NativeName = manifestEntry?.Native ?? translation.TranslationName,
                MaintainerHandle = manifestEntry?.MaintainerHandle,
                MaintainerUrl = manifestEntry?.MaintainerUrl,
                LastUpdated = manifestEntry?.Updated,
                Fallback = translation.Fallback,
                IsSelected = isSelected,
                IsDeletable = translation.Source == "LanguagePack" || translation.Source == "Custom",
                UpdateAvailable = updateAvailable
            };
            if (isSelected)
                vm.InstalledLanguages.Insert(0, translationVm);
            else
                vm.InstalledLanguages.Add(translationVm);
        }

        if (!degradedMode)
        {
            foreach (var manifestEntry in manifestLanguages.Where(m => !installedNames.Contains(m.Name)).OrderBy(m => m.Name))
            {
                vm.AvailableToInstall.Add(new ListTranslationsViewModel.TranslationViewModel
                {
                    TranslationName = manifestEntry.Name,
                    NativeName = manifestEntry.Native ?? manifestEntry.Name,
                    MaintainerHandle = manifestEntry.MaintainerHandle,
                    MaintainerUrl = manifestEntry.MaintainerUrl,
                    LastUpdated = manifestEntry.Updated
                });
            }
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
        var d = await localizer.GetTranslation(translation);
        if (d is null || d.Source == "LanguagePack")
            return NotFound();

        var translations = await localizer.GetTranslations(translation);
        return View(new EditTranslationViewModel().SetTranslations(translations.Translations));
    }

    [HttpPost("server/translations/{translation}")]
    public async Task<IActionResult> EditTranslation(string translation, EditTranslationViewModel viewModel)
    {
        var d = await localizer.GetTranslation(translation);
        if (d is null || d.Source == "LanguagePack")
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

    [HttpPost("server/translations/download")]
    public async Task<IActionResult> DownloadLanguagePack(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Please select a language"].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        Translations translations;
        string version;
        try
        {
            var result = await languagePackUpdateService.FetchLanguagePackFromRepository(language);
            version = result.Item2;
            if (!Translations.TryCreateFromJson(result.Item1, out translations))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Downloaded language pack is invalid"].Value;
                return RedirectToAction(nameof(ListTranslations));
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Failed to download language pack: {0}", ex.Message].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        var existingTranslation = await localizer.GetTranslation(language);
        if (existingTranslation is null)
        {
            try
            {
                existingTranslation = await localizer.CreateTranslation(language, Translations.DefaultLanguage, "LanguagePack");
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' downloaded successfully", language].Value;
            }
            catch (DbException)
            {
                existingTranslation = await localizer.GetTranslation(language);
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' updated successfully", language].Value;
            }
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Language pack '{0}' updated successfully", language].Value;
        }

        if (existingTranslation is null)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Failed to download language pack: {0}", language].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        await localizer.Save(existingTranslation, translations);
        await localizer.UpdateVersion(language, version);
        languagePackUpdateService.InvalidateCache(language);
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpPost("server/translations/{translation}/uninstall")]
    public async Task<IActionResult> UninstallLanguagePack(string translation)
    {
        var existing = await localizer.GetTranslation(translation);
        if (existing is null)
            return NotFound();
        if (existing.Source != "LanguagePack" && existing.Source != "Custom")
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Translation {0} is not user-installed and cannot be uninstalled", translation].Value;
            return RedirectToAction(nameof(ListTranslations));
        }
        if (policiesSettings.LangTranslation == translation)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Translation {0} is the currently selected one and cannot be uninstalled", translation].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        var fallbackUsers = (await localizer.GetTranslations())
            .Where(t => t.Fallback == translation)
            .Select(t => t.TranslationName)
            .OrderBy(t => t)
            .ToArray();
        if (fallbackUsers.Length > 0)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Translation {0} cannot be uninstalled because it is used as fallback by: {1}", translation, string.Join(", ", fallbackUsers)].Value;
            return RedirectToAction(nameof(ListTranslations));
        }

        try
        {
            await localizer.DeleteTranslation(translation);
        }
        catch (DbException)
        {
            fallbackUsers = (await localizer.GetTranslations())
                .Where(t => t.Fallback == translation)
                .Select(t => t.TranslationName)
                .OrderBy(t => t)
                .ToArray();
            if (fallbackUsers.Length > 0)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Translation {0} cannot be uninstalled because it is used as fallback by: {1}", translation, string.Join(", ", fallbackUsers)].Value;
                return RedirectToAction(nameof(ListTranslations));
            }
            throw;
        }

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Translation {0} deleted", translation].Value;
        return RedirectToAction(nameof(ListTranslations));
    }

    [HttpGet("server/dictionaries")]
    public IActionResult RedirectToTranslation()
    {
        return RedirectToActionPermanent(nameof(ListTranslations));
    }

    [Route("server/dictionaries/{**catchall}")]
    public IActionResult RedirectDictionariesSubpath(string catchall)
    {
        var newPath = "/server/translations" + (string.IsNullOrEmpty(catchall) ? "" : $"/{catchall}");
        return RedirectPermanentPreserveMethod(newPath + Request.QueryString);
    }
}
