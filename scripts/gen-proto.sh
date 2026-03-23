#!/usr/bin/env bash
#
# Regenerates the vendored C# protobuf bindings for the OTLP sink
# from the opentelemetry-proto submodule.
#
# Prerequisites:
#   - protoc (apt install protobuf-compiler / brew install protobuf)
#   - grpc_csharp_plugin (from Grpc.Tools NuGet, or install separately)
#
# Usage:
#   ./scripts/gen-proto.sh [--grpc-plugin PATH]
#
# If --grpc-plugin is not provided, the script looks for it in the
# Grpc.Tools NuGet cache.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROTO_ROOT="$REPO_ROOT/Clip.OpenTelemetry/proto"
OUT_DIR="$REPO_ROOT/Clip.OpenTelemetry/Generated"

# Find grpc_csharp_plugin
GRPC_PLUGIN="${GRPC_PLUGIN:-}"
if [[ -z "$GRPC_PLUGIN" ]]; then
  for arg in "$@"; do
    if [[ "$arg" == "--grpc-plugin" ]]; then
      shift
      GRPC_PLUGIN="$1"
      shift
      break
    fi
  done
fi

if [[ -z "$GRPC_PLUGIN" ]]; then
  # Detect platform for NuGet cache lookup
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64)  GRPC_PLATFORM="macosx_x64" ;;  # no arm64 build; x64 runs via Rosetta
    Darwin-x86_64) GRPC_PLATFORM="macosx_x64" ;;
    Linux-aarch64) GRPC_PLATFORM="linux_arm64" ;;
    Linux-x86_64)  GRPC_PLATFORM="linux_x64" ;;
    *)             GRPC_PLATFORM="" ;;
  esac

  if [[ -n "$GRPC_PLATFORM" ]]; then
    GRPC_PLUGIN=$(find "$HOME/.nuget/packages/grpc.tools" -path "*/$GRPC_PLATFORM/grpc_csharp_plugin" -type f 2>/dev/null | sort -V | tail -1 || true)
  fi
fi

if [[ -z "$GRPC_PLUGIN" || ! -x "$GRPC_PLUGIN" ]]; then
  echo "Error: grpc_csharp_plugin not found. Install Grpc.Tools NuGet or pass --grpc-plugin PATH."
  exit 1
fi

if [[ ! -d "$PROTO_ROOT" ]]; then
  echo "Error: Proto submodule not found at $PROTO_ROOT"
  echo "Run: git submodule update --init"
  exit 1
fi

echo "Proto root:    $PROTO_ROOT"
echo "Output dir:    $OUT_DIR"
echo "gRPC plugin:   $GRPC_PLUGIN"

mkdir -p "$OUT_DIR"

protoc \
  --proto_path="$PROTO_ROOT" \
  --csharp_out="$OUT_DIR" \
  --csharp_opt=internal_access \
  --grpc_out="$OUT_DIR" \
  --grpc_opt=internal_access \
  --plugin=protoc-gen-grpc="$GRPC_PLUGIN" \
  opentelemetry/proto/common/v1/common.proto \
  opentelemetry/proto/resource/v1/resource.proto \
  opentelemetry/proto/logs/v1/logs.proto \
  opentelemetry/proto/collector/logs/v1/logs_service.proto

echo "Done. Generated $(ls "$OUT_DIR"/*.cs | wc -l | tr -d ' ') files."
