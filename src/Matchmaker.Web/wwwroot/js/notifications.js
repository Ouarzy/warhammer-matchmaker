// Notifications « adversaire trouvé » : notification système, son et titre d'onglet.

const originalTitle = document.title;

export async function requestPermission() {
    if (!("Notification" in window) || Notification.permission !== "default") return;
    try {
        await Notification.requestPermission();
    } catch {
        // Navigateur sans permission de notification : le son et le titre suffiront.
    }
}

export function notifyMatchFound(opponent) {
    playChime();
    flashTitle();
    if ("Notification" in window && Notification.permission === "granted" && document.hidden) {
        const notification = new Notification("Adversaire trouvé !", {
            body: `${opponent} est prêt à jouer. Acceptez dans les 5 minutes.`,
            tag: "match-found",
        });
        notification.onclick = () => {
            window.focus();
            notification.close();
        };
    }
}

function playChime() {
    try {
        const audio = new AudioContext();
        [660, 880].forEach((frequency, index) => {
            const oscillator = audio.createOscillator();
            const gain = audio.createGain();
            const start = audio.currentTime + index * 0.18;
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0.15, start);
            gain.gain.exponentialRampToValueAtTime(0.001, start + 0.3);
            oscillator.connect(gain).connect(audio.destination);
            oscillator.start(start);
            oscillator.stop(start + 0.3);
        });
    } catch {
        // Audio bloqué par le navigateur : pas de son.
    }
}

function flashTitle() {
    document.title = "⚔ Adversaire trouvé !";
    const restore = () => {
        if (document.hidden) return;
        document.title = originalTitle;
        document.removeEventListener("visibilitychange", restore);
    };
    document.addEventListener("visibilitychange", restore);
    if (!document.hidden) setTimeout(restore, 5000);
}
