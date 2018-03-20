const locales = {
    en: {
        message: {
            hello: 'Hello!! - EN',
        },
        "Awaiting Payment...": "Awaiting Payment...",
        await_pay: "Awaiting Payment..."
    },

    de: {
        message: {
            hello: 'Hallo!! - DE',
        },
        "Awaiting Payment...": "Warten auf Zahlung...",
        await_pay: "Warten auf Zahlung..."
    },
};

i18next.init({
    lng: 'en',
    fallbackLng: 'en',
    resources: {
        en: { translation: locales.en },
        de: { translation: locales.de }
    },
});

const i18n = new VueI18next(i18next);

function changeLanguage(lang) {
    i18next.changeLanguage(lang);
}
