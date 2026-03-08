#!/bin/bash
# Lance EliteBridge — backend + frontend
cd "$(dirname "$0")"

echo "▶ Libération des ports..."
lsof -ti:4200 | xargs kill -9 2>/dev/null || true
lsof -ti:7293 | xargs kill -9 2>/dev/null || true
sleep 2

echo "▶ Backend .NET..."
(cd EliteBridgePlanner.Server && dotnet run) &
sleep 6

echo "▶ Frontend Angular (HTTP)..."
(cd elitebridgeplanner.client && npm run start:http) &

echo ""
echo "════════════════════════════════════════════"
echo "  Backend  : https://localhost:7293"
echo "  Frontend : http://localhost:4200"
echo ""
echo "  Ouvrez http://localhost:4200 dans le navigateur"
echo "  Ctrl+C pour arrêter"
echo "════════════════════════════════════════════"
wait
