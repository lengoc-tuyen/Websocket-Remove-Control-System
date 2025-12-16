# HƯỚNG DẪN CHẠY ĐỒ ÁN

## Cách 1: Dùng Script Tự Động (Khuyến nghị) 🚀

```bash
cd Client/WebSystem
./start-demo.sh
```

Script sẽ tự động:
1. ✅ Chạy C# Server (SignalR) ở background
2. ✅ Chạy HTTP Server để serve Client
3. ✅ Mở browser tại http://localhost:8000/begin.html
4. ✅ Dừng cả 2 server khi nhấn Ctrl+C

---

## Cách 2: Chạy Thủ Công (2 Terminal)

### Terminal 1 - C# Server:
```bash
cd Server
dotnet run
```

### Terminal 2 - HTTP Server:
```bash
cd Client/WebSystem
python3 -m http.server 8000
```

Sau đó mở browser: **http://localhost:8000/begin.html**

---

## Lưu Ý Quan Trọng ⚠️

1. **C# Server** chạy trên **port 5000** (hoặc port được config trong appsettings.json)
2. **HTTP Server** chạy trên **port 8000**
3. **Chatbot AI** cần kết nối Internet (gọi Google Gemini API)
4. Trên macOS, một số tính năng cần cấp quyền:
   - Keylogger → Accessibility permission
   - Screen capture → Screen Recording permission

---

## Tính Năng Chính

✨ **Remote Control Dashboard** với:
- 📹 Webcam Live Stream + Proof Video (3s đầu)
- ⌨️ Keylogger (Windows/macOS)
- 📸 Screenshot (Multi-screen support)
- 🔧 Process Manager (Start/Kill apps)
- 🔌 Power Control (Restart/Shutdown)
- 🤖 AI Chatbot (Snowie - powered by Google Gemini)

---

## Yêu Cầu Hệ Thống

- **Server:** .NET 7.0+ SDK
- **Client:** Python 3.x (để chạy HTTP server)
- **Browser:** Chrome, Firefox, Safari (latest)
- **Internet:** Required for AI chatbot
