-- Tests du script de l'objet, hors de TTS : TTS et LCT sont remplacés par des faux.
-- Lancer depuis la racine du dépôt : lua tts/tests/WarhammerMatchmaker_test.lua

local SCRIPT = "tts/WarhammerMatchmaker.lua"
local failures, passed = 0, 0

-- Faux TTS ---------------------------------------------------------------------------------------

local world

local function fakePlayer(steamId, seated, host)
    return { steam_id = steamId, seated = seated, host = host }
end

local function newWorld()
    world = {
        buttons = {}, inputs = {}, requests = {}, broadcasts = {}, timers = {},
        summary = { red = { total = 45 }, blue = { total = 30 }, roundsPlayed = 3 },
        sheetPresent = true,
    }
    self = {
        createButton = function(button) table.insert(world.buttons, button) end,
        createInput = function(input) table.insert(world.inputs, input) end,
        editButton = function(edit) world.buttons[edit.index + 1].label = edit.label end,
    }
    Player = {
        Red = fakePlayer("111", true, true),
        Blue = fakePlayer("222", true, false),
    }
    Player.getPlayers = function() return { Player.Red, Player.Blue } end
    Global = { getVar = function(name) if name == "scoreSheet_GUID" then return "06d627" end end }
    getObjectFromGUID = function(guid)
        if guid ~= "06d627" or not world.sheetPresent then return nil end
        return { call = function(name) if name == "getMatchSummary" then return world.summary end end }
    end
    -- On garde la table envoyée plutôt que du vrai JSON : les tests vérifient son contenu.
    JSON = {
        encode = function(value) world.lastEncoded = value; return "<json>" end,
        decode = function(text) return world.decoded[text] end,
    }
    world.decoded = {}
    WebRequest = {
        custom = function(url, method, download, body, headers, callback)
            table.insert(world.requests, { url = url, method = method, report = world.lastEncoded, headers = headers, callback = callback })
        end,
    }
    Wait = { time = function(fn, seconds, repetitions) table.insert(world.timers, { fn = fn, seconds = seconds, repetitions = repetitions }) end }
    broadcastToAll = function(text) table.insert(world.broadcasts, text) end
    broadcastToColor = function(text, color) table.insert(world.broadcasts, color .. ": " .. text) end
    dofile(SCRIPT)
end

local function answer(request, responseCode, message)
    world.decoded["answer"] = { message = message }
    request.callback({ response_code = responseCode, text = "answer", is_error = responseCode >= 400 })
end

local function statusLabel() return world.buttons[3].label end
local function typeCode(value) onCodeChanged(self, "Red", value, false) end

-- Petit cadre de test ------------------------------------------------------------------------------

local function eq(expected, actual, what)
    if expected ~= actual then
        error((what or "valeur") .. " : attendu « " .. tostring(expected) .. " », obtenu « " .. tostring(actual) .. " »", 2)
    end
end

local function test(name, body)
    newWorld()
    local ok, err = pcall(body)
    if ok then passed = passed + 1 else failures = failures + 1; print("ÉCHEC  " .. name .. "\n       " .. tostring(err)) end
end

-- Tests -------------------------------------------------------------------------------------------

test("le bouton envoie le score final de la feuille LCT au nom de l'hôte", function()
    onLoad("")
    typeCode("  k7q2m9xa ")
    sendFinalScore(self, "Red")
    local request = world.requests[1]
    eq("https://warhammer-matchmaker.com/api/tts/results", request.url, "url")
    eq("POST", request.method)
    eq("application/json", request.headers["Content-Type"])
    eq(1, request.report.version, "version")
    eq("k7q2m9xa", request.report.code, "code (le site ignore la casse)")
    eq("111", request.report.hostSteamId, "hôte")
    eq("111", request.report.red.steamId); eq(45, request.report.red.vp)
    eq("222", request.report.blue.steamId); eq(30, request.report.blue.vp)
    eq(3, request.report.roundsPlayed)
end)

test("l'objet a un titre, un champ pour le code, un bouton d'envoi et une ligne d'état", function()
    onLoad("")
    eq("Warhammer Matchmaker", world.buttons[1].label)
    eq("onCodeChanged", world.inputs[1].input_function)
    eq("sendFinalScore", world.buttons[2].click_function)
    eq("Collez le code de la partie (page de la partie sur le site).", statusLabel())
end)

test("la réponse du site est affichée à tous", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    sendFinalScore(self, "Red")
    answer(world.requests[1], 200, "Score 45 - 30 envoyé, en attente de confirmation par l'adversaire.")
    eq("Score 45 - 30 envoyé, en attente de confirmation par l'adversaire.", statusLabel(), "état")
    eq("Warhammer Matchmaker : Score 45 - 30 envoyé, en attente de confirmation par l'adversaire.", world.broadcasts[1])
end)

test("sans code, rien n'est envoyé", function()
    onLoad("")
    sendFinalScore(self, "Blue")
    eq(0, #world.requests, "requêtes")
    eq("Blue: Collez d'abord le code de la partie.", world.broadcasts[1])
end)

test("les deux joueurs doivent être assis en Rouge et en Bleu", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    Player.Blue.seated = false
    sendFinalScore(self, "Red")
    eq(0, #world.requests, "requêtes")
    eq("Les deux joueurs doivent être assis en Rouge et en Bleu.", statusLabel())
end)

test("sans feuille de score LCT, l'objet l'explique", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    world.sheetPresent = false
    sendFinalScore(self, "Red")
    eq(0, #world.requests, "requêtes")
    eq("Feuille de score LCT introuvable : cet objet ne fonctionne qu'avec le mod LCT.", statusLabel())
end)

test("le code est gardé avec la sauvegarde de la table", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    local saved = onSave()
    newWorld()
    onLoad(saved)
    eq("K7Q2M9XA", world.inputs[1].value, "champ")
    sendFinalScore(self, "Red")
    eq("K7Q2M9XA", world.requests[1].report.code)
end)

test("le score en cours est envoyé en direct toutes les 30 secondes", function()
    onLoad("")
    eq(30, world.timers[1].seconds, "intervalle"); eq(-1, world.timers[1].repetitions, "répétitions")
    typeCode("K7Q2M9XA")
    world.timers[1].fn()
    eq("https://warhammer-matchmaker.com/api/tts/live", world.requests[1].url)
    answer(world.requests[1], 200, "ok")
    eq("En direct sur le site : Rouge 45 - Bleu 30 (round 3)", statusLabel())
end)

test("le score en direct n'est renvoyé que s'il a changé", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    world.timers[1].fn(); answer(world.requests[1], 200, "ok")
    world.timers[1].fn()
    eq(1, #world.requests, "requêtes sans changement")
    world.summary.red.total = 50
    world.timers[1].fn()
    eq(2, #world.requests, "requêtes après changement")
    eq(50, world.requests[2].report.red.vp)
end)

test("un envoi en direct raté est retenté au tour suivant", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    world.timers[1].fn(); answer(world.requests[1], 429, "Trop d'envois : réessayez dans une minute.")
    world.timers[1].fn()
    eq(2, #world.requests, "requêtes")
end)

test("le direct s'arrête une fois le résultat déclaré", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    world.timers[1].fn(); answer(world.requests[1], 409, "Un résultat a déjà été déclaré pour cette partie.")
    world.summary.red.total = 60
    world.timers[1].fn()
    eq(1, #world.requests, "requêtes")
    eq("Un résultat a déjà été déclaré pour cette partie.", statusLabel())
end)

test("le direct reprend avec un nouveau code", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    sendFinalScore(self, "Red"); answer(world.requests[1], 200, "envoyé")
    world.timers[1].fn()
    eq(1, #world.requests, "plus de direct après le score final")
    typeCode("ABCDEFGH")
    world.timers[1].fn()
    eq(2, #world.requests, "direct de la nouvelle partie")
end)

test("le site injoignable est signalé", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    sendFinalScore(self, "Red")
    world.requests[1].callback({ response_code = 0, text = "", is_error = true, error = "Cannot resolve destination host" })
    eq("Cannot resolve destination host", statusLabel())
end)

print(passed .. " tests réussis, " .. failures .. " en échec")
os.exit(failures == 0 and 0 or 1)
