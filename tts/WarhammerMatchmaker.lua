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

-- Les boutons d'un objet sont étirés avec lui. Le bloc est aplati (13 x 7 dans le fichier téléchargé) :
-- on lit son échelle réelle pour garder des boutons de taille fixe (UI_SIZE), sans déformation, même
-- si le joueur le redimensionne. Les positions sont données en unités de la table, depuis le centre.
UI_SIZE = 2
WHITE = { 1, 1, 1 }
STATUS_LINE_LENGTH = 45

function layout(worldZ)
    local scale = self.getScale()
    return {
        position = { 0, 0.6, worldZ / scale.z },
        scale = { UI_SIZE / scale.x, 1, UI_SIZE / scale.z },
    }
end

function withLayout(worldZ, element)
    local placed = layout(worldZ)
    element.position = placed.position
    element.scale = placed.scale
    element.function_owner = self
    return element
end

function onLoad(savedState)
    if savedState and savedState ~= "" then
        code = savedState
    end
    self.createButton(withLayout(-2.2, { click_function = "noop", label = "Warhammer Matchmaker",
        width = 0, height = 0, font_size = 220, font_color = WHITE }))
    self.createInput(withLayout(-0.8, { input_function = "onCodeChanged", label = "Code de la partie",
        value = code, alignment = 3, validation = 1, tooltip = "Code de 8 caractères affiché sur la page de la partie",
        width = 1800, height = 260, font_size = 180 }))
    self.createButton(withLayout(0.6, { click_function = "sendFinalScore", label = "Envoyer le score final",
        width = 1800, height = 260, font_size = 150, color = { 0.85, 0.65, 0.2 } }))
    self.createButton(withLayout(2.0, { click_function = "noop", label = wrap(status),
        width = 0, height = 0, font_size = 100, font_color = WHITE }))
    Wait.time(shareLiveScore, LIVE_INTERVAL_SECONDS, -1)
end

-- Coupe un texte en lignes d'au plus STATUS_LINE_LENGTH caractères, entre les mots, pour rester dans le cadre.
function wrap(text)
    local lines, line = {}, ""
    for word in text:gmatch("%S+") do
        if line ~= "" and #line + 1 + #word > STATUS_LINE_LENGTH then
            table.insert(lines, line)
            line = word
        else
            line = (line == "") and word or (line .. " " .. word)
        end
    end
    if line ~= "" then table.insert(lines, line) end
    return table.concat(lines, "\n")
end

function onSave()
    return code
end

function noop() end

-- Boutons dans l'ordre de création : titre (0), envoi (1), ligne d'état (2).
local STATUS_BUTTON = 2

function setStatus(text)
    status = text
    self.editButton({ index = STATUS_BUTTON, label = wrap(text) })
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
