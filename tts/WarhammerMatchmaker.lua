-- Objet « Warhammer Matchmaker » pour Tabletop Simulator (mod LCT).
-- Lit la feuille de score LCT et envoie le score à https://warhammer-matchmaker.com :
--   * en direct, à chaque changement, pour suivre la partie sur le site (rien n'est déclaré) ;
--   * sur clic « Envoyer le score final » : déclaration que l'adversaire confirme sur le site.
-- Lua 5.2 (MoonSharp). Les scripts TTS ne tournent que chez l'hôte de la partie.

SITE = "https://warhammer-matchmaker.com"
VERSION = 1
LIVE_INTERVAL_SECONDS = 30
SCORE_SHEET_GUID = "06d627"

code = ""
lastLiveScore = nil
liveStopped = false
status = "Collez le code de la partie (page de la partie sur le site)."

-- Interface ------------------------------------------------------------------------------------

-- L'objet est un bloc aplati (échelle 8 x 5) : on compense pour que les boutons ne soient pas déformés
-- (0,25 x 8 = 0,4 x 5 = 2 : boutons agrandis 2 fois).
local UI_SCALE = { 0.25, 1, 0.4 }

function onLoad(savedState)
    if savedState and savedState ~= "" then
        code = savedState
    end
    self.createButton({ click_function = "noop", function_owner = self, label = "Warhammer Matchmaker",
        position = { 0, 0.6, -0.36 }, scale = UI_SCALE, width = 0, height = 0, font_size = 220 })
    self.createInput({ input_function = "onCodeChanged", function_owner = self, label = "Code de la partie",
        value = code, alignment = 3, validation = 1, tooltip = "Code de 8 caractères affiché sur la page de la partie",
        position = { 0, 0.6, -0.12 }, scale = UI_SCALE, width = 1800, height = 260, font_size = 180 })
    self.createButton({ click_function = "sendFinalScore", function_owner = self, label = "Envoyer le score final",
        position = { 0, 0.6, 0.13 }, scale = UI_SCALE, width = 1800, height = 260, font_size = 150,
        color = { 0.85, 0.65, 0.2 } })
    self.createButton({ click_function = "noop", function_owner = self, label = status,
        position = { 0, 0.6, 0.36 }, scale = UI_SCALE, width = 0, height = 0, font_size = 100 })
    Wait.time(shareLiveScore, LIVE_INTERVAL_SECONDS, -1)
end

function onSave()
    return code
end

function noop() end

-- Boutons dans l'ordre de création : titre (0), envoi (1), ligne d'état (2).
local STATUS_BUTTON = 2

function setStatus(text)
    status = text
    self.editButton({ index = STATUS_BUTTON, label = text })
end

function onCodeChanged(_, _, value, stillEditing)
    if stillEditing then return end
    code = trim(value or "")
    lastLiveScore = nil
    liveStopped = false
    if code ~= "" then
        setStatus("Code enregistré. Le score sera suivi en direct sur le site.")
    end
end

-- Lecture du score -----------------------------------------------------------------------------

function trim(text)
    return (text:gsub("^%s+", ""):gsub("%s+$", ""))
end

function hostSteamId()
    for _, player in ipairs(Player.getPlayers()) do
        if player.host then return player.steam_id end
    end
    return nil
end

-- Renvoie le score à envoyer, ou nil et la raison.
function readReport()
    if code == "" then
        return nil, "Collez d'abord le code de la partie."
    end
    if not (Player.Red.seated and Player.Blue.seated) then
        return nil, "Les deux joueurs doivent être assis en Rouge et en Bleu."
    end
    local guid = (Global.getVar("scoreSheet_GUID")) or SCORE_SHEET_GUID
    local sheet = getObjectFromGUID(guid)
    if not sheet then
        return nil, "Feuille de score LCT introuvable : cet objet ne fonctionne qu'avec le mod LCT."
    end
    local summary = sheet.call("getMatchSummary")
    if type(summary) ~= "table" or type(summary.red) ~= "table" or type(summary.blue) ~= "table" then
        return nil, "Impossible de lire la feuille de score LCT (version du mod non prise en charge)."
    end
    return {
        version = VERSION,
        code = code,
        hostSteamId = hostSteamId(),
        red = { steamId = Player.Red.steam_id, vp = summary.red.total or 0 },
        blue = { steamId = Player.Blue.steam_id, vp = summary.blue.total or 0 },
        roundsPlayed = summary.roundsPlayed or 0,
    }
end

function scoreText(report)
    return "Rouge " .. report.red.vp .. " - Bleu " .. report.blue.vp .. " (round " .. report.roundsPlayed .. ")"
end

-- Envoi ----------------------------------------------------------------------------------------

function post(path, report, onAnswer)
    WebRequest.custom(SITE .. path, "POST", true, JSON.encode(report),
        { ["Content-Type"] = "application/json", Accept = "application/json" },
        function(request)
            local message = nil
            if request.text and request.text ~= "" then
                local ok, answer = pcall(JSON.decode, request.text)
                if ok and type(answer) == "table" then message = answer.message end
            end
            local responseCode = request.response_code or 0
            onAnswer(responseCode >= 200 and responseCode < 300, responseCode, message or request.error or "Site injoignable.")
        end)
end

function sendFinalScore(_, playerColor)
    local report, problem = readReport()
    if not report then
        setStatus(problem)
        broadcastToColor(problem, playerColor, { 1, 0.4, 0.4 })
        return
    end
    setStatus("Envoi du score final : " .. scoreText(report) .. "...")
    post("/api/tts/results", report, function(success, _, message)
        setStatus(message)
        broadcastToAll("Warhammer Matchmaker : " .. message, success and { 0.5, 1, 0.5 } or { 1, 0.4, 0.4 })
        if success then liveStopped = true end
    end)
end

-- Appelé toutes les 30 s : n'envoie que si le score a changé depuis le dernier envoi réussi.
function shareLiveScore()
    if liveStopped then return end
    local report = readReport()
    if not report then return end
    local key = report.red.vp .. ":" .. report.blue.vp .. ":" .. report.roundsPlayed
    if key == lastLiveScore then return end
    post("/api/tts/live", report, function(success, responseCode, message)
        if success then
            lastLiveScore = key
            setStatus("En direct sur le site : " .. scoreText(report))
        elseif responseCode == 409 or responseCode == 403 or responseCode == 404 then
            -- Résultat déjà déclaré, ou mauvais code : inutile d'insister jusqu'au prochain code.
            liveStopped = true
            setStatus(message)
        end
    end)
end
