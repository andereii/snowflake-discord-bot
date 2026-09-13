#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

install_if_needed() {
  local dir="$1"
  if [[ ! -d "$dir/node_modules" ]]; then
    echo "Installing dependencies in $(basename "$dir")..."
    (cd "$dir" && npm install)
  fi
}

pids_on_port() {
  ss -H -tlnp "sport = :$1" 2>/dev/null \
    | grep -oP 'pid=\K[0-9]+' \
    | sort -u
}

stop_pid() {
  local pid="$1"
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    sleep 0.15
    kill -9 "$pid" 2>/dev/null || true
  fi
}

stop_previous() {
  echo "Stopping previous Snowflake processes..."
  pkill -f "$ROOT/web-backend/src/server.js" 2>/dev/null || true
  pkill -f "$ROOT/web-frontend/node_modules/.bin/vite" 2>/dev/null || true
  pkill -f "$ROOT/bot/src/index.js" 2>/dev/null || true
  pkill -f "$ROOT/bot/src/deploy-commands.js" 2>/dev/null || true

  local pid
  for pid in $(pids_on_port 3000) $(pids_on_port 5173); do
    stop_pid "$pid"
  done
  sleep 0.3
}

wait_for() {
  local url="$1"
  local name="$2"
  for _ in $(seq 1 40); do
    if curl -sf "$url" >/dev/null 2>&1; then
      echo "$name is up."
      return 0
    fi
    sleep 0.25
  done
  echo "$name did not start ($url). Check the error above."
  return 1
}

export SNOWFLAKE_ENV="${SNOWFLAKE_ENV:-local}"
export NODE_ENV="${NODE_ENV:-development}"

stop_previous

install_if_needed "$ROOT/bot"
install_if_needed "$ROOT/web-backend"
install_if_needed "$ROOT/web-frontend"

cd "$ROOT/bot"
npm run deploy:commands
npm run start &

cd "$ROOT/web-backend"
npm run start &

cd "$ROOT/web-frontend"
npm run dev &

wait_for "http://127.0.0.1:3000/api/health" "web-backend"
wait_for "http://127.0.0.1:5173" "web-frontend"

echo "Dashboard: http://localhost:5173/"
wait
