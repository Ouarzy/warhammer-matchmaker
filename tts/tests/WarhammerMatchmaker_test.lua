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
        getScale = function() return { x = 13, y = 0.2, z = 7 } end,
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

-- La ligne d'état est coupée en plusieurs lignes : on la relit d'un seul tenant.
local function statusLabel() return (world.buttons[2].label:gsub("\n", " ")) end
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

local function tick() world.timers[1].fn() end

test("le score en cours de la feuille LCT est envoyé en direct au nom de l'hôte", function()
    onLoad("")
    typeCode("  k7q2m9xa ")
    tick()
    local request = world.requests[1]
    eq("https://warhammer-matchmaker.com/api/tts/live", request.url, "url")
    eq("POST", request.method)
    eq("application/json", request.headers["Content-Type"])
    eq(1, request.report.version, "version")
    eq("k7q2m9xa", request.report.code, "code (le site ignore la casse)")
    eq("111", request.report.hostSteamId, "hôte")
    eq("111", request.report.red.steamId); eq(45, request.report.red.vp)
    eq("222", request.report.blue.steamId); eq(30, request.report.blue.vp)
    eq(3, request.report.roundsPlayed)
end)

test("l'objet a un titre, un champ pour le code et une ligne d'état, sans bouton d'envoi du score final", function()
    onLoad("")
    eq(2, #world.buttons, "boutons")
    eq("Warhammer Matchmaker", world.buttons[1].label)
    eq("onCodeChanged", world.inputs[1].input_function)
    eq(nil, sendFinalScore, "plus d'envoi du score final : il se valide sur le site")
    eq("Collez le code de la partie (page de la partie sur le site).", statusLabel())
end)

test("les boutons gardent leur taille sur un bloc aplati, quelle que soit son échelle", function()
    onLoad("")
    local title = world.buttons[1]
    eq(2, title.scale[1] * 13, "largeur du titre"); eq(2, title.scale[3] * 7, "hauteur du titre")
    eq(-2.2, title.position[3] * 7, "titre en haut")
    eq(2, world.inputs[1].scale[1] * 13, "largeur du champ")
end)

test("le titre et la ligne d'état sont écrits en blanc sur le bloc sombre", function()
    onLoad("")
    eq(1, world.buttons[1].font_color[1]); eq(1, world.buttons[2].font_color[3])
end)

test("une ligne d'état trop longue est coupée entre les mots", function()
    local text = "Les joueurs assis à la table ne sont pas ceux de cette partie."
    local wrapped = wrap(text)
    for line in wrapped:gmatch("[^\n]+") do
        assert(#line <= STATUS_LINE_LENGTH, "ligne trop longue : " .. line)
    end
    eq(text, (wrapped:gsub("\n", " ")), "texte complet")
    eq("Code enregistré.", wrap("Code enregistré."), "texte court inchangé")
end)

test("sans code, rien n'est envoyé et l'objet invite toujours à le coller", function()
    onLoad("")
    tick()
    eq(0, #world.requests, "requêtes")
    eq("Collez le code de la partie (page de la partie sur le site).", statusLabel())
end)

test("les deux joueurs doivent être assis en Rouge et en Bleu", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    Player.Blue.seated = false
    tick()
    eq(0, #world.requests, "requêtes")
    eq("Les deux joueurs doivent être assis en Rouge et en Bleu.", statusLabel())
end)

test("sans feuille de score LCT, l'objet l'explique", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    world.sheetPresent = false
    tick()
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
    tick()
    eq("K7Q2M9XA", world.requests[1].report.code)
end)

test("le score en cours est envoyé en direct toutes les 30 secondes", function()
    onLoad("")
    eq(30, world.timers[1].seconds, "intervalle"); eq(-1, world.timers[1].repetitions, "répétitions")
    typeCode("K7Q2M9XA")
    tick()
    answer(world.requests[1], 200, "ok")
    eq("En direct sur le site : Rouge 45 - Bleu 30 (round 3)", statusLabel())
end)

test("le score en direct n'est renvoyé que s'il a changé", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    tick(); answer(world.requests[1], 200, "ok")
    tick()
    eq(1, #world.requests, "requêtes sans changement")
    world.summary.red.total = 50
    tick()
    eq(2, #world.requests, "requêtes après changement")
    eq(50, world.requests[2].report.red.vp)
end)

test("un envoi en direct raté est retenté au tour suivant", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    tick(); answer(world.requests[1], 429, "Trop d'envois : réessayez dans une minute.")
    tick()
    eq(2, #world.requests, "requêtes")
end)

test("une fois un score final proposé sur le site, le même score n'est pas renvoyé", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    tick(); answer(world.requests[1], 409, "Un résultat a déjà été déclaré pour cette partie.")
    tick()
    eq(1, #world.requests, "requêtes")
    eq("Un résultat a déjà été déclaré pour cette partie.", statusLabel())
end)

test("si la proposition est annulée sur le site, le direct reprend au prochain changement de score", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    tick(); answer(world.requests[1], 409, "Un résultat a déjà été déclaré pour cette partie.")
    world.summary.red.total = 60
    tick()
    eq(2, #world.requests, "requêtes")
    answer(world.requests[2], 200, "ok")
    eq("En direct sur le site : Rouge 60 - Bleu 30 (round 3)", statusLabel())
end)

test("un mauvais code arrête le direct jusqu'au prochain code", function()
    onLoad("")
    typeCode("K7Q2M9XA")
    tick(); answer(world.requests[1], 404, "Code de partie inconnu : recopiez-le depuis la page de la partie sur le site.")
    world.summary.red.total = 60
    tick()
    eq(1, #world.requests, "plus de direct avec ce code")
    typeCode("ABCDEFGH")
    tick()
    eq(2, #world.requests, "direct avec le nouveau code")
end)

print(passed .. " tests réussis, " .. failures .. " en échec")
os.exit(failures == 0 and 0 or 1)
