# Warhammer Matchmaker

## Vision
Ce projet a pour but de proposer une solution de matchmaking automatisé pour les joueurs de warhammer du monde entier.
Les joueurs joueront sur tabletop-simulator.
Le site devra proposer de s'enregistrer via son compte Steam, puis suivre un système de ELO pour les parties enregistrées.
Un joueur devra pouvoir importer des listes d'armée via un code YellowScribe, puis lancer une recherche en spécifiant le format et si la partie est classée ou non.
Une fois qu'un adversaire est trouvé, les joueurs seront notifiés, accepteront le match, et pourront directement s'inviter via leur identifiant steam et lancer une partie.
À terme, d'autres jeux Warhammer (AOS notamment) seront supportés.
Voici le projet dont tu peux t'inspirer http://www.tts-mission-terminal.com/index.html (lui est surtout centré sur les Etats Unis, on veut une version Fr et européenne)

## Utilisateurs
Des joueurs de warhammer qui souhaitent trouver un adversaire à leur niveau pour jouer en ligne sur TableTopSimulator (TTS).
Tous les joueurs jouent via le mod LCT (11e édition) de TTS et enregistrent leur liste sur le mod ForceOrg.

## Fonctionnalités clés
- pouvoir créer son compte via son identifiant steam
- pouvoir importer plusieurs listes d'armée via un code YellowScribe
- pouvoir lancer une recherche immédiate (file d'attente) en choisissant :
  - le format (ex : Incursion 1000 pts, Strike Force 2000 pts)
  - une de ses listes compatible avec ce format
  - partie classée (impacte l'Elo, compétitive) ou non classée (fun)
- être notifié dans le navigateur quand un adversaire est trouvé
- accepter / refuser le match proposé
- une fois accepté : voir l'identifiant steam de son adversaire et pouvoir parcourir sa liste d'armée
- enregistrer le résultat de la partie (points de victoire des deux joueurs) et mettre à jour l'Elo
- jouer jusqu'à 5 parties en parallèle : une partie peut se jouer en plusieurs fois, sur plusieurs semaines
- garder l'historique de toutes les parties jouées
- rechercher un joueur inscrit et consulter son profil : Elo, listes et historique de parties
- classement Elo public, dont les noms mènent au profil des joueurs

## Hors périmètre (pour l'instant)
- un lien avec discord pour lancer des recherche depuis un chan discord
- gérer les autres langues que le FR
- gérer les autres jeux que warhammer 40k (AOS prévu plus tard)
- gérer d'autres identifiants que Steam
- gérer d'autres plateforme que TableTopSimulator
- parties planifiées (prise de rendez-vous) : plus tard, MVP = recherche immédiate
- notifications push / e-mail
- anti-triche
- remontée automatique du résultat depuis TTS / le mod LCT (V2, voir Questions ouvertes)

## Règles métier
### Elo (inspiré des échecs)
- Elo global unique par joueur (pas par faction), Elo de départ : 1500
- Facteur K : 40 pour les 30 premières parties classées, 20 ensuite, 10 au-delà de 2400
- Le résultat Elo d'un joueur vaut son score WTC / 20 (10-10 → 0.5, 13-7 → 0.65, 20-0 → 1)
- Seules les parties classées impactent l'Elo

### Résultat d'une partie (méthode WTC)
On enregistre les points de victoire (VP) de chaque joueur, puis on les convertit sur 20 selon le barème WTC :

| Écart de VP | Score WTC (gagnant-perdant) |
|---|---|
| 0-5 | 10-10 |
| 6-10 | 11-9 |
| 11-15 | 12-8 |
| 16-20 | 13-7 |
| 21-25 | 14-6 |
| 26-30 | 15-5 |
| 31-35 | 16-4 |
| 36-40 | 17-3 |
| 41-45 | 18-2 |
| 46-50 | 19-1 |
| 51+ | 20-0 |

Catégorie du résultat selon le score WTC du joueur :

| Score WTC | Catégorie |
|---|---|
| 16-20 | Victoire écrasante |
| 13-15 | Victoire |
| 11-12 | Petite victoire |
| 10 | Nul |
| 8-9 | Petite défaite |
| 5-7 | Défaite |
| 0-4 | Grosse défaite |
- Résultat contesté : la partie n'impacte pas l'Elo
- Abandon : victoire 20-0 (score WTC) pour l'adversaire
- Sans réponse de l'adversaire 24 h après la déclaration du résultat, celui-ci est confirmé automatiquement

### Appariement
- Deux joueurs sont appariés s'ils cherchent le même format et le même mode (classé / non classé)
- Écart d'Elo toléré très large au démarrage (à resserrer quand la base de joueurs grandit)
- Les deux joueurs doivent accepter le match dans les 5 minutes ; si l'un refuse ou ne répond pas, l'autre retourne dans la file (en gardant son ancienneté)
- Un joueur peut avoir jusqu'à 5 parties en cours ; il peut chercher un nouvel adversaire tant qu'il n'a pas atteint cette limite
- Une partie en cours n'a pas de limite de durée
- Parmi les adversaires compatibles, on choisit l'Elo le plus proche, puis celui qui attend depuis le plus longtemps

## Données
On s'identifie avec son compte steam.
Listes d'armée importées via code YellowScribe.
Résultats des parties et historique d'Elo.

## Contraintes techniques
Seulement une version web pour le moment.
Stack .NET (préférence du mainteneur), simple et maintenable.
Approche TDD, code bien couvert par des tests.
Doit tenir quelques milliers de joueurs assez rapidement.
Hébergement / nom de domaine fournis plus tard.

## Exemples / scénarios
En tant que joueur de 40k
Je veux m'enregistrer sur le site via mon identifiants Steam
Pour pouvoir créer un compte rapidement

En tant que joueur de 40k déjà identifié
Je veux lancer une recherche classée en Strike Force avec une de mes listes
Pour recevoir une notification quand le système aura trouvé un adversaire

En tant que joueur de 40k qui reçoit une notification d'adversaire trouvé
Je veux accepter le match, voir la liste de mon adversaire et son identifiant steam
Pour lancer la partie TTS et jouer avec lui

En tant que joueur de 40k qui a fini sa partie
Je veux déclarer le résultat et que mon adversaire le confirme
Pour que nos Elo soient mis à jour

## Questions ouvertes
- ~~Remontée automatique du score~~ → V2 : objet TTS fourni par nous qui lit la feuille de score LCT (faisable, cf. PLAN.md itération 0)
- ~~Listes~~ → les joueurs utilisent ForceOrg, on importe via le code YellowScribe (expire en 10 min : on stocke une copie à l'import, cf. PLAN.md)
