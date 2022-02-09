"use strict"

const isDev = document.documentElement.hasAttribute("data-devenv");

document.addEventListener("DOMContentLoaded", () => {
    // SignalR
    ;(window.LNbankHubs || []).forEach(hub => {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl(`/plugins/lnbank/hubs/${hub.id}`)
            .withAutomaticReconnect()
            .build()

        Object.entries(hub.handlers).forEach(([message, handler]) => {
            if (isDev) connection.on(message, console.debug)
            connection.on(message, handler)
        })

        connection.start()
            .catch(console.error)
    })
})
