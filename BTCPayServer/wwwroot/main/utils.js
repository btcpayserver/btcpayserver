function delegate(eventType, selector, handler, root) {
    (root || document).addEventListener(eventType, function(event) {
        const target = event.target.closest(selector)
        if (target) {
            event.target = target
            if (handler.call(this, event) === false) {
                event.preventDefault()
            }
        }
    })
}
