# Plan — Warhammer Matchmaker

Spécifications : voir [PROJECT.md](PROJECT.md).

## Décisions

- **Résultats** : V0/MVP = déclaration manuelle du score, confirmée par l'adversaire. V2 = objet TTS fourni par nous, importé par les joueurs, qui lit le score LCT et l'envoie au site (l'adversaire confirme toujours).
- **Listes** : les joueurs utilisent ForceOrg ; YellowScribe est le format d'échange (code) entre outils. On importe via le code YellowScribe (faisabilité à valider en itération 0). Repli : coller le texte exporté de la liste.
- **Multi-jeux** : formats, listes et Elo portent un identifiant de jeu (40k, AOS…) dès le départ.

## Stack

- ASP.NET Core + Blazor (rendu serveur) — tout en C#
- SignalR pour le temps réel (adversaire trouvé, acceptation)
- Connexion Steam : `AspNet.Security.OpenId.Steam`
- PostgreSQL + EF Core
- xUnit ; domaine métier (Elo, appariement, cycle de vie du match) dans un projet isolé, sans dépendance web/BDD, développé en TDD pur
- Une seule application, file d'attente en mémoire, déployée en conteneur Docker

## Itérations

| # | Itération | Résultat | Statut |
|---|---|---|---|
| 0 | **Étude** : récupération d'une liste via code YellowScribe ; manière dont LCT expose ses scores (pour V2) | Décision sur l'import des listes | Fait |
| 1 | **Domaine Résultat & Elo** (TDD) : VP → score WTC sur 20 → catégorie (7 niveaux) ; Elo avec résultat = score WTC / 20, facteur K | Moteur de score et Elo testé | Fait |
| 2 | **Domaine Match** (TDD) : file par format + classé/non classé, appariement, acceptation/refus/timeout, déclaration des VP + confirmation, litige/abandon sans effet sur l'Elo | Logique métier complète, testée | Fait |
| 3 | **Squelette web** : solution .NET, connexion Steam, profil joueur, BDD | On peut se connecter | Fait |
| 4 | **Listes d'armée** : import (selon étude), plusieurs listes par joueur, format associé | On gère ses listes | Fait |
| 5 | **File d'attente temps réel** : recherche, notification SignalR, écran d'acceptation, Steam ID + liste adverse | **Premier MVP jouable** | Fait |
| 6 | **Résultats & classement** : déclaration, confirmation, historique, classement Elo | MVP complet | Fait |
| 6b | **Parties longues** : jusqu'à 5 parties en parallèle sans limite de durée, abandon = victoire 20-0 pour l'adversaire, confirmation automatique du résultat après 24 h sans réponse | Règles de jeu réelles | Fait |
| 6c | **Préparation du déploiement** : image Docker, Docker Compose de production (site, PostgreSQL, Caddy HTTPS, sauvegardes), scripts, répétition générale en local | Prêt à déployer | Fait |
| 7 | **V2** : objet TTS de remontée du score, parties planifiées, AOS… | Évolutions | À faire |

## Résultats de l'itération 0

Étude menée le 2026-10-27 sur le code source de [Yellowscribe](https://github.com/ThePants999/Yellowscribe) et de [LCT](https://github.com/Tzabari/lct).

### YellowScribe : faisable, avec une contrainte forte

- **Endpoint** : `GET https://yellowscribe.link/get_army_by_id?id=<code>`. C'est celui qu'utilise le mod TTS lui-même. Il est public, sans authentification, mais **non documenté** (pas de contrat d'API). Vérifié en ligne : un code inconnu renvoie un 404 JSON.
- **Le code expire au bout de 10 minutes** : le serveur supprime les fichiers chaque minute (`cleanFiles`, `TEN_MINUTES`). Il faut donc **récupérer la liste au moment de l'import et en stocker une copie chez nous**. Le site n'interroge plus jamais YellowScribe ensuite, et c'est notre copie qui sert à l'affichage pour l'adversaire.
- **Contenu** : `edition`, `order` et `armyData` (unités → nom, mots-clés de faction, mots-clés, aptitudes, profils de figurines M/T/Sv/W/Ld/OC/InSv, armes). C'est suffisant pour afficher une liste lisible.
- **Pas de points** : le modèle 10e/11e ne les extrait pas. Le **format est donc déclaré par le joueur** à l'import. On ne peut pas vérifier automatiquement la compatibilité liste/format.
- **Risques** : l'API peut changer sans préavis. On l'isole derrière une interface (`IArmyListProvider`), on stocke le JSON brut et on contacte le mainteneur (Discord YellowScribe) avant la mise en production. Repli possible : coller le texte exporté.
- **Charge** : un seul appel par import, ce qui reste négligeable pour leur serveur.

### LCT : la remontée du score en V2 est faisable

- La feuille de score est un objet LCT (GUID `06d627`, exposé via `Global.getVar("scoreSheet_GUID")`).
- Sa fonction `playerSum(pId)` renvoie le total de VP (1 = Rouge, 2 = Bleu). L'état complet (`scores`, `endOfBattle`) est lisible.
- TTS permet à un objet d'appeler une fonction d'un autre objet, de lire `Player.Red.steam_id` / `Player.Blue.steam_id` et de faire un `WebRequest.post`. Notre objet pourra donc envoyer « match X : Steam ID A = 85 VP, Steam ID B = 70 VP » au site.
- **Risque** : ce n'est pas une API officielle de LCT. Le projet lui-même considère que les GUID font office d'API, mais un renommage casserait notre objet. Il faudra prendre contact avec les mainteneurs de LCT en V2. Il existe plusieurs dépôts (`Tzabari/lct`, `Las0mbra/lct`) : le dépôt de référence reste à confirmer.

### Impacts sur le plan

- **Itération 2** : le résultat d'un match contient les **VP de chaque joueur** (le résultat V/N/D s'en déduit), et pas seulement V/N/D. La saisie manuelle du MVP et la remontée TTS de la V2 produiront ainsi la même donnée.
- **Itération 4** : import = saisie du code → récupération immédiate → stockage d'un instantané + nom et format choisis par le joueur.
- **Score** : les VP sont convertis sur 20 selon le barème WTC, puis classés en 7 catégories. L'Elo utilise le score WTC / 20 (voir PROJECT.md, Règles métier).
- **Exemple réel capturé** : [docs/samples/yellowscribe-0c6cfa93.json](docs/samples/yellowscribe-0c6cfa93.json) (Adeptus Custodes, 11e, 9 unités). Structure confirmée : `edition`, `order` (ordre des unités), `armyData[uuid]` → `name`, `factionKeywords`, `keywords`, `abilities{name, desc}`, `modelProfiles{m,t,sv,w,ld,oc,insv}`, `models.models[]{name, number, weapons[], abilities[]}`, `weapons{name, range, a, bsws, s, ap, d, abilities}`. Le champ `baseScript` (Lua, ~56 Ko) ne nous sert pas et ne sera pas stocké.
