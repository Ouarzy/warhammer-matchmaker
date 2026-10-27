# Déploiement

Le site tourne sur un seul serveur (VPS) avec Docker Compose :

| Service | Rôle |
|---|---|
| `caddy` | Reçoit le trafic sur les ports 80/443, obtient et renouvelle seul le certificat HTTPS (Let's Encrypt), transmet au site. |
| `web` | Le site (image construite depuis le `Dockerfile` du dépôt). Applique les migrations de base au démarrage. |
| `db` | PostgreSQL 17, données sur le volume `pgdata`. |
| `backup` | Une sauvegarde de la base par jour dans `deploy/backups/`, gardée 14 jours. |

Une seule instance du site : la file d'attente est gardée en mémoire. Les clés qui signent les cookies de connexion sont sur le volume `keys`, pour que les joueurs restent connectés après une mise à jour.

## Prérequis

- Un VPS **Ubuntu 24.04 LTS**, 2 vCPU / 4 Go de mémoire (ex. Hetzner CX22, OVH VPS), avec votre clé SSH.
- Le nom de domaine, avec deux enregistrements DNS vers l'adresse IP du serveur :
  - `A` `@` → IP du serveur
  - `A` `www` → IP du serveur
  (et `AAAA` si le serveur a une IPv6)
- Une clé d'API Web Steam : https://steamcommunity.com/dev/apikey (domaine : `warhammer-matchmaker.com`).

## Première installation

```sh
# 1. Sur le serveur, en root : Docker, pare-feu, code, deploy/.env
ssh root@<ip> 'bash -s' < deploy/bootstrap.sh

# 2. Compléter les secrets sur le serveur (ACME_EMAIL, STEAM_API_KEY)
ssh root@<ip> 'nano /opt/matchmaker/deploy/.env'

# 3. Démarrer
ssh root@<ip> 'cd /opt/matchmaker && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build'
```

Le certificat HTTPS est obtenu au premier accès, une fois le DNS propagé. Vérification : `https://<domaine>/health` doit répondre `Healthy`.

## Mettre en ligne une nouvelle version

Depuis un poste de développement, une fois les commits poussés sur `main` :

```sh
deploy/deploy.sh root@<ip>
```

Le script récupère le code, reconstruit l'image, redémarre le site et vérifie `/health`. Les migrations de base s'appliquent au démarrage.

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

Les sauvegardes restent sur le serveur : pour s'en prémunir en cas de perte du serveur, copiez régulièrement `deploy/backups/` ailleurs (ex. `scp` ou l'option de sauvegarde de l'hébergeur).

## Répétition en local

Toute la pile peut tourner sur un poste de développement, avec `localhost` (certificat local de Caddy) et d'autres ports :

```sh
cat > /tmp/rehearsal.env <<'EOF'
DOMAIN=localhost
ACME_EMAIL=test@example.com
POSTGRES_PASSWORD=repetition123
STEAM_API_KEY=cle-factice
HTTP_PORT=8088
HTTPS_PORT=8443
EOF
docker compose -p matchmaker-rehearsal -f deploy/docker-compose.yml --env-file /tmp/rehearsal.env up -d --build
curl -k https://localhost:8443/health
docker compose -p matchmaker-rehearsal -f deploy/docker-compose.yml --env-file /tmp/rehearsal.env down -v
```
