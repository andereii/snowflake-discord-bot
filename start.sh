#!/bin/bash
echo "Esperando a que terminen de compilar las dependencias (canvas, opus, sqlite3)..."
cd bot
npm run deploy:commands
npm run start &
cd ../web-backend
npm run start &
cd ../web-frontend
npm run dev &
wait
