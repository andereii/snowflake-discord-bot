#!/usr/bin/env bash
# Script para limpiar el proyecto antes de copiarlo a un disco externo o hacer un backup.
# Elimina node_modules y archivos temporales, reduciendo el proyecto al mínimo tamaño.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Limpieza para Copia / Backup ==="
echo "Calculando tamaño actual..."
PREV_SIZE=$(du -sh "$DIR" | cut -f1)
echo "Tamaño inicial: $PREV_SIZE"
echo ""

echo "Eliminando carpetas de dependencias (node_modules)..."
rm -rf "$DIR/bot/node_modules"
rm -rf "$DIR/web-backend/node_modules"
rm -rf "$DIR/web-frontend/node_modules"
rm -rf "$DIR/.kilo"

echo "Eliminando cachés y archivos temporales..."
rm -rf "$DIR/web-frontend/dist"
rm -rf "$DIR/.npm"
find "$DIR" -maxdepth 3 -type f -name "*.log" -delete 2>/dev/null || true
find "$DIR" -maxdepth 3 -type f -name "*.db-shm" -delete 2>/dev/null || true
find "$DIR" -maxdepth 3 -type f -name "*.db-wal" -delete 2>/dev/null || true

echo ""
NEW_SIZE=$(du -sh "$DIR" | cut -f1)
echo "✅ ¡Listo! Proyecto optimizado."
echo "Tamaño anterior: $PREV_SIZE"
echo "Tamaño final:    $NEW_SIZE"
echo ""
echo "💡 Cuando vuelvas a usar el proyecto en tu destino, ejecuta:"
echo "   ./install-deps.sh"
echo "o entra a bot/, web-backend/ y web-frontend/ y corre 'npm install'."
