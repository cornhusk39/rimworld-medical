#!/usr/bin/env bash
# Restore the user's real modlist after spike testing.
CFGDIR="/c/Users/New User/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config"
CFG="$CFGDIR/ModsConfig.xml"
BK="$CFGDIR/ModsConfig.realbackup.xml"
if [ -f "$BK" ]; then cp "$BK" "$CFG"; echo "modlist restored"; else echo "no backup found"; fi
