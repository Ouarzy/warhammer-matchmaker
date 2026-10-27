// Service worker : affiche les notifications push du site, même onglet fermé.

self.addEventListener("push", (event) => {
    let message;
    try {
        message = event.data.json();
    } catch {
        return;
    }
    // Le serveur ne connaît pas le fuseau du joueur : l'heure de la partie est mise en heure locale ici.
    const at = message.at
        ? new Intl.DateTimeFormat("fr-FR", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" }).format(new Date(message.at))
        : "";
    event.waitUntil(self.registration.showNotification(message.title, {
        body: (message.body || "").replace("{at}", at),
        tag: message.tag,
        renotify: true,
        data: { url: message.url || "/" },
    }));
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const url = new URL(event.notification.data.url, self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        const open = windows.find((client) => client.url.startsWith(self.location.origin));
        if (open) {
            await open.focus();
            return open.navigate(url);
        }
        return self.clients.openWindow(url);
    })());
});
