#!/usr/bin/env bash
# Installation initiale d'un serveur Ubuntu 24.04 (à lancer une fois, en root, sur le serveur).
# Installe Docker, ouvre uniquement SSH/HTTP/HTTPS, récupère le code et prépare deploy/.env.
set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/Ouarzy/warhammer-matchmaker.git}"
APP_DIR="${APP_DIR:-/opt/matchmaker}"

echo "==> Paquets système et Docker"
apt-get update
apt-get install -y ca-certificates git ufw docker.io docker-compose-v2
systemctl enable --now docker

# Petit VPS (1 Go) : 1 Go de mémoire d'appoint par sécurité lors des pics.
if ! swapon --show | grep -q .; then
  echo "==> Mémoire d'appoint (swap) de 1 Go"
  fallocate -l 1G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "==> Pare-feu : SSH, HTTP, HTTPS uniquement"
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

echo "==> Code du site dans $APP_DIR"
if [[ -d "$APP_DIR/.git" ]]; then
  git -C "$APP_DIR" pull --ff-only
else
  git clone "$REPO_URL" "$APP_DIR"
fi

ENV_FILE="$APP_DIR/deploy/.env"
if [[ ! -f "$ENV_FILE" ]]; then
  echo "==> Création de $ENV_FILE (mot de passe de base généré)"
  sed "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 24)/" "$APP_DIR/deploy/.env.example" > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
fi
mkdir -p "$APP_DIR/deploy/backups"

echo
echo "Terminé. Complétez ACME_EMAIL et STEAM_API_KEY dans $ENV_FILE, puis lancez :"
echo "  cd $APP_DIR && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d"
