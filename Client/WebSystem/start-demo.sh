#!/bin/bash
# Script tự động chạy C# Server + HTTP Server + mở browser

echo "🚀 Starting Remote Control System Demo..."
echo ""
echo "📌 Step 1: Building C# Server..."

# Build C# Server first
cd ../../Server
dotnet build

# Check if build succeeded
if [ $? -ne 0 ]; then
    echo "❌ Build failed! Please fix errors and try again."
    exit 1
fi

echo "✅ Build successful!"
echo ""
echo "📌 Step 2: Starting C# Server (SignalR)..."

# Start C# Server in background
dotnet run &
SERVER_PID=$!

echo "✅ C# Server started (PID: $SERVER_PID)"
echo ""
echo "⏳ Waiting 5 seconds for server to initialize..."
sleep 5

echo ""
echo "📌 Step 3: Starting HTTP Server for Client..."
cd ../Client/WebSystem

# Mở browser
open http://localhost:8000/begin.html

echo "🌐 Client URL: http://localhost:8000"
echo "📂 Serving from: $(pwd)"
echo ""
echo "⚠️  Press Ctrl+C to stop BOTH servers"
echo ""

# Start HTTP server (this will block)
python3 -m http.server 8000

# Cleanup: Kill C# server when HTTP server stops
kill $SERVER_PID 2>/dev/null
echo ""
echo "✅ Both servers stopped"
