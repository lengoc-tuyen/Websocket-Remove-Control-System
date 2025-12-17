let connection = null;
let isConnected = false;

// [THÊM] Các biến hỗ trợ Webcam Proof Video (tương thích với logic cũ)
let proofFrames = []; 
let playbackInterval = null;

// [FIX] Lấy IP từ input trong Header (đã được đồng bộ từ Wreath)
const serverIpInput = document.getElementById("serverIpInput");

function buildConnectionUrl(input) {
    let url = input.trim();

    if (!url.startsWith("http")) {
        url = "http://" + url;
    }

    const colonCount = (url.match(/:/g) || []).length;
    if (colonCount < 2) {
        url += ":5001";
    }

    if (!url.endsWith("/controlHub")) {
        if (url.endsWith("/")) url = url.slice(0, -1);
        url += "/controlHub";
    }

    return url;
}

async function connect() {
    const rawIp = serverIpInput.value;
    const finalUrl = buildConnectionUrl(rawIp);

    setStatus("Đang kết nối tới: " + finalUrl);
    
    // [FIX LỖI FLOW] Hiển thị LOADING_MESSAGE ngay lập tức khi bắt đầu kết nối
    handleServerStatus("LOADING_WAIT");

    connection = new signalR.HubConnectionBuilder()
        .withUrl(finalUrl)
        .withAutomaticReconnect()
        .build();
    
    // [THÊM] Listener nhận trạng thái Auth (SETUP_REQUIRED, LOGIN_REQUIRED, v.v.)
    connection.on("ReceiveServerStatus", (status) => {
        handleServerStatus(status);
    });

    connection.on("ReceiveProcessList", (json) => {
        try {
            const list = JSON.parse(json);
            if (document.getElementById("appsSection").style.display !== "none") {
                renderTable("appsTableBody", list, "selectedApp");
            } else {
                renderTable("processesTableBody", list, "selectedProcess");
            }
            setStatus(`Đã tải ${list.length} mục.`);
        } catch (e) {
            console.error("Lỗi parse JSON:", e);
            setStatus("Lỗi dữ liệu từ Server.");
        }
    });

    connection.on("ReceiveStatus", (type, success, message) => {
        setStatus(`[${type}] ${message}`);
        
        // [SỬA LỚN] BỎ qua logic cũ, chỉ gửi thông báo vào chatbox nếu có
        if (window.ui && window.ui.addChatMessage && type !== "SERVER_STATUS") {
            ui.addChatMessage(`[Server] ${message}`, success ? 'bot' : 'error');
        }
    });

    connection.on("ReceiveImage", (type, base64Data)    => {
        const src = "data:image/jpeg;base64," + base64Data;
        const cam = document.getElementById("webcamPreview");
        
        if (type === "SCREENSHOT") {
            document.getElementById("screenPreview").src = src;
            setStatus("Đã nhận ảnh màn hình.");
        } 
        // [FIX] Cập nhật logic Webcam để xử lý LIVE_AND_PROOF (tương thích với Hub mới)
        else if (type === "WEBCAM_FRAME") {
             if (cam) {
                cam.src = src;
                cam.style.display = 'block';
                cam.style.border = "2px solid red"; 
            }
            if (type === "LIVE_AND_PROOF") {
                proofFrames.push(src);
            }
        }
    });


    connection.on("ReceiveKeyLog", (key) => {
        const area = document.getElementById("keylogArea");
        area.value += key;
        area.scrollTop = area.scrollHeight;
    });

    // [XÓA LISTENER CHAT AI CŨ] Không cần listener này nữa vì AI chạy Client-side
    // connection.on("ReceiveChatMessage", (message) => { ... });

    try {
        await connection.start();
        isConnected = true;
        updateConnectionUI(true);
        setStatus("Kết nối thành công!");
        
        // [SỬA LỚN] Gọi GetServerStatus để Server đẩy trạng thái AUTH về
        await connection.invoke("GetServerStatus");
        
    } catch (err) {
        console.error(err);
        setStatus("Kết nối thất bại: " + err.toString());
        alert("Không thể kết nối tới Server. Hãy kiểm tra IP và chắc chắn Server đang chạy.");
        
        // [SPA LOGIC] Nếu lỗi kết nối, quay lại màn hình Auth/Login
        document.getElementById("auth-screen").style.display = 'flex';
        document.getElementById("main-screen").style.display = 'none';
        handleServerStatus("LOGIN_REQUIRED"); 
    }

    connection.onclose(() => {
        isConnected = false;
        updateConnectionUI(false);
        setStatus("Mất kết nối với Server.");
        
        // [SPA LOGIC] Khi mất kết nối, chuyển về màn hình Auth
        document.getElementById("auth-screen").style.display = 'flex';
        document.getElementById("main-screen").style.display = 'none';
        
        // [FIX] Reset các form
        handleServerStatus("LOGIN_REQUIRED");
    });
}

// [SỬA LỚN] LOGIC XỬ LÝ AUTH FLOW (SPA SWITCHING)
function handleServerStatus(status) {
    const authScreen = document.getElementById('auth-screen');
    const mainScreen = document.getElementById('main-screen');
    const loadingMsg = document.getElementById('loading-message');
    
    // Đảm bảo lấy được SETUP_FORM
    const forms = {
        SETUP: document.getElementById('setup-form'),
        REGISTER: document.getElementById('register-form'),
        LOGIN: document.getElementById('login-form')
    };

    // Ẩn tất cả form con và loading
    loadingMsg.classList.add('hidden');
    // Kiểm tra null trước khi add class (phòng trường hợp form bị ẩn)
    if(forms.SETUP) forms.SETUP.classList.add('hidden');
    if(forms.REGISTER) forms.REGISTER.classList.add('hidden');
    if(forms.LOGIN) forms.LOGIN.classList.add('hidden');


    if (status === "AUTHENTICATED") {
        // 1. Đăng nhập thành công -> Chuyển sang Dashboard
        authScreen.style.display = 'none';
        mainScreen.style.display = 'flex'; 
    } else if (status === "LOADING_WAIT") {
        // [THÊM LOGIC] Trạng thái chờ kết nối (sau khi bấm Connect)
        authScreen.style.display = 'flex';
        mainScreen.style.display = 'none';
        loadingMsg.classList.remove('hidden'); 
    }
     else {
        // 2. Cần xác thực -> Hiện màn hình Auth và form tương ứng
        authScreen.style.display = 'flex';
        mainScreen.style.display = 'none';
        
        if (status === "REGISTRATION_REQUIRED" && forms.REGISTER) {
            forms.REGISTER.classList.remove('hidden');
        } else if (status === "LOGIN_REQUIRED" && forms.LOGIN) {
            // [FIX] Hiển thị login form (Mặc định)
            forms.LOGIN.classList.remove('hidden'); 
        } else {
             // Mặc định, hiện login (vì không có SETUP_REQUIRED cứng)
             if(forms.LOGIN) forms.LOGIN.classList.remove('hidden');
        }
    }
}

// Hàm phát lại video bằng chứng (Chạy khi tắt cam)
function playReplay() {
    if (proofFrames.length === 0) return;
    
    const cam = document.getElementById("webcamPreview");
    if (!cam) return;

    cam.style.border = "2px solid #22c55e"; // Viền xanh = Đang xem lại
    setStatus(`▶️ Đang phát lại 3s đầu (${proofFrames.length} frames)`);

    let index = 0;
    if (playbackInterval) clearInterval(playbackInterval);

    playbackInterval = setInterval(() => {
        if (index >= proofFrames.length) index = 0; 
        cam.src = proofFrames[index];
        index++;
    }, 66); 
}


// [THÊM] HÀM GỌI AUTH HUB TỪ CLIENT (Cần thiết cho HTML)
async function submitSetupCode() {
    if (!checkConn()) return;
    const code = document.getElementById('master-code').value;
    if(code) {
        setStatus("Đang gửi Master Code...");
        await connection.invoke("SubmitSetupCode", code);
    }
}

async function registerUser() {
    console.log("Attempting registration..."); // [THÊM DEBUG LOG]
    if (!checkConn()) {
        console.error("Registration attempt failed: Not connected (isConnected = false)."); // [THÊM DEBUG LOG]
        return;
    }
    const u = document.getElementById('reg-username').value;
    const p = document.getElementById('reg-password').value;
    if(u && p) {
        setStatus("Đang đăng ký tài khoản...");
        console.log("Invoking RegisterUser Hub method..."); // [THÊM DEBUG LOG]
        await connection.invoke("RegisterUser", u, p);
    } else {
        alert("Vui lòng nhập đầy đủ Tên đăng nhập và Mật khẩu!");
    }
}

async function loginUser() {
    console.log("Attempting login...");
    if (!checkConn()) {
        console.error("Login attempt failed: Not connected (isConnected = false).");
        return;
    }
    const u = document.getElementById('login-username').value;
    const p = document.getElementById('login-password').value;
    if(u && p) {
        setStatus("Đang xác thực đăng nhập...");
        console.log("Invoking Login Hub method...");
        await connection.invoke("Login", u, p);
    } else {
        alert("Vui lòng nhập đầy đủ Tên đăng nhập và Mật khẩu!");
    }
}

// [BỎ HÀM KHÔNG CẦN THIẾT] Đã xóa showMainScreen() và hideAllAuthForms() vì đã có handleServerStatus()


async function disconnect() {
    if (connection) await connection.stop();
    isConnected = false;
    updateConnectionUI(false);
    setStatus("Đã ngắt kết nối.");
}

function checkConn() {
    if (!isConnected) {
        alert("Vui lòng kết nối tới Server trước!");
        return false;
    }
    return true;
}

function wireActionButtons() {
    // [SỬA LỚN] Gắn sự kiện cho các nút Auth và gán vào window
    const btnSetup = document.getElementById("btn-submit-setup");
    if(btnSetup) btnSetup.addEventListener("click", submitSetupCode);

    const btnReg = document.getElementById("btn-submit-register");
    // [FIX LỖI CHẮC CHẮN] Nút Hoàn tất Đăng kí đã được gắn sự kiện bằng cả addEventListener và onclick trong HTML.
    if(btnReg) btnReg.addEventListener("click", registerUser);

    const btnLogin = document.getElementById("btn-submit-login");
    if(btnLogin) btnLogin.addEventListener("click", loginUser);
    
    // [GẮN VÀO WINDOW] để HTML gọi trực tiếp (onkeydown)
    window.submitSetupCode = submitSetupCode;
    window.registerUser = registerUser;
    window.loginUser = loginUser;

    document.getElementById("refreshAppsBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Đang tải danh sách App...");
            connection.invoke("GetProcessList", true);
        }
    });

    document.getElementById("refreshProcessesBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Đang tải danh sách Process...");
            connection.invoke("GetProcessList", false);
        }
    });

    document.getElementById("startAppBtn").addEventListener("click", () => {
        if (checkConn()) {
            const name = document.getElementById("appNameInput").value;
            if (name) connection.invoke("StartProcess", name);
            else alert("Vui lòng nhập tên ứng dụng!");
        }
    });

    document.getElementById("startProcessBtn").addEventListener("click", () => {
        if (checkConn()) {
            const name = document.getElementById("processNameInput").value;
            if (name) connection.invoke("StartProcess", name);
            else alert("Vui lòng nhập tên hoặc đường dẫn Process!");
        }
    });

    document.getElementById("stopSelectedAppBtn").addEventListener("click", () => {
        if (checkConn()) {
            const id = getSelectedId("selectedApp");
            if (id) connection.invoke("KillProcess", id);
            else alert("Vui lòng chọn một App trong danh sách!");
        }
    });

    document.getElementById("stopSelectedProcessBtn").addEventListener("click", () => {
        if (checkConn()) {
            const id = getSelectedId("selectedProcess");
            if (id) connection.invoke("KillProcess", id);
            else alert("Vui lòng chọn một Process trong danh sách!");
        }
    });

    document.getElementById("captureScreenBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Đang yêu cầu chụp màn hình...");
            connection.invoke("GetScreenshot");
        }
    });
    
    document.getElementById("webcamOnBtn").addEventListener("click", () => {
        if (checkConn()) {
            if (playbackInterval) clearInterval(playbackInterval);

            setStatus("Đang yêu cầu Webcam...");
            connection.invoke("RequestWebcam"); 
        }
    });

    // Logic Webcam OFF (Thêm logic Phát lại Proof Frames)
    document.getElementById("webcamOffBtn").addEventListener("click", () => {
        if (checkConn()) {
             connection.invoke("CloseWebcam"); 
            
            // Nếu đã lưu được bằng chứng (proofFrames) thì phát lại
            if (proofFrames.length > 0) {
                playReplay();
            } else {
                // Tắt hẳn nếu không có gì để phát
                const cam = document.getElementById("webcamPreview");
                if(cam) cam.src = "";
            }
        }
    });

    document.getElementById("startKeylogBtn").addEventListener("click", () => {
        if (checkConn()) connection.invoke("StartKeyLogger");
    });

    document.getElementById("stopKeylogBtn").addEventListener("click", () => {
        if (checkConn()) connection.invoke("StopKeyLogger");
    });

    document.getElementById("clearKeylogBtn").addEventListener("click", () => {
        document.getElementById("keylogArea").value = "";
    });

    document.getElementById("restartBtn").addEventListener("click", () => {
        if (checkConn() && confirm("CẢNH BÁO: Bạn có chắc muốn RESTART máy Server ngay lập tức?")) {
            connection.invoke("ShutdownServer", true);
        }
    });

    document.getElementById("shutdownBtn").addEventListener("click", () => {
        if (checkConn() && confirm("CẢNH BÁO: Bạn có chắc muốn TẮT MÁY Server ngay lập tức?")) {
            connection.invoke("ShutdownServer", false);
        }
    });

    const sendChatBtn = document.getElementById("sendChatBtn");
    if (sendChatBtn) {
        sendChatBtn.addEventListener("click", () => {
            const input = document.getElementById("chat-input");
            const text = input.value.trim();
            if (!text) return;

            // 1. Hiện tin nhắn user
            if(window.ui && window.ui.addChatMessage) {
                ui.addChatMessage(text, 'user');
            }
            input.value = "";

            // 2. Check xem dịch vụ AI độc lập đã tải chưa (ai_service.js)
            if (!window.chatWithGemini) {
                // [FIX LỖI AI] Sử dụng logic báo lỗi AI chưa tải
                if(window.ui) ui.addChatMessage("⚠️ Dịch vụ Snowie AI chưa được tải (Vui lòng kiểm tra file ai_service.js)!", 'bot');
                return;
            }

            // 3. Gửi lên AI Service (Client-side)
            if(window.ui) ui.showTyping(true);
            
            window.chatWithGemini(text).then(botResponse => {
                if(window.ui) {
                    ui.showTyping(false);
                    ui.addChatMessage(botResponse, 'bot');
                }
            }).catch(err => {
                if(window.ui) {
                    ui.showTyping(false);
                    ui.addChatMessage("Lỗi AI: " + err.toString(), 'bot');
                }
            });
        });
    }
}

// Gắn sự kiện cho nút Connect chính
if(toggleConnectBtn) {
    toggleConnectBtn.addEventListener("click", () => {
        if (!isConnected) connect();
        else disconnect();
    });
}

// Khởi tạo các sự kiện khi trang web load xong
document.addEventListener("DOMContentLoaded", wireActionButtons);