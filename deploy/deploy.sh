#!/usr/bin/env bash
# Met en ligne la dernière image publiée par GitHub Actions (à lancer depuis un poste de développement,
# une fois la CI de main terminée). Usage : deploy/deploy.sh ubuntu@<ip-du-serveur> [domaine]
# Pour revenir à une version précise : IMAGE_TAG=<hash du commit> deploy/deploy.sh ubuntu@<ip>
set -euo pipefail

SERVER="${1:?Usage : deploy/deploy.sh ubuntu@<ip-du-serveur> [domaine]}"
DOMAIN="${2:-warhammer-matchmaker.com}"
APP_DIR="${APP_DIR:-/opt/matchmaker}"
IMAGE_TAG="${IMAGE_TAG:-latest}"

echo "==> Téléchargement de l'image ($IMAGE_TAG) et redémarrage sur $SERVER"
ssh "$SERVER" "set -e
  cd '$APP_DIR'
  git pull --ff-only
  export IMAGE_TAG='$IMAGE_TAG'
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env pull web
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
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
