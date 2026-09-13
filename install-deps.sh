#!/usr/bin/env bash
# Script para reinstalar todas las dependencias del bot, backend y frontend.
# Uso: ./install-deps.sh

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Instalando dependencias de Snowflake Bot v2 ==="

echo ""
echo "[1/3] Instalando dependencias del Bot..."
(cd "$DIR/bot" && npm install)

echo ""
echo "[2/3] Instalando dependencias del Web Backend..."
(cd "$DIR/web-backend" && npm install)

echo ""
echo "[3/3] Instalando dependencias del Web Frontend..."
(cd "$DIR/web-frontend" && npm install)

echo ""
echo "✅ ¡Todas las dependencias se instalaron correctamente!"
echo "Para iniciar los servicios puedes ejecutar: ./start.sh"
