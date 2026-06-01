#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
GAME_ROOT="$(cd "$ROOT/../.." && pwd)"
MANAGED="$GAME_ROOT/Contents/Resources/Data/Managed"
BUILD="${TMPDIR:-/tmp}/phinix-redpacket-rework-build"
REF="$BUILD/ref"

mkdir -p "$REF" "$ROOT/Assemblies" "$ROOT/ServerExtensions"

MCS="${MCS:-mcs}"
COMMON_REFS=(
  -r:System.Core
  -r:System.Runtime.Serialization
)
RIMWORLD_REFS=(
  -r:"$MANAGED/netstandard.dll"
  -r:"$MANAGED/Assembly-CSharp.dll"
  -r:"$MANAGED/UnityEngine.dll"
  -r:"$MANAGED/UnityEngine.CoreModule.dll"
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll"
  -r:"$MANAGED/UnityEngine.TextRenderingModule.dll"
  -r:"$MANAGED/UnityEngine.UIModule.dll"
)

"$MCS" -target:library -langversion:latest -out:"$REF/Utils.dll" \
  "${COMMON_REFS[@]}" \
  "$ROOT/Source/Stubs/UtilsStubs.cs"

"$MCS" -target:library -langversion:latest -out:"$REF/UserManagement.dll" \
  -r:"$REF/Utils.dll" \
  "${COMMON_REFS[@]}" \
  "$ROOT/Source/Stubs/UserManagementStubs.cs"

"$MCS" -target:library -langversion:latest -out:"$REF/ClientExtensionAbstractions.dll" \
  -r:"$REF/Utils.dll" \
  -r:"$REF/UserManagement.dll" \
  "${COMMON_REFS[@]}" \
  "${RIMWORLD_REFS[@]}" \
  "$ROOT/Source/Stubs/ClientExtensionAbstractionsStubs.cs"

"$MCS" -target:library -langversion:latest -out:"$ROOT/Assemblies/12-Natsuki.PhinixRedPacket.Client.dll" \
  -r:"$REF/Utils.dll" \
  -r:"$REF/UserManagement.dll" \
  -r:"$REF/ClientExtensionAbstractions.dll" \
  "${COMMON_REFS[@]}" \
  "${RIMWORLD_REFS[@]}" \
  "$ROOT"/Source/Shared/*.cs \
  "$ROOT"/Source/Client/*.cs

"$MCS" -target:library -langversion:latest -out:"$ROOT/ServerExtensions/12-Natsuki.PhinixRedPacket.Server.dll" \
  -r:"$REF/Utils.dll" \
  -r:"$REF/UserManagement.dll" \
  "${COMMON_REFS[@]}" \
  "$ROOT"/Source/Shared/*.cs \
  "$ROOT"/Source/Server/*.cs

echo "Built:"
echo "  $ROOT/Assemblies/12-Natsuki.PhinixRedPacket.Client.dll"
echo "  $ROOT/ServerExtensions/12-Natsuki.PhinixRedPacket.Server.dll"
