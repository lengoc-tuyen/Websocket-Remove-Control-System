// --- Cấu hình API Gemini ---
// Import API key từ config.js (file này không push lên GitHub)
import { API_CONFIG } from './config.js';

const MODEL_NAME = "gemini-2.5-flash-preview-09-2025";
const API_URL = `https://generativelanguage.googleapis.com/v1beta/models/${MODEL_NAME}:generateContent`;

/**
 * Gọi API Gemini để lấy phản hồi văn bản dựa trên truy vấn người dùng.
 * @param {string} userQuery - Câu hỏi của người dùng.
 * @returns {Promise<string>} - Phản hồi đã được tạo ra từ AI.
 */
export async function chatWithGemini(userQuery) {
    console.log("🤖 chatWithGemini called with:", userQuery);
    
    if (!userQuery) return "Vui lòng nhập câu hỏi.";
    
    // Lấy API key (ưu tiên window.API_CONFIG nếu có)
    const apiKey = window.API_CONFIG?.GeminiApiKey || API_CONFIG?.GeminiApiKey;
    
    console.log("🔑 Checking API key...");
    console.log("  window.API_CONFIG:", window.API_CONFIG);
    console.log("  API_CONFIG:", API_CONFIG);
    console.log("  apiKey:", apiKey ? apiKey.substring(0, 10) + "..." : "undefined");
    
    // Kiểm tra API key
    if (!apiKey || apiKey === "YOUR_GEMINI_API_KEY_HERE") {
        console.error("❌ API key chưa được cấu hình!");
        return "⚠️ Lỗi: API key chưa được cấu hình.\n\n" +
               "Vui lòng kiểm tra file Chatbot/config.js\n" +
               "và đảm bảo GeminiApiKey được set đúng.\n\n" +
               "Lấy API key miễn phí tại: https://aistudio.google.com/app/apikey";
    }
    
    console.log("✅ API key loaded successfully");

    // [SYSTEM PROMPT] Định nghĩa tính cách Snowie và kiến thức về Dashboard
    const systemPrompt = `
Chào mừng bạn! Tôi là Snowie, Trợ Lý AI Điều Khiển Đa Nền Tảng của dự án này! ⛄
Tôi là một chatbot thân thiện, hữu ích, và tôi được tích hợp trực tiếp vào giao diện Client này (tôi là con AI mà bạn thấy đó).

Nhiệm vụ của tôi là khoe (boast) về các tính năng mạnh mẽ và hướng dẫn chi tiết về cách bạn có thể điều khiển máy Server từ xa một cách hiệu quả và thông minh nhất.

**Đặc điểm nổi bật của ứng dụng & Khả năng Đa Nền Tảng:**
1.  **Kiến trúc Đa Nền Tảng:** Ứng dụng này là một Dashboard Điều khiển từ xa (Remote Control Dashboard) được xây dựng trên SignalR (C# Server) và JavaScript Client. **Chúng tôi tự hào về khả năng hoạt động mượt mà trên cả Windows và macOS!**
2.  **Webcam & Video Bằng chứng (Proof Video):**
    * Hỗ trợ Live Stream video liên tục. Khi người dùng bấm Tắt Live Stream, hệ thống tự động lưu và phát lại **3 giây đầu tiên** của phiên Live làm bằng chứng.
3.  **Keylogger:** Theo dõi và ghi lại các phím gõ trên máy Server (Đã tối ưu hóa ASCII/Unicode trên Windows). Chức năng này yêu cầu cấp quyền hệ thống (Accessibility) trên macOS.
4.  **Chụp Màn hình:** Chụp ảnh toàn bộ không gian màn hình hiện tại của Server, bao gồm **đa màn hình (Virtual Screen)** trên Windows.
5.  **Quản lý Tiến trình (Apps/Processes):** Khởi động và Dừng (Kill) các tiến trình trên máy Server.
6.  **Điều khiển nguồn:** Khởi động lại (Restart) hoặc Tắt máy (Shutdown) Server.

Luôn giữ giọng điệu vui vẻ, thân thiện, và tự tin (boastful) khi giải thích về các công nghệ này. Luôn trả lời bằng Tiếng Việt. Trả lời đúng trọng tâm, không quá dài.
`;

    const payload = {
        contents: [{ parts: [{ text: userQuery }] }],
        // Kích hoạt Google Search để trả lời các câu hỏi về thông tin mới (Grounding)
        tools: [{ "google_search": {} }], 
        systemInstruction: {
            parts: [{ text: systemPrompt }]
        },
    };

    let resultText = "Lỗi hệ thống hoặc quá tải API.";
    let retries = 0;
    const maxRetries = 3;
    let delay = 1000; // 1 giây

    while (retries < maxRetries) {
        try {
            const response = await fetch(API_URL + `?key=${apiKey}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                if (response.status === 429) { // Quá tải (Too Many Requests)
                    throw new Error("429");
                }
                throw new Error(`API returned status ${response.status}`);
            }

            const result = await response.json();
            const candidate = result.candidates?.[0];

            if (candidate && candidate.content?.parts?.[0]?.text) {
                // Trích xuất văn bản thành công
                resultText = candidate.content.parts[0].text;
                return resultText;
            } else {
                resultText = "Không nhận được phản hồi hợp lệ từ AI.";
                break; // Thoát vòng lặp nếu phản hồi không hợp lệ
            }

        } catch (error) {
            console.error(`Fetch error (Retry ${retries + 1}):`, error.message);
            if (error.message === "429" && retries < maxRetries - 1) {
                await new Promise(resolve => setTimeout(resolve, delay));
                delay *= 2; // Tăng thời gian chờ (Exponential Backoff)
                retries++;
            } else {
                resultText = "Lỗi kết nối hoặc API không phản hồi.";
                break;
            }
        }
    }

    return resultText;
}

// [QUAN TRỌNG] Đưa hàm ra ngoài global để connection.js có thể gọi
window.chatWithGemini = chatWithGemini;
