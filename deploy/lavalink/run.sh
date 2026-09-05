#!/usr/bin/env bash
# Arranca el servidor Lavalink. Requiere Java 17+ instalado.
# Uso: ./deploy/lavalink/run.sh
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$DIR/../.." && pwd)"

# Lavalink necesita las credenciales de Spotify en su propio proceso. Carga el
# .env del proyecto sin imprimirlo ni incluirlo en el repositorio.
if [ -f "$ROOT_DIR/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$ROOT_DIR/.env"
  set +a
fi

if [ ! -f "$DIR/Lavalink.jar" ]; then
  echo "Lavalink.jar no encontrado. Descargando v4.2.2..."
  curl -sSL -o "$DIR/Lavalink.jar" "https://github.com/lavalink-devs/Lavalink/releases/download/4.2.2/Lavalink.jar"
fi

cd "$DIR"
exec java -jar Lavalink.jar