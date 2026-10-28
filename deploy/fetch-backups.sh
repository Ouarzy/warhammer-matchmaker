#!/usr/bin/env bash
# Copie les sauvegardes quotidiennes du serveur sur ce poste (seuls les nouveaux fichiers sont transférés).
# Le serveur n'en garde que 14 jours ; ici, rien n'est supprimé.
# Usage : deploy/fetch-backups.sh [ubuntu@<ip-du-serveur>] [dossier local]
set -euo pipefail

SERVER="${1:-ubuntu@46.226.107.145}"
# Hors de ~/Documents : macOS y interdit l'écriture aux tâches de fond.
DESTINATION="${2:-$HOME/warhammer-matchmaker/backup}"
APP_DIR="${APP_DIR:-/opt/matchmaker}"

# Tâche planifiée : l'agent SSH de macOS n'a pas les clés. On reprend l'agent ouvert par le terminal
# (plugin ssh-agent d'oh-my-zsh, qui note son adresse dans ~/.ssh/environment-<machine>).
if ! ssh-add -l >/dev/null 2>&1; then
  for environment in "$HOME"/.ssh/environment-*; do
    [[ -f "$environment" ]] || continue
    # shellcheck disable=SC1090
    source "$environment" >/dev/null
    ssh-add -l >/dev/null 2>&1 && break
  done
fi

mkdir -p "$DESTINATION"
echo "==> $(date '+%F %T') : sauvegardes de $SERVER vers $DESTINATION"
# BatchMode : jamais de question interactive (tâche planifiée) ; échec net si la clé SSH n'est pas disponible.
rsync -a --ignore-existing -e "ssh -o BatchMode=yes -o ConnectTimeout=20" \
  "$SERVER:$APP_DIR/deploy/backups/" "$DESTINATION/"
echo "==> Terminé : $(find "$DESTINATION" -name '*.dump' | wc -l | tr -d ' ') sauvegarde(s) sur ce poste"
