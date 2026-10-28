# Déploiement

Le site tourne sur un seul serveur (VPS) avec Docker Compose :

| Service | Rôle |
|---|---|
| `caddy` | Reçoit le trafic sur les ports 80/443, obtient et renouvelle seul le certificat HTTPS (Let's Encrypt), transmet au site. |
| `web` | Le site : image `ghcr.io/ouarzy/warhammer-matchmaker`, construite et publiée par GitHub Actions à chaque push sur `main` (après les tests). Applique les migrations de base au démarrage. |
| `db` | PostgreSQL 17, données sur le volume `pgdata`. |
| `backup` | Une sauvegarde de la base par jour dans `deploy/backups/`, gardée 14 jours. |

Une seule instance du site : la file d'attente est gardée en mémoire. Les clés qui signent les cookies de connexion sont sur le volume `keys`, pour que les joueurs restent connectés après une mise à jour.

## Prérequis

- Un VPS **Ubuntu 24.04 LTS**, au moins 1 Go de mémoire (ex. Gandi Cloud VPS Starter V-R1), avec votre clé SSH. Le serveur ne compile rien : il télécharge l'image. Pour quelques milliers de joueurs simultanés, passer à 2 Go ou plus.
- Le nom de domaine, avec deux enregistrements DNS vers l'adresse IP du serveur :
  - `A` `@` → IP du serveur
  - `A` `www` → IP du serveur
  (et `AAAA` si le serveur a une IPv6)
- Une clé d'API Web Steam : https://steamcommunity.com/dev/apikey (domaine : `warhammer-matchmaker.com`).

## Première installation

```sh
# Gandi : se connecter avec l'utilisateur « ubuntu » (root est refusé), qui a les droits sudo.

# 1. Sur le serveur : Docker, swap de 1 Go, pare-feu, code, deploy/.env
ssh ubuntu@<ip> 'sudo bash -s' < deploy/bootstrap.sh

# 2. Compléter les secrets sur le serveur (ACME_EMAIL, STEAM_API_KEY, et VAPID_* pour les notifications push :
#    commande de génération dans deploy/.env.example ; ne jamais changer les clés VAPID, sinon les abonnements existants cessent de fonctionner)
ssh -t ubuntu@<ip> 'nano /opt/matchmaker/deploy/.env'

# 3. Démarrer
ssh ubuntu@<ip> 'cd /opt/matchmaker && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d'
```

Le certificat HTTPS est obtenu au premier accès, une fois le DNS propagé. Vérification : `https://<domaine>/health` doit répondre `Healthy`.

## Mettre en ligne une nouvelle version

1. Pousser les commits sur `main` : GitHub Actions lance les tests puis publie l'image (onglet *Actions* du dépôt).
2. Une fois la CI au vert, depuis un poste de développement :

```sh
deploy/deploy.sh ubuntu@<ip>
```

Le script met à jour les fichiers de déploiement, télécharge la nouvelle image, redémarre le site et vérifie `/health`. Les migrations de base s'appliquent au démarrage.

Pour revenir à une version précédente : `IMAGE_TAG=<hash du commit> deploy/deploy.sh ubuntu@<ip>` (chaque image est aussi publiée sous le hash de son commit).

## Exploitation

```sh
cd /opt/matchmaker
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps          # état des services
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f web # journaux du site
```

### Restaurer une sauvegarde

```sh
cd /opt/matchmaker
docker compose -f deploy/docker-compose.yml --env-file deploy/.env stop web
docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec -T db \
  pg_restore -U matchmaker -d matchmaker --clean --if-exists < deploy/backups/matchmaker-AAAA-MM-JJ.dump
docker compose -f deploy/docker-compose.yml --env-file deploy/.env start web
```

### Copie des sauvegardes sur un Mac

Le serveur ne garde que 14 jours de sauvegardes, et les perdrait avec lui. `deploy/fetch-backups.sh` les copie dans `~/warhammer-matchmaker/backup` (seuls les nouveaux fichiers ; rien n'est supprimé côté Mac). Pour le lancer chaque jour à 13 h (et au réveil si le Mac dormait) :

```bash
deploy/install-mac-backups.sh        # à relancer après toute modification de fetch-backups.sh
```

- Journal : `~/Library/Logs/warhammer-matchmaker-backups.log`.
- Le dossier est hors de `~/Documents` : macOS y interdit l'écriture aux tâches de fond.
- La clé SSH est protégée par une phrase de passe : la tâche reprend l'agent SSH ouvert par le terminal (plugin `ssh-agent` d'oh-my-zsh). Après un redémarrage du Mac, il faut donc avoir ouvert un terminal une fois pour qu'elle réussisse.

## Surveillance

Le workflow GitHub Actions `Disponibilité` (`.github/workflows/uptime.yml`) interroge `/health` toutes les 15 minutes (trois essais) ; en cas d'échec, GitHub envoie un e-mail. Les tâches planifiées de GitHub peuvent être retardées de quelques minutes, et sont mises en pause après 60 jours sans activité sur le dépôt (les réactiver depuis l'onglet Actions).

## Répétition en local

Toute la pile peut tourner sur un poste de développement, avec `localhost` (certificat local de Caddy), d'autres ports et une image construite localement (`deploy/docker-compose.local.yml`) :

```sh
cat > /tmp/rehearsal.env <<'EOF'
DOMAIN=localhost
ACME_EMAIL=test@example.com
POSTGRES_PASSWORD=repetition123
STEAM_API_KEY=cle-factice
HTTP_PORT=8088
HTTPS_PORT=8443
EOF
docker compose -p matchmaker-rehearsal -f deploy/docker-compose.yml -f deploy/docker-compose.local.yml --env-file /tmp/rehearsal.env up -d --build
curl -k https://localhost:8443/health
docker compose -p matchmaker-rehearsal -f deploy/docker-compose.yml -f deploy/docker-compose.local.yml --env-file /tmp/rehearsal.env down -v
```
