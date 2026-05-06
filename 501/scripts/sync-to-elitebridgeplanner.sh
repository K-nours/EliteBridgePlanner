#!/usr/bin/env zsh
# Synchronise 501.gc/ → EliteBridgePlanner/501/, puis commit + push sur la branche 501.gc

set -euo pipefail

SRC="/Users/biboxm1/Documents/Projets perso/501.gc/"
DEST="/Users/biboxm1/Documents/Projets perso/EliteBridgePlanner/501/"
REPO="/Users/biboxm1/Documents/Projets perso/EliteBridgePlanner"
BRANCH="501.gc"

# --- Paramètre obligatoire : message de commit ---
if [[ $# -lt 1 || -z "${1:-}" ]]; then
  print -u2 "Usage : $0 \"message de commit\""
  exit 1
fi
COMMIT_MSG="$1"

# --- Vérification branche courante ---
CURRENT_BRANCH=$(git -C "$REPO" rev-parse --abbrev-ref HEAD)
if [[ "$CURRENT_BRANCH" != "$BRANCH" ]]; then
  print -u2 "Erreur : la branche active dans EliteBridgePlanner est « $CURRENT_BRANCH », pas « $BRANCH »."
  print -u2 "Faites « git -C \"$REPO\" checkout $BRANCH » puis relancez."
  exit 1
fi

# --- Synchronisation rsync ---
echo "→ Synchronisation rsync…"
rsync -av --delete \
  --exclude='.angular/' \
  --exclude='node_modules/' \
  --exclude='dist/' \
  --exclude='tmp/' \
  --exclude='.DS_Store' \
  --exclude='bin/' \
  --exclude='obj/' \
  --exclude='wwwroot/browser/' \
  --exclude='server/Data/frontier-journal/' \
  --delete-excluded \
  "$SRC" "$DEST"

# --- Staging ---
echo "→ git add…"
git -C "$REPO" add "$DEST"

# --- Vérification : y a-t-il des changements ? ---
if git -C "$REPO" diff --cached --quiet; then
  echo "Aucun changement à committer. Le dépôt est déjà à jour."
  exit 0
fi

# --- Commit ---
echo "→ git commit…"
git -C "$REPO" commit -m "$COMMIT_MSG"

# --- Push ---
echo "→ git push origin $BRANCH…"
git -C "$REPO" push origin "$BRANCH"

echo "✓ Synchronisation, commit et push terminés."
