#!/usr/bin/env bash
#
# install-mac-deps.sh
#
# Installs everything needed to build, test, run, and containerize the
# Rehearsal Forecast application on macOS (Intel or Apple Silicon).
#
# What it installs:
#   1. Xcode Command Line Tools (prerequisite for Homebrew).
#   2. Homebrew (if missing).
#   3. Git (via Homebrew if the system git is missing).
#   4. .NET SDK at the exact version pinned in global.json (via the
#      official Microsoft dotnet-install.sh script, installed to
#      ~/.dotnet).
#   5. Docker Desktop for Mac (via the Homebrew cask).
#   6. Terraform (via the hashicorp/tap Homebrew tap).
#
# The script is idempotent: it detects existing installations and skips
# them.
#
# Usage:
#   ./scripts/install-mac-deps.sh
#
# Exit codes:
#   0 - success
#   non-zero - failure at some step

set -euo pipefail

# ---------- Pretty output --------------------------------------------------

if [[ -t 1 ]]; then
  BOLD=$'\033[1m'
  RED=$'\033[31m'
  GREEN=$'\033[32m'
  YELLOW=$'\033[33m'
  BLUE=$'\033[34m'
  RESET=$'\033[0m'
else
  BOLD=""; RED=""; GREEN=""; YELLOW=""; BLUE=""; RESET=""
fi

log()   { printf "%s==>%s %s\n" "${BLUE}${BOLD}" "${RESET}" "$*"; }
ok()    { printf "%s✓%s  %s\n" "${GREEN}${BOLD}" "${RESET}" "$*"; }
warn()  { printf "%s!%s  %s\n" "${YELLOW}${BOLD}" "${RESET}" "$*"; }
fail()  { printf "%s✗%s  %s\n" "${RED}${BOLD}" "${RESET}" "$*" >&2; exit 1; }

# ---------- Preconditions --------------------------------------------------

if [[ "$(uname -s)" != "Darwin" ]]; then
  fail "This script targets macOS only. Detected: $(uname -s)"
fi

ARCH="$(uname -m)"
case "$ARCH" in
  arm64)  BREW_PREFIX_DEFAULT="/opt/homebrew" ;;
  x86_64) BREW_PREFIX_DEFAULT="/usr/local"    ;;
  *) fail "Unsupported architecture: $ARCH" ;;
esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
GLOBAL_JSON="${REPO_DIR}/global.json"

if [[ ! -f "${GLOBAL_JSON}" ]]; then
  fail "global.json not found at ${GLOBAL_JSON}. Run this script from the checked-out repository."
fi

log "Repository root: ${REPO_DIR}"
log "Architecture:    ${ARCH}"

# ---------- 1. Xcode Command Line Tools ------------------------------------

log "Checking Xcode Command Line Tools..."
if xcode-select -p >/dev/null 2>&1; then
  ok "Xcode Command Line Tools already installed at $(xcode-select -p)."
else
  warn "Installing Xcode Command Line Tools. A GUI prompt may appear — accept it and wait for it to finish, then re-run this script."
  xcode-select --install || true
  fail "Re-run this script once the Xcode CLT installation completes."
fi

# ---------- 2. Homebrew ----------------------------------------------------

log "Checking Homebrew..."
if command -v brew >/dev/null 2>&1; then
  ok "Homebrew already installed at $(command -v brew)."
else
  log "Installing Homebrew (non-interactive)..."
  NONINTERACTIVE=1 /bin/bash -c \
    "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
fi

# Make sure brew is on PATH for the rest of this script even on a brand-new
# install where the user has not yet sourced their shell profile.
if ! command -v brew >/dev/null 2>&1; then
  if [[ -x "${BREW_PREFIX_DEFAULT}/bin/brew" ]]; then
    eval "$(${BREW_PREFIX_DEFAULT}/bin/brew shellenv)"
  else
    fail "Homebrew install did not put 'brew' on PATH. Aborting."
  fi
fi
BREW_PREFIX="$(brew --prefix)"
ok "brew: $(command -v brew) (prefix: ${BREW_PREFIX})"

# ---------- 3. Git ---------------------------------------------------------

log "Checking Git..."
if command -v git >/dev/null 2>&1; then
  ok "git: $(git --version)"
else
  log "Installing git via Homebrew..."
  brew install git
  ok "git: $(git --version)"
fi

# ---------- 4. .NET 10 SDK (exact version from global.json) ----------------

log "Reading pinned .NET SDK version from global.json..."
# Extract the "version" value with a plain-text sed. No jq dependency required.
PINNED_SDK="$(sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "${GLOBAL_JSON}" | head -n1)"
if [[ -z "${PINNED_SDK}" ]]; then
  fail "Could not parse the pinned SDK version from ${GLOBAL_JSON}."
fi
log "Pinned SDK: ${PINNED_SDK}"

# The dotnet-install.sh script installs to ~/.dotnet by default.
DOTNET_INSTALL_DIR="${HOME}/.dotnet"

sdk_installed() {
  # Look for the exact pinned SDK in either the ~/.dotnet install or any
  # dotnet already on PATH.
  local candidate_cmd=""
  if [[ -x "${DOTNET_INSTALL_DIR}/dotnet" ]]; then
    candidate_cmd="${DOTNET_INSTALL_DIR}/dotnet"
  elif command -v dotnet >/dev/null 2>&1; then
    candidate_cmd="$(command -v dotnet)"
  fi
  [[ -z "${candidate_cmd}" ]] && return 1
  "${candidate_cmd}" --list-sdks 2>/dev/null | awk '{print $1}' | grep -Fxq "${PINNED_SDK}"
}

log "Checking for .NET SDK ${PINNED_SDK}..."
if sdk_installed; then
  ok ".NET SDK ${PINNED_SDK} already installed."
else
  log "Installing .NET SDK ${PINNED_SDK} to ${DOTNET_INSTALL_DIR}..."
  DOTNET_INSTALL_SCRIPT="$(mktemp -t dotnet-install.XXXXXX.sh)"
  # shellcheck disable=SC2064
  trap "rm -f '${DOTNET_INSTALL_SCRIPT}'" EXIT
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${DOTNET_INSTALL_SCRIPT}"
  chmod +x "${DOTNET_INSTALL_SCRIPT}"
  "${DOTNET_INSTALL_SCRIPT}" \
    --version "${PINNED_SDK}" \
    --install-dir "${DOTNET_INSTALL_DIR}" \
    --no-path
  rm -f "${DOTNET_INSTALL_SCRIPT}"
  trap - EXIT
  ok ".NET SDK ${PINNED_SDK} installed."
fi

# Make sure the just-installed SDK is discoverable in future shells.
# Detect the user's login shell profile and append the exports if missing.
SHELL_NAME="$(basename "${SHELL:-/bin/zsh}")"
case "${SHELL_NAME}" in
  zsh)  PROFILE="${HOME}/.zshrc"  ;;
  bash) PROFILE="${HOME}/.bash_profile" ;;
  *)    PROFILE="${HOME}/.profile" ;;
esac
if [[ ! -f "${PROFILE}" ]]; then
  touch "${PROFILE}"
fi

DOTNET_MARKER="# added by rehearsal-forecast install-mac-deps.sh"
if ! grep -Fq "${DOTNET_MARKER}" "${PROFILE}" 2>/dev/null; then
  log "Appending .NET PATH and DOTNET_ROOT to ${PROFILE}..."
  {
    echo ""
    echo "${DOTNET_MARKER}"
    echo "export DOTNET_ROOT=\"\$HOME/.dotnet\""
    echo "export PATH=\"\$DOTNET_ROOT:\$PATH\""
  } >> "${PROFILE}"
  ok "Shell profile updated. Open a new terminal, or 'source ${PROFILE}', to pick up the new PATH."
else
  ok "Shell profile ${PROFILE} already exports DOTNET_ROOT."
fi

# For version checks below in *this* shell:
export DOTNET_ROOT="${DOTNET_INSTALL_DIR}"
export PATH="${DOTNET_ROOT}:${PATH}"

# ---------- 5. Docker Desktop ---------------------------------------------

log "Checking Docker Desktop..."
if command -v docker >/dev/null 2>&1; then
  ok "docker: $(docker --version)"
else
  if [[ -d "/Applications/Docker.app" ]]; then
    warn "Docker.app is installed but 'docker' is not on PATH. Launch Docker Desktop once so it registers its CLI shims."
  else
    log "Installing Docker Desktop via Homebrew cask..."
    brew install --cask docker
    warn "Docker Desktop was installed. Launch it once from Applications so it can request Rosetta / privileged permissions and start its background service."
  fi
fi

# ---------- 6. Terraform ---------------------------------------------------

log "Checking Terraform..."
TERRAFORM_MIN="1.7.0"   # matches infrastructure/terraform/environments/dev/versions.tf
version_ge() {
  # Returns 0 iff $1 >= $2 in dotted-version order.
  # Uses sort -V so it works for any semver-ish string.
  [[ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -n1)" == "$2" ]]
}
if command -v terraform >/dev/null 2>&1; then
  TF_CURRENT="$(terraform version -json 2>/dev/null | sed -n 's/.*"terraform_version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
  if [[ -z "${TF_CURRENT}" ]]; then
    # Fall back to plaintext parsing for older CLIs that lack -json.
    TF_CURRENT="$(terraform version | head -n1 | awk '{gsub(/^v/,"",$2); print $2}')"
  fi
  if version_ge "${TF_CURRENT}" "${TERRAFORM_MIN}"; then
    ok "terraform: v${TF_CURRENT} (satisfies >= ${TERRAFORM_MIN})"
  else
    warn "terraform: v${TF_CURRENT} is BELOW the required ${TERRAFORM_MIN} (see infrastructure/terraform/environments/dev/versions.tf)."
    warn "Upgrading via Homebrew..."
    brew tap hashicorp/tap 2>/dev/null || true
    brew install hashicorp/tap/terraform || brew upgrade hashicorp/tap/terraform
    ok "terraform: $(terraform version | head -n1)"
  fi
else
  log "Adding hashicorp/tap and installing Terraform..."
  brew tap hashicorp/tap
  brew install hashicorp/tap/terraform
  ok "terraform: $(terraform version | head -n1)"
fi

# ---------- Summary --------------------------------------------------------

echo
log "Verifying installed tool versions..."
printf "  %-14s %s\n" "git:"       "$(git --version 2>/dev/null || echo 'MISSING')"
printf "  %-14s %s\n" "dotnet:"    "$(dotnet --version 2>/dev/null || echo 'MISSING')"
printf "  %-14s %s\n" "dotnet SDKs:" "$(dotnet --list-sdks 2>/dev/null | awk '{print $1}' | paste -sd, - || echo '')"
printf "  %-14s %s\n" "docker:"    "$(docker --version 2>/dev/null || echo 'not on PATH — launch Docker.app once')"
printf "  %-14s %s\n" "terraform:" "$(terraform version 2>/dev/null | head -n1 || echo 'MISSING')"

echo
ok "All dependencies are installed."
cat <<EOF

Next steps:
  1. Open a new terminal (or 'source ${PROFILE}') so the .NET PATH takes effect.
  2. From the repository root (${REPO_DIR}):
       dotnet restore RehearsalForecast.sln
       dotnet build   RehearsalForecast.sln -c Release
       dotnet test    RehearsalForecast.sln -c Release
       dotnet run --project src/RehearsalForecast.Web
  3. To build and run the container image:
       docker build -t rehearsal-forecast:local .
       docker run --rm -p 8080:8080 rehearsal-forecast:local
  4. To validate the Terraform scaffolding (no cloud resources are created):
       cd infrastructure/terraform/environments/dev
       terraform fmt -check
       terraform init -backend=false
       terraform validate

If Docker Desktop was just installed, launch it once from Applications
before running any 'docker' command.
EOF
