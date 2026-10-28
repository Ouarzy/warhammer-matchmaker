#!/usr/bin/env bash
# macOS : installe une tâche planifiée (launchd) qui lance deploy/fetch-backups.sh chaque jour à 13 h,
# et au réveil du Mac si l'heure est passée pendant qu'il dormait. Journal : ~/Library/Logs/warhammer-matchmaker-backups.log
# À relancer après une modification de fetch-backups.sh (le script est copié).
# Désinstaller : launchctl bootout gui/$(id -u)/com.warhammer-matchmaker.backups && rm ~/Library/LaunchAgents/com.warhammer-matchmaker.backups.plist
set -euo pipefail

LABEL="com.warhammer-matchmaker.backups"
# Copie du script hors de ~/Documents : macOS interdit à une tâche de fond d'y lire (le dépôt s'y trouve souvent).
SUPPORT="$HOME/Library/Application Support/warhammer-matchmaker"
SCRIPT="$SUPPORT/fetch-backups.sh"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG="$HOME/Library/Logs/warhammer-matchmaker-backups.log"

mkdir -p "$HOME/Library/LaunchAgents" "$SUPPORT"
cp "$(cd "$(dirname "$0")" && pwd)/fetch-backups.sh" "$SCRIPT"
chmod +x "$SCRIPT"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$LABEL</string>
    <key>ProgramArguments</key>
    <array><string>/bin/bash</string><string>$SCRIPT</string></array>
    <key>StartCalendarInterval</key>
    <dict><key>Hour</key><integer>13</integer><key>Minute</key><integer>0</integer></dict>
    <key>StandardOutPath</key><string>$LOG</string>
    <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
EOF

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"
echo "Tâche installée : $PLIST (journal : $LOG)"
echo "Lancer tout de suite : launchctl kickstart gui/$(id -u)/$LABEL"
