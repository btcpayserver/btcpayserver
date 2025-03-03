Interop = {
    eventListeners: {},
    getWidth(el) {
        return el.clientWidth;
    },
    openModal(selector) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        const modal = bootstrap.Modal.getOrCreateInstance($el);
        modal.show();
    },
    closeModal(selector) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        const modal = bootstrap.Modal.getOrCreateInstance($el);
        modal.hide();
    },
    showOffcanvas(selector) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        if (window.getComputedStyle($el).visibility === 'hidden') {
            const offcanvas = bootstrap.Offcanvas.getOrCreateInstance($el);
            offcanvas.show();
        }
    },
    hideOffcanvas(selector) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        const offcanvas = bootstrap.Offcanvas.getOrCreateInstance($el);
        offcanvas.hide();
    },
    addEventListener(dotnetHelper, selector, eventName, methodName) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        const id = `${selector}_${eventName}_${methodName}`;
        Interop.eventListeners[id] = async event => {
            console.debug('Event listener invoked:', id);
            await dotnetHelper.invokeMethodAsync(methodName);
        }
        $el.addEventListener(eventName, Interop.eventListeners[id]);
        console.debug('Event listener added:', id);
    },
    removeEventListener(selector, eventName, methodName) {
        const $el = document.querySelector(selector);
        if (!$el) return console.warn('Selector does not exist:', selector);
        const id = `${selector}_${eventName}_${methodName}`;
        if (!Interop.eventListeners[id]) return console.warn('Event listener does not exist:', id);
        $el.removeEventListener(eventName, Interop.eventListeners[id]);
        delete Interop.eventListeners[id];
        console.debug('Event listener removed:', id);
    },
    removeEventListeners(...args) {
        for (arg of args) Interop.removeEventListener(...arg)
    }
}
