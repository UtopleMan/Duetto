#!/usr/bin/env bash
# Run the SMB integration tests against a throwaway Samba container.
#
#   scripts/smb-it.sh
#
# Brings up docker-compose.smb.yml, waits for the SMB port, runs the gated
# SmbIntegrationTests (DUETTO_SMB_TEST), then tears the container down. Requires
# Docker and the .NET SDK. Host port 445 must be free (SMBLibrary dials 445
# directly and has no custom-port support).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker-compose.smb.yml"

cleanup() {
  docker compose -f "$compose_file" down --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Starting Samba container"
docker compose -f "$compose_file" up -d samba

echo "==> Waiting for SMB on 127.0.0.1:445"
for _ in $(seq 1 30); do
  if nc -z 127.0.0.1 445 2>/dev/null; then
    ready=1
    break
  fi
  sleep 1
done
if [ "${ready:-0}" != "1" ]; then
  echo "SMB port 445 never became ready" >&2
  exit 1
fi

echo "==> Running SMB integration tests"
DUETTO_SMB_TEST=1 \
DUETTO_SMB_TEST_HOST=127.0.0.1 \
DUETTO_SMB_TEST_USER=smbuser \
DUETTO_SMB_TEST_PASSWORD=smbpass \
DUETTO_SMB_TEST_DOMAIN=WORKGROUP \
DUETTO_SMB_TEST_SHARE=duetto \
DUETTO_SMB_TEST_GUEST_SHARE=public \
  dotnet test "$repo_root/tests/Duetto.Tests/Duetto.Tests.csproj" \
    --filter "Category=Integration&FullyQualifiedName~Smb"

echo "==> SMB integration tests passed"
