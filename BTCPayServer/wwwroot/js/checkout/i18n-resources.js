const locales = {
    en: {
        nested: {
            lang: 'Language',
        },
        "Awaiting Payment...": "Awaiting Payment...",
        "Pay with": "Pay with",
        "Scan": "Scan",
        "Copy": "Copy",
        "Conversion": "Conversion",
        "Open in wallet": "Open in wallet"
    },

    de: {
        nested: {
            lang: 'Sprache',
        },
        "Awaiting Payment...": "Warten auf Zahlung...",
        "Pay with": "Bezahlen mit",
        "Scan": "Scan",
        "Copy": "Kopieren",
        "Conversion": "Umwandlung",
        "Open in wallet": "In der Brieftasche öffnen"
    },
};

i18next.init({
    lng: 'en',
    fallbackLng: 'en',
    nsSeparator: false,
    keySeparator: false,
    resources: {
        en: { translation: locales.en },
        de: { translation: locales.de }
    },
});

const i18n = new VueI18next(i18next);

function changeLanguage(lang) {
    i18next.changeLanguage(lang);
}
