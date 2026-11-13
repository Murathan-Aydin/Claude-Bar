#!/usr/bin/env bash
# Compose les captures 1920×1080 de la fiche Microsoft Store à partir des rendus de l'autotest Windows
# (artefact CI « ClaudeBar-Windows » → dossier selftest/).
# Usage : ./windows/tools/make-store-screenshots.sh <dossier-selftest>
set -euo pipefail

SRC="${1:?Usage : $0 <dossier-selftest>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/store/screenshots"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$OUT"
FONT="${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}"
FONT_BOLD="${FONT_BOLD:-/System/Library/Fonts/Supplemental/Arial Bold.ttf}"

# compose <nom> <thème light|dark> <popup.png> <icone-zone-notif.png> <légende>
compose() {
  local name=$1 theme=$2 popup=$3 tray=$4 caption=$5
  local bg1 bg2 bar fg
  if [ "$theme" = dark ]; then bg1='#1b2a4a'; bg2='#0b0f1a'; bar='#1c1c1cE6'; fg='#ffffff'
  else bg1='#cfe3ff'; bg2='#8db8f2'; bar='#f3f3f3E6'; fg='#1b1b1b'; fi

  magick -size 1920x1080 "gradient:$bg1-$bg2" "$TMP/bg.png"
  # Barre des tâches + icône Claude Bar dans la zone de notification
  magick "$TMP/bg.png" -fill "$bar" -draw "rectangle 0,1020 1920,1080" \
    \( "$tray" -filter point -resize 24x24 \) -geometry +1700+1038 -composite \
    -fill "$fg" -font "$FONT" -pointsize 15 -annotate +1790+1056 "14:32" "$TMP/desk.png"
  # Fenêtre (ombre portée) au-dessus de l'icône
  magick "$popup" \( +clone -background black -shadow 45x14+0+8 \) +swap -background none -layers merge +repage "$TMP/popup.png"
  local ph; ph=$(magick identify -format '%h' "$TMP/popup.png")
  local pw; pw=$(magick identify -format '%w' "$TMP/popup.png")
  local px=$(( 1712 - pw / 2 )); local py=$(( 1020 - 16 - ph ))
  [ $(( px + pw )) -gt 1904 ] && px=$(( 1904 - pw ))
  magick "$TMP/desk.png" "$TMP/popup.png" -geometry "+${px}+${py}" -composite \
    -fill "$fg" -font "$FONT_BOLD" -pointsize 54 -annotate +120+200 "$caption" \
    "$OUT/$name.png"
  echo "  $OUT/$name.png"
}

echo "Captures générées :"
compose 01-utilisation-clair light "$SRC/popup-data-light-192.png" "$SRC/tray-42.png" "Ton utilisation de Claude, d'un coup d'œil"
compose 02-utilisation-sombre dark "$SRC/popup-data-dark-192.png" "$SRC/tray-42.png" "Session de 5 h et limite hebdomadaire"
compose 03-connexion light "$SRC/popup-loggedout-light-192.png" "$SRC/tray-loggedout.png" "Connexion à claude.ai en un clic"
