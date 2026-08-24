#!/usr/bin/env bash
#
# run-docker.sh
#
# Convenience wrapper around 'docker build' and 'docker run' for the
# Rehearsal Forecast web application.
#
# Subcommands:
#   up        Build the image if missing, then start the container detached.
#             (Default when no subcommand is given.)
#   down      Stop and remove the container.
#   restart   Down, then up.
#   rebuild   Force a fresh 'docker build --no-cache', then up.
#   logs      Tail the container's logs (Ctrl+C to stop tailing).
#   status    Show whether the container is running and on what port.
#   shell     Open an interactive shell inside the running container.
#
# Environment overrides (all optional):
#   IMAGE_NAME     Tag for the built image     (default: rehearsal-forecast:local)
#   CONTAINER_NAME Name of the container       (default: rehearsal-forecast)
#   HOST_PORT      Host port to publish 8080   (default: 8080)
#
# Examples:
#   ./scripts/run-docker.sh                 # up
#   ./scripts/run-docker.sh rebuild
#   HOST_PORT=9090 ./scripts/run-docker.sh  # up on http://localhost:9090
#   ./scripts/run-docker.sh logs
#   ./scripts/run-docker.sh down

set -euo pipefail

# ---------- Locate the repo root ------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

if [[ ! -f "${REPO_DIR}/Dockerfile" ]]; then
  echo "Dockerfile not found at ${REPO_DIR}/Dockerfile" >&2
  exit 1
fi

# ---------- Configurable defaults -----------------------------------------

IMAGE_NAME="${IMAGE_NAME:-rehearsal-forecast:local}"
CONTAINER_NAME="${CONTAINER_NAME:-rehearsal-forecast}"
HOST_PORT="${HOST_PORT:-8080}"
CONTAINER_PORT=8080  # matches EXPOSE 8080 in the Dockerfile

# ---------- Pretty output -------------------------------------------------

if [[ -t 1 ]]; then
  BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'
  YELLOW=$'\033[33m'; BLUE=$'\033[34m'; RESET=$'\033[0m'
else
  BOLD=""; RED=""; GREEN=""; YELLOW=""; BLUE=""; RESET=""
fi

log()  { printf "%s==>%s %s\n" "${BLUE}${BOLD}" "${RESET}" "$*"; }
ok()   { printf "%s✓%s  %s\n" "${GREEN}${BOLD}" "${RESET}" "$*"; }
warn() { printf "%s!%s  %s\n" "${YELLOW}${BOLD}" "${RESET}" "$*"; }
fail() { printf "%s✗%s  %s\n" "${RED}${BOLD}" "${RESET}" "$*" >&2; exit 1; }

# ---------- Docker availability -------------------------------------------

require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    fail "'docker' is not on PATH. Install Docker Desktop (run ./scripts/install-mac-deps.sh on macOS) and launch it once."
  fi
  if ! docker info >/dev/null 2>&1; then
    fail "The Docker daemon is not running. Open Docker Desktop and wait for it to finish starting, then re-run."
  fi
}

# ---------- Helpers -------------------------------------------------------

image_exists() {
  docker image inspect "${IMAGE_NAME}" >/dev/null 2>&1
}

container_exists() {
  docker container inspect "${CONTAINER_NAME}" >/dev/null 2>&1
}

container_running() {
  [[ "$(docker container inspect -f '{{.State.Running}}' "${CONTAINER_NAME}" 2>/dev/null || echo false)" == "true" ]]
}

port_in_use() {
  # 0 if $HOST_PORT is bound on the loopback interface by something other
  # than our own container. Uses lsof, which is preinstalled on macOS.
  if ! command -v lsof >/dev/null 2>&1; then
    return 1
  fi
  lsof -nP -iTCP:"${HOST_PORT}" -sTCP:LISTEN >/dev/null 2>&1
}

build_image() {
  local extra_args=("$@")
  log "Building image ${IMAGE_NAME} from ${REPO_DIR}..."
  ( cd "${REPO_DIR}" && docker build "${extra_args[@]}" -t "${IMAGE_NAME}" . )
  ok "Image built: ${IMAGE_NAME}"
}

remove_container_if_present() {
  if container_exists; then
    log "Removing existing container ${CONTAINER_NAME}..."
    docker rm -f "${CONTAINER_NAME}" >/dev/null
    ok "Removed ${CONTAINER_NAME}"
  fi
}

start_container() {
  # If the container already exists but is stopped, restart it; otherwise
  # run a fresh one. If it's already running we're a no-op.
  if container_running; then
    ok "Container ${CONTAINER_NAME} is already running."
  else
    if container_exists; then
      log "Restarting existing container ${CONTAINER_NAME}..."
      docker start "${CONTAINER_NAME}" >/dev/null
    else
      # Guard against a port collision with a non-Docker process.
      if port_in_use; then
        warn "Host port ${HOST_PORT} is already in use by another process."
        warn "Either stop that process or re-run with HOST_PORT=<free-port> ./scripts/run-docker.sh"
        exit 1
      fi
      log "Starting container ${CONTAINER_NAME} on http://localhost:${HOST_PORT}..."
      docker run -d \
        --name "${CONTAINER_NAME}" \
        -p "${HOST_PORT}:${CONTAINER_PORT}" \
        "${IMAGE_NAME}" >/dev/null
    fi
  fi

  # Small readiness probe so the printed URL actually resolves when the
  # user clicks it.
  local attempts=0
  local max_attempts=20
  while (( attempts < max_attempts )); do
    if curl -fsS -o /dev/null "http://localhost:${HOST_PORT}/" 2>/dev/null; then
      ok "App is up: http://localhost:${HOST_PORT}/"
      return 0
    fi
    sleep 0.5
    attempts=$((attempts + 1))
  done
  warn "App did not respond on http://localhost:${HOST_PORT}/ within ~10s."
  warn "Check logs with: ./scripts/run-docker.sh logs"
  return 1
}

# ---------- Subcommands ---------------------------------------------------

cmd_up() {
  require_docker
  if ! image_exists; then
    build_image
  else
    log "Reusing existing image ${IMAGE_NAME} (run 'rebuild' to force a fresh build)."
  fi
  start_container
}

cmd_down() {
  require_docker
  if container_exists; then
    log "Stopping and removing ${CONTAINER_NAME}..."
    docker rm -f "${CONTAINER_NAME}" >/dev/null
    ok "Stopped."
  else
    warn "No container named ${CONTAINER_NAME} to stop."
  fi
}

cmd_restart() {
  cmd_down
  cmd_up
}

cmd_rebuild() {
  require_docker
  remove_container_if_present
  build_image --no-cache
  start_container
}

cmd_logs() {
  require_docker
  if ! container_exists; then
    fail "No container named ${CONTAINER_NAME} exists. Run './scripts/run-docker.sh up' first."
  fi
  log "Tailing logs for ${CONTAINER_NAME} (Ctrl+C to stop)..."
  docker logs -f "${CONTAINER_NAME}"
}

cmd_status() {
  require_docker
  if container_running; then
    ok "${CONTAINER_NAME} is running."
    docker ps --filter "name=^${CONTAINER_NAME}$" \
      --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}\t{{.Image}}'
  elif container_exists; then
    warn "${CONTAINER_NAME} exists but is stopped."
    docker ps -a --filter "name=^${CONTAINER_NAME}$" \
      --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
  else
    warn "${CONTAINER_NAME} is not present."
  fi
}

cmd_shell() {
  require_docker
  if ! container_running; then
    fail "${CONTAINER_NAME} is not running. Run './scripts/run-docker.sh up' first."
  fi
  # The runtime image is Debian-based; /bin/bash is available. Fall back
  # to /bin/sh just in case.
  if docker exec -it "${CONTAINER_NAME}" /bin/bash 2>/dev/null; then
    :
  else
    docker exec -it "${CONTAINER_NAME}" /bin/sh
  fi
}

usage() {
  sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# ---------- Entry point ---------------------------------------------------

subcommand="${1:-up}"
case "${subcommand}" in
  up)       cmd_up ;;
  down)     cmd_down ;;
  restart)  cmd_restart ;;
  rebuild)  cmd_rebuild ;;
  logs)     cmd_logs ;;
  status)   cmd_status ;;
  shell)    cmd_shell ;;
  -h|--help|help) usage ;;
  *)
    echo "Unknown subcommand: ${subcommand}" >&2
    echo
    usage
    exit 2
    ;;
esac
