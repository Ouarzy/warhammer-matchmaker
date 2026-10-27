// <push-settings data-key="…"> : active ou désactive les notifications push sur cet appareil (page Mon compte).
// Élément personnalisé : il s'initialise tout seul, y compris après une navigation de Blazor.

const toBytes = (base64Url) => {
    const base64 = (base64Url + "=".repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
    return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
};

const send = (method, url, body) => fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
});

class PushSettings extends HTMLElement {
    connectedCallback() {
        this.render();
    }

    async registration() {
        return navigator.serviceWorker.register("/sw.js");
    }

    async render() {
        if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) {
            this.show(`<p>Ce navigateur ne permet pas les notifications. Sur iPhone ou iPad, ajoutez d'abord le site à
                l'écran d'accueil (Partager → « Sur l'écran d'accueil »), puis ouvrez-le depuis cette icône.</p>`);
            return;
        }
        if (Notification.permission === "denied") {
            this.show(`<p>Les notifications sont bloquées pour ce site : autorisez-les dans les réglages du navigateur.</p>`);
            return;
        }
        const subscription = await (await this.registration()).pushManager.getSubscription();
        if (subscription) {
            this.show(`<p><strong>Activées sur cet appareil.</strong></p>
                <div class="actions">
                    <button class="button secondary" data-action="test">Envoyer une notification de test</button>
                    <button class="button secondary" data-action="disable">Désactiver</button>
                </div>`);
        } else {
            this.show(`<p>Soyez prévenu d'une proposition de partie ou d'un résultat à confirmer, même site fermé.</p>
                <div class="actions"><button class="button" data-action="enable">Activer sur cet appareil</button></div>`);
        }
    }

    show(html) {
        this.innerHTML = html;
        this.querySelectorAll("button[data-action]").forEach((button) =>
            button.addEventListener("click", () => this.run(button.dataset.action, button)));
    }

    async run(action, button) {
        button.disabled = true;
        try {
            const registration = await this.registration();
            if (action === "enable") {
                if (await Notification.requestPermission() !== "granted") return;
                const subscription = await registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: toBytes(this.dataset.key),
                });
                const response = await send("POST", "/api/push/subscriptions", subscription.toJSON());
                if (!response.ok) throw new Error(`Enregistrement refusé (${response.status}).`);
            } else if (action === "disable") {
                const subscription = await registration.pushManager.getSubscription();
                if (subscription) {
                    await send("DELETE", "/api/push/subscriptions", { endpoint: subscription.endpoint });
                    await subscription.unsubscribe();
                }
            } else if (action === "test") {
                await send("POST", "/api/push/test");
            }
        } catch (error) {
            this.insertAdjacentHTML("beforeend", `<p class="error">Impossible : ${error.message}</p>`);
            return;
        } finally {
            button.disabled = false;
        }
        await this.render();
    }
}

if (!customElements.get("push-settings")) customElements.define("push-settings", PushSettings);
