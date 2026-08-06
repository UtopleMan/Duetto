#!/usr/bin/env bash
# Smoke test: stand up the throwaway backend servers and run Duetto's gated
# integration tests against them.
#
#   scripts/smoke.sh
#
# Brings up docker-compose.yml (samba + sftp + minio), waits for each backend,
# runs the SMB and SFTP integration tests (Category=Integration), then tears
# everything down. Requires Docker and the .NET SDK.
#
# Host port 445 must be free (SMBLibrary dials 445 directly, no custom-port support).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker-compose.yml"

cleanup() {
  docker compose -f "$compose_file" down --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

# Wait until a TCP port answers (or fail after ~30s).
wait_for_port() {
  local host="$1" port="$2" label="$3"
  echo "==> Waiting for $label on $host:$port"
  for _ in $(seq 1 30); do
    if nc -z "$host" "$port" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "$label on $host:$port never became ready" >&2
  exit 1
}

echo "==> Starting backend containers"
docker compose -f "$compose_file" up -d

wait_for_port 127.0.0.1 445 "SMB"
wait_for_port 127.0.0.1 2222 "SFTP"
wait_for_port 127.0.0.1 9000 "MinIO"
wait_for_port 127.0.0.1 10000 "Azurite"

# Give the SFTP handshake a moment to settle after the port opens.
sleep 2

echo "==> Running integration tests (SMB + SFTP + S3 + Azure)"
DUETTO_SMB_TEST=1 \
DUETTO_SMB_TEST_HOST=127.0.0.1 \
DUETTO_SMB_TEST_USER=smbuser \
DUETTO_SMB_TEST_PASSWORD=smbpass \
DUETTO_SMB_TEST_DOMAIN=WORKGROUP \
DUETTO_SMB_TEST_SHARE=duetto \
DUETTO_SMB_TEST_GUEST_SHARE=public \
DUETTO_SFTP_TEST=1 \
DUETTO_SFTP_TEST_HOST=127.0.0.1 \
DUETTO_SFTP_TEST_PORT=2222 \
DUETTO_SFTP_TEST_USER=test \
DUETTO_SFTP_TEST_PASSWORD=test \
DUETTO_SFTP_TEST_PATH=/upload \
DUETTO_S3_TEST=1 \
DUETTO_S3_TEST_ENDPOINT=http://127.0.0.1:9000 \
DUETTO_S3_TEST_ACCESS=duetto \
DUETTO_S3_TEST_SECRET=duettosecret \
DUETTO_S3_TEST_BUCKET=duetto \
DUETTO_AZURE_TEST=1 \
DUETTO_AZURE_TEST_ENDPOINT=http://127.0.0.1:10000/devstoreaccount1 \
DUETTO_AZURE_TEST_ACCOUNT=devstoreaccount1 \
DUETTO_AZURE_TEST_KEY=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw== \
DUETTO_AZURE_TEST_CONTAINER=duetto \
  dotnet test "$repo_root/tests/Duetto.Tests/Duetto.Tests.csproj" \
    --filter "Category=Integration"

echo "==> Smoke test passed"
