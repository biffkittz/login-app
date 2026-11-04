#!/usr/bin/env bash
#
# buildAndRunSite.sh - build the whole Login App site and run it locally with Kestrel.
#
# Steps:
#   1. Frontend: clean `npm ci`, unit tests, type-check and production build (Vite) into frontend/dist.
#   2. Combine:  copy frontend/dist into backend/wwwroot, where the API serves the SPA from.
#   3. Backend:  unit/integration tests, then `dotnet publish` (Release: analyzer warnings fail the build)
#                into app/artifacts/site. The SPA must be in wwwroot before publishing, because
#                MapStaticAssets fingerprints and pre-compresses the files at publish time.
#   4. Run:      start the published site on http://localhost:<port>.
#
# Usage: ./app/buildAndRunSite.sh [options]
#
#   -p, --port <port>            Port to listen on (default: 8080).
#   -e, --environment <name>     ASPNETCORE_ENVIRONMENT (default: Development, which uses the local PostgreSQL
#                                connection string in backend/appsettings.Development.json).
#       --skip-tests             Skip the frontend and backend test suites.
#       --skip-npm-ci            Reuse the existing node_modules instead of a clean `npm ci`.
#       --no-run                 Build and combine only; don't start the site.
#   -h, --help                   Show this help.
#
# See docs/BUILD_AND_DEPLOY.md for details and troubleshooting.

set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly FRONTEND_DIR="${SCRIPT_DIR}/frontend"
readonly BACKEND_DIR="${SCRIPT_DIR}/backend"
readonly BACKEND_PROJECT="${BACKEND_DIR}/LoginApp.Api.csproj"
readonly SOLUTION="${SCRIPT_DIR}/LoginApp.slnx"
readonly SITE_DIR="${SCRIPT_DIR}/artifacts/site"
readonly REQUIRED_DOTNET_MAJOR=10
readonly REQUIRED_NODE_MAJOR=22

port=8080
environment_name="Development"
run_tests=true
clean_install=true
run_site=true

log() { printf '\n\033[1;33m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33mwarning:\033[0m %s\n' "$*" >&2; }
die() {
  printf '\033[1;31merror:\033[0m %s\n' "$*" >&2
  exit 1
}
usage() { sed -n '3,23p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

trap 'die "Step failed at line ${LINENO}: ${BASH_COMMAND}"' ERR

parse_arguments() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -p | --port)
        [[ $# -ge 2 && "$2" =~ ^[0-9]+$ ]] || die "--port needs a number."
        port="$2"
        shift 2
        ;;
      -e | --environment)
        [[ $# -ge 2 ]] || die "--environment needs a value."
        environment_name="$2"
        shift 2
        ;;
      --skip-tests) run_tests=false; shift ;;
      --skip-npm-ci) clean_install=false; shift ;;
      --no-run) run_site=false; shift ;;
      -h | --help) usage; exit 0 ;;
      *) usage; die "Unknown option: $1" ;;
    esac
  done
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "'$1' is not installed or not on PATH. See docs/DEVELOPMENT.md."
}

check_prerequisites() {
  log "Checking prerequisites"
  require_command dotnet
  require_command node
  require_command npm

  dotnet --list-sdks | grep -q "^${REQUIRED_DOTNET_MAJOR}\." \
    || die ".NET ${REQUIRED_DOTNET_MAJOR} SDK not found (dotnet --list-sdks). See docs/DEVELOPMENT.md."

  local node_major
  node_major="$(node --version | sed -E 's/^v([0-9]+).*/\1/')"
  ((node_major >= REQUIRED_NODE_MAJOR)) \
    || die "Node.js ${REQUIRED_NODE_MAJOR}+ is required (found $(node --version)). See docs/DEVELOPMENT.md."

  printf '  .NET SDK %s, Node.js %s, npm %s\n' "$(dotnet --version)" "$(node --version)" "$(npm --version)"
}

build_frontend() {
  log "Building the Vue frontend"
  cd "${FRONTEND_DIR}"
  if [[ "${clean_install}" == true ]]; then
    npm ci --no-audit --no-fund
  fi
  if [[ "${run_tests}" == true ]]; then
    npm run test
  fi
  # Type-checks with vue-tsc and bundles with Vite into frontend/dist.
  npm run build
}

combine_frontend_and_backend() {
  log "Copying frontend/dist into backend/wwwroot"
  rm -rf "${BACKEND_DIR}/wwwroot"
  mkdir -p "${BACKEND_DIR}/wwwroot"
  cp -R "${FRONTEND_DIR}/dist/." "${BACKEND_DIR}/wwwroot/"
}

build_backend() {
  log "Testing and publishing the ASP.NET Core backend"
  # Run from app/ so the SDK finds the repository's global.json (SDK version and test runner settings).
  cd "${SCRIPT_DIR}"
  if [[ "${run_tests}" == true ]]; then
    dotnet test --solution "${SOLUTION}" --configuration Release
  fi
  rm -rf "${SITE_DIR}"
  dotnet publish "${BACKEND_PROJECT}" --configuration Release --output "${SITE_DIR}"
  printf '  Published the combined site to %s\n' "${SITE_DIR}"
}

warn_if_database_unreachable() {
  [[ "${environment_name}" == "Development" ]] || return 0
  # Bash's /dev/tcp needs no extra tools. The API applies migrations at startup, and without a database it
  # would spend about a minute retrying before giving up, so say so now.
  if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
    warn "Nothing is listening on localhost:5432. Start PostgreSQL first (docs/DEVELOPMENT.md, 'Local database')."
  fi
}

run_published_site() {
  warn_if_database_unreachable
  log "Starting the site at http://localhost:${port} (${environment_name}); press Ctrl+C to stop"
  cd "${SITE_DIR}"
  # Bind to loopback only: the site is for this machine (WSL forwards localhost to Windows browsers).
  # exec hands the terminal to Kestrel so Ctrl+C triggers a graceful shutdown.
  ASPNETCORE_ENVIRONMENT="${environment_name}" \
    ASPNETCORE_URLS="http://localhost:${port}" \
    exec dotnet LoginApp.Api.dll
}

main() {
  parse_arguments "$@"
  check_prerequisites
  build_frontend
  combine_frontend_and_backend
  build_backend
  if [[ "${run_site}" == true ]]; then
    run_published_site
  else
    log "Done. Start the site later with: cd ${SITE_DIR} && ASPNETCORE_ENVIRONMENT=${environment_name} dotnet LoginApp.Api.dll"
  fi
}

main "$@"
