#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
GAME_ROOT="${RIMWORLD_APP_ROOT:-$(cd "$ROOT/../.." && pwd)}"
MANAGED="${RIMWORLD_MANAGED:-$GAME_ROOT/Contents/Resources/Data/Managed}"

PHINIX_REWORK_ROOT="${PHINIX_REWORK_ROOT:-}"
PHINIX_CLIENT_COMMON="${PHINIX_CLIENT_COMMON:-}"
PHINIX_SERVER_BIN="${PHINIX_SERVER_BIN:-}"

if [[ -n "$PHINIX_REWORK_ROOT" ]]; then
  if [[ -z "$PHINIX_CLIENT_COMMON" ]]; then
    for candidate in \
      "$PHINIX_REWORK_ROOT/Output/phinix-rework/Common" \
      "$PHINIX_REWORK_ROOT/Output/phinix-rework/Client/Common" \
      "$PHINIX_REWORK_ROOT/Client/Common"; do
      if [[ -d "$candidate" ]]; then
        PHINIX_CLIENT_COMMON="$candidate"
        break
      fi
    done
  fi

  if [[ -z "$PHINIX_SERVER_BIN" ]]; then
    for candidate in \
      "$PHINIX_REWORK_ROOT/Output/phinix-rework/Server" \
      "$PHINIX_REWORK_ROOT/Output/phinix-rework" \
      "$PHINIX_REWORK_ROOT/Server/bin/Release/net10.0" \
      "$PHINIX_REWORK_ROOT/Server/bin/Debug/net10.0"; do
      if [[ -d "$candidate" ]]; then
        PHINIX_SERVER_BIN="$candidate"
        break
      fi
    done
  fi
fi

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

find_ref() {
  local base="$1"
  shift
  local name
  for name in "$@"; do
    if [[ -f "$base/$name" ]]; then
      printf '%s/%s\n' "$base" "$name"
      return 0
    fi
    if [[ -f "$base/Assemblies/$name" ]]; then
      printf '%s/Assemblies/%s\n' "$base" "$name"
      return 0
    fi
    if [[ -f "$base/Common/Assemblies/$name" ]]; then
      printf '%s/Common/Assemblies/%s\n' "$base" "$name"
      return 0
    fi
  done
  return 1
}

[[ -d "$MANAGED" ]] || fail "RimWorld managed assemblies not found: $MANAGED"
[[ -n "$PHINIX_CLIENT_COMMON" ]] || fail "Set PHINIX_REWORK_ROOT or PHINIX_CLIENT_COMMON to a built Phinix-Rework client Common directory."
[[ -n "$PHINIX_SERVER_BIN" ]] || fail "Set PHINIX_REWORK_ROOT or PHINIX_SERVER_BIN to a built Phinix-Rework server output directory."

UTILS_CLIENT="$(find_ref "$PHINIX_CLIENT_COMMON" "03-Utils.dll" "Utils.dll")" || fail "Cannot find Utils.dll under $PHINIX_CLIENT_COMMON"
USER_CLIENT="$(find_ref "$PHINIX_CLIENT_COMMON" "06-UserManagement.dll" "UserManagement.dll")" || fail "Cannot find UserManagement.dll under $PHINIX_CLIENT_COMMON"
ABSTRACTIONS_CLIENT="$(find_ref "$PHINIX_CLIENT_COMMON" "07-ClientExtensionAbstractions.dll" "ClientExtensionAbstractions.dll")" || fail "Cannot find ClientExtensionAbstractions.dll under $PHINIX_CLIENT_COMMON"

UTILS_SERVER="$(find_ref "$PHINIX_SERVER_BIN" "Utils.dll" "03-Utils.dll")" || fail "Cannot find server Utils.dll under $PHINIX_SERVER_BIN"
USER_SERVER="$(find_ref "$PHINIX_SERVER_BIN" "UserManagement.dll" "06-UserManagement.dll")" || fail "Cannot find server UserManagement.dll under $PHINIX_SERVER_BIN"

CLIENT_OUT="$ROOT/Output/phinix-rework/Common/Extensions"
LANGUAGE_OUT="$ROOT/Output/phinix-rework/Common/Languages"
SERVER_OUT="$ROOT/Output/phinix-rework/Server/UserExtensions"
mkdir -p "$CLIENT_OUT" "$LANGUAGE_OUT" "$SERVER_OUT"

MCS="${MCS:-mcs}"
"$MCS" -target:library -langversion:latest -out:"$CLIENT_OUT/12-Natsuki.PhinixRedPacket.Client.dll" \
  -r:System.Core \
  -r:System.Runtime.Serialization \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll" \
  -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
  -r:"$MANAGED/UnityEngine.UIModule.dll" \
  -r:"$UTILS_CLIENT" \
  -r:"$USER_CLIENT" \
  -r:"$ABSTRACTIONS_CLIENT" \
  "$ROOT"/Source/Shared/*.cs \
  "$ROOT"/Source/Client/*.cs

dotnet build "$ROOT/Source/Server/PhinixRedPacket.Server.csproj" \
  -c Release \
  -p:PhinixUtils="$UTILS_SERVER" \
  -p:PhinixUserManagement="$USER_SERVER" \
  -o "$SERVER_OUT"

cp -R "$ROOT/Languages/." "$LANGUAGE_OUT/"

echo "Built:"
echo "  $CLIENT_OUT/12-Natsuki.PhinixRedPacket.Client.dll"
echo "  $LANGUAGE_OUT"
echo "  $SERVER_OUT/12-Natsuki.PhinixRedPacket.Server.dll"
