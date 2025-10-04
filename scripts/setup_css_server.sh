#!/usr/bin/env bash
# shellcheck disable=SC2086
set -euo pipefail

# This script bootstraps the Clash SL Server runtime dependencies on
# Debian/Ubuntu-based distributions. It installs the required packages,
# prepares MySQL and Redis, and imports the bundled schema.

if [[ ${CSS_DEBUG:-0} -eq 1 ]]; then
  set -x
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
SQL_DUMP="${REPO_ROOT}/Clash SL Server/Tools/CSSdb.sql"

if [[ ! -f "${SQL_DUMP}" ]]; then
  echo "[!] Unable to locate CSSdb.sql at ${SQL_DUMP}" >&2
  exit 1
fi

CSS_DB_NAME="${CSS_DB_NAME:-cssdb}"
CSS_DB_USER="${CSS_DB_USER:-CSS}"
CSS_DB_PASSWORD="${CSS_DB_PASSWORD:-ClashOfSL!2024}"
MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:-}"

if command -v apt-get >/dev/null 2>&1; then
  PACKAGE_MANAGER="apt-get"
else
  echo "[!] This script currently supports Debian/Ubuntu via apt-get." >&2
  echo "    Please install Mono, MySQL, Redis, msbuild, nuget and screen manually on your distribution." >&2
  exit 1
fi

if [[ $EUID -ne 0 ]]; then
  if command -v sudo >/dev/null 2>&1; then
    SUDO="sudo"
  else
    echo "[!] Please run as root or install sudo." >&2
    exit 1
  fi
else
  SUDO=""
fi

APT_PACKAGES=(mono-complete msbuild nuget mysql-server redis-server screen unzip)

echo "[*] Updating package lists..."
${SUDO} ${PACKAGE_MANAGER} update -y

echo "[*] Installing required packages: ${APT_PACKAGES[*]}"
${SUDO} ${PACKAGE_MANAGER} install -y "${APT_PACKAGES[@]}"

ensure_service() {
  local service="$1"
  if command -v systemctl >/dev/null 2>&1; then
    echo "[*] Enabling and starting ${service}"
    ${SUDO} systemctl enable --now "${service}"
  else
    echo "[!] systemctl not available; ensure ${service} is running manually." >&2
  fi
}

ensure_service mysql || ensure_service mysqld || true
ensure_service redis-server || ensure_service redis || true

mysql_args=(-u root -B -N)
if [[ -n "${MYSQL_ROOT_PASSWORD}" ]]; then
  mysql_args+=(-p"${MYSQL_ROOT_PASSWORD}")
fi

mysql_query() {
  local query="$1"
  ${SUDO} mysql "${mysql_args[@]}" -e "${query}"
}

mysql_exec_file() {
  local file="$1"
  if [[ ! -f "${file}" ]]; then
    echo "[!] Missing SQL file: ${file}" >&2
    return 1
  fi
  if [[ -n "${MYSQL_ROOT_PASSWORD}" ]]; then
    ${SUDO} mysql -u root -p"${MYSQL_ROOT_PASSWORD}" "${CSS_DB_NAME}" < "${file}"
  else
    ${SUDO} mysql -u root "${CSS_DB_NAME}" < "${file}"
  fi
}

read -r -d '' MYSQL_BOOTSTRAP <<SQL || true
CREATE DATABASE IF NOT EXISTS \`${CSS_DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${CSS_DB_USER}'@'localhost' IDENTIFIED BY '${CSS_DB_PASSWORD}';
CREATE USER IF NOT EXISTS '${CSS_DB_USER}'@'%' IDENTIFIED BY '${CSS_DB_PASSWORD}';
GRANT ALL PRIVILEGES ON \`${CSS_DB_NAME}\`.* TO '${CSS_DB_USER}'@'localhost';
GRANT ALL PRIVILEGES ON \`${CSS_DB_NAME}\`.* TO '${CSS_DB_USER}'@'%';
FLUSH PRIVILEGES;
SQL

mysql_query "${MYSQL_BOOTSTRAP}"

TABLE_COUNT="$(mysql_query "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='${CSS_DB_NAME}'" || echo 0)"
if [[ "${TABLE_COUNT}" -eq 0 ]]; then
  echo "[*] Importing CSS database schema..."
  mysql_exec_file "${SQL_DUMP}"
else
  echo "[i] Existing tables detected in ${CSS_DB_NAME}; skipping import."
fi

echo "[*] Redis status:"
if command -v redis-cli >/dev/null 2>&1; then
  ${SUDO} redis-cli ping || true
else
  echo "[!] redis-cli not found; Redis installation may have failed." >&2
fi

echo "[*] Setup complete."
echo "    MySQL database: ${CSS_DB_NAME}"
if [[ -n "${CSS_DB_PASSWORD}" ]]; then
  echo "    Credentials: ${CSS_DB_USER}/${CSS_DB_PASSWORD}"
else
  echo "    Credentials: ${CSS_DB_USER} (no password)"
fi

echo "You can now build the server with msbuild and run it with mono:"
echo "  msbuild 'Clash SL Server/Clash SL Server.csproj'"
echo "  mono 'Clash SL Server/bin/Debug/Clash SL Server.exe'"
