#!/usr/bin/env bash
# Launch RimWorld with a minimal modlist + the dev spike, for automated load/logic checks.
# Usage: tools/run_spike.sh [extra args...]   (e.g. -mequit to self-terminate)
set -e
CFGDIR="/c/Users/New User/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config"
CFG="$CFGDIR/ModsConfig.xml"
BK="$CFGDIR/ModsConfig.realbackup.xml"
EXE="/e/SteamLibrary/steamapps/common/RimWorld/RimWorldWin64.exe"

# Back up the real modlist once.
[ -f "$BK" ] || cp "$CFG" "$BK"

cat > "$CFG" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.6.4633 rev1261</version>
  <activeMods>
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
    <li>cornhusk39.medicalexperimentation</li>
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
EOF

echo "modlist swapped; launching..."
"$EXE" -quicktest -mespike "$@" &
echo "launched"
