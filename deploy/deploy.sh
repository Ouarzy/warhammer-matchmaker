#!/usr/bin/env bash
# Met en ligne la dernière version de la branche main (à lancer depuis un poste de développement).
# Usage : deploy/deploy.sh root@<ip-du-serveur> [domaine]
set -euo pipefail

SERVER="${1:?Usage : deploy/deploy.sh root@<ip-du-serveur> [domaine]}"
DOMAIN="${2:-warhammer-matchmaker.com}"
APP_DIR="${APP_DIR:-/opt/matchmaker}"

echo "==> Mise à jour du code et redémarrage sur $SERVER"
ssh "$SERVER" "set -e
  cd '$APP_DIR'
  git pull --ff-only
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
  docker image prune -f >/dev/null"

echo "==> Vérification de https://$DOMAIN/health"
for _ in $(seq 1 30); do
  if [[ "$(curl -fsS "https://$DOMAIN/health" 2>/dev/null)" == "Healthy" ]]; then
    echo "En ligne : https://$DOMAIN"
    exit 0
  fi
  sleep 2
done
echo "Le site ne répond pas encore correctement : voir 'docker compose -f deploy/docker-compose.yml logs web' sur le serveur." >&2
exit 1
