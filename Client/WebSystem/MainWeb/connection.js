let connection = null;
let isConnected = false;
let wasAuthenticated = false;
let manualDisconnect = false;
let authCheckTimer = null;
let preferredUnauthForm = "LOGIN"; // LOGIN | SETUP
let isRegistering = false;

let webcamProofs = [];
let isWebcamLive = false;

// [FIX] Lấy IP từ input trong Header (đã được đồng bộ từ Wreath)
const serverIpInput = document.getElementById("serverIpInput");

function buildConnectionUrl(input) {
    let raw = (input || "").trim();
    if (!raw) return "";

    if (!raw.startsWith("http://") && !raw.startsWith("https://")) {
        raw = "http://" + raw;
    }

    let u;
    try {
        u = new URL(raw);
    } catch {
        return raw;
    }

    // On macOS, using "localhost" on port 5000 can be flaky (system service sharing);
    // prefer explicit loopback for same-machine connects.
    if (u.hostname === "localhost") u.hostname = "127.0.0.1";

    const pathname = u.pathname || "/";
    if (!pathname.endsWith("/controlHub")) {
        u.pathname = (pathname.endsWith("/") ? pathname.slice(0, -1) : pathname) + "/controlHub";
    }

    // Keep any query/fragment user entered.
    return u.toString().replace(/\/$/, "");
}

function buildConnectionUrlCandidates(input) {
    let raw = (input || "").trim();
    if (!raw) return [];

    if (!raw.startsWith("http://") && !raw.startsWith("https://")) {
        raw = "http://" + raw;
    }

    let u;
    try {
        u = new URL(raw);
    } catch {
        return [buildConnectionUrl(raw)].filter(Boolean);
    }

    if (u.hostname === "localhost") u.hostname = "127.0.0.1";

    const hasExplicitPort = !!u.port;
    const ports = hasExplicitPort
        ? [u.port]
        : ["5000"];

    const candidates = [];
    for (const p of ports) {
        const c = new URL(u.toString());
        c.port = p;
        const pathname = c.pathname || "/";
        if (!pathname.endsWith("/controlHub")) {
            c.pathname = (pathname.endsWith("/") ? pathname.slice(0, -1) : pathname) + "/controlHub";
        }
        candidates.push(c.toString().replace(/\/$/, ""));
    }
    return [...new Set(candidates)];
}

function setServerKeycaptureHintFromHubUrl(hubUrl) {
    const el = document.getElementById("serverKeycaptureUrl");
    if (!el) return;
    try {
        const u = new URL(hubUrl);
        const base = `${u.protocol}//${u.host}`;
        el.textContent = `${base}/server-keycapture`;
    } catch {
        // ignore
    }
}

function showSetupForm() {
    preferredUnauthForm = "SETUP";
    handleServerStatus("LOGIN_REQUIRED");
}

function showLoginForm() {
    preferredUnauthForm = "LOGIN";
    handleServerStatus("LOGIN_REQUIRED");
}

async function connect() {
    const rawIp = serverIpInput.value;
    const candidates = buildConnectionUrlCandidates(rawIp);
    const finalUrl = candidates[0] || buildConnectionUrl(rawIp);

    setStatus("Connecting to: " + finalUrl);
    
    // [FIX LỖI FLOW] Hiển thị LOADING_MESSAGE ngay lập tức khi bắt đầu kết nối
    handleServerStatus("LOADING_WAIT");
    manualDisconnect = false;

    function wireHubHandlers() {
        // [THÊM] Listener nhận trạng thái Auth (SETUP_REQUIRED, LOGIN_REQUIRED, v.v.)
        connection.on("ReceiveServerStatus", (status) => {
            handleServerStatus(status);
        });

        connection.on("ReceiveAuthToken", (token) => {
            if (typeof token === "string" && token.length > 0) {
                localStorage.setItem("authToken", token);
            }
        });

        connection.on("ReceiveProcessList", (json) => {
        try {
            const list = JSON.parse(json);
            if (document.getElementById("appsSection").style.display !== "none") {
                renderTable("appsTableBody", list, "selectedApp");
            } else {
                renderTable("processesTableBody", list, "selectedProcess");
            }
            setStatus(`Loaded ${list.length} items.`);
        } catch (e) {
            console.error("Parse JSON error:", e);
            setStatus("Server data error..");
        }
    });

        function showAuthMessage(type, success, message) {
            const authScreen = document.getElementById("auth-screen");
            const msgEl = document.getElementById("authMessage");
            if (!authScreen || !msgEl) return;
            if (authScreen.style.display !== "flex") return;

            const text = `[${type}] ${message}`;
            msgEl.textContent = text;
            msgEl.classList.remove("hidden", "ok", "bad");
            msgEl.classList.add(success ? "ok" : "bad");

            if (msgEl._hideTimer) clearTimeout(msgEl._hideTimer);
            msgEl._hideTimer = setTimeout(() => {
                msgEl.classList.add("hidden");
            }, 6000);
        }

        connection.on("ReceiveStatus", (type, success, message) => {
        setStatus(`[${type}] ${message}`);

        // Auth screen covers status bar; mirror important messages here.
        if (type === "AUTH" || type === "SERVER_STATUS") {
            showAuthMessage(type, success, message);
        }
        
        // [SỬA LỚN] BỎ qua logic cũ, chỉ gửi thông báo vào chatbox nếu có
        if (window.ui && window.ui.addChatMessage && type !== "SERVER_STATUS") {
            ui.addChatMessage(`[Server] ${message}`, success ? 'bot' : 'error');
        }
    });

        connection.on("ReceiveWebcamProofList", (json) => {
        try {
            webcamProofs = JSON.parse(json) || [];
        } catch {
            webcamProofs = [];
        }
        renderProofSelect();
    });

        connection.on("ReceiveImage", (type, base64Data) => {
        const src = "data:image/jpeg;base64," + base64Data;
        const cam = document.getElementById("webcamPreview");
        
        if (type === "SCREENSHOT") {
            document.getElementById("screenPreview").src = src;
            setStatus("Screen capture received.");
        } else if (type === "WEBCAM_LIVE") {
            if (cam) {
                cam.src = src;
                cam.style.display = 'block';
                cam.style.border = "2px solid red";
            }
        } else if (type === "WEBCAM_PROOF_FRAME") {
            if (cam) {
                cam.src = src;
                cam.style.display = 'block';
                cam.style.border = "2px solid #22c55e";
            }
        } else if (type === "WEBCAM_FRAME") {
            if (cam) {
                cam.src = src;
                cam.style.display = 'block';
                cam.style.border = "2px solid red";
            }
        }
    });


        connection.on("ReceiveKeyLog", (key) => {
        const area = document.getElementById("keylogArea");
        if (!area) return;

        // Update small status based on markers
        if (typeof key === "string") {
            if (key.includes("session enabled")) setKeylogUiState(true);
            if (key.includes("session disabled")) setKeylogUiState(false);
        }

        if ("value" in area) {
            area.value += key;
            area.scrollTop = area.scrollHeight;
        } else {
            area.textContent = (area.textContent || "") + key;
            area.scrollTop = area.scrollHeight;
        }
    });
    }

    // [XÓA LISTENER CHAT AI CŨ] Không cần listener này nữa vì AI chạy Client-side
    // connection.on("ReceiveChatMessage", (message) => { ... });

    let lastErr = null;
    outer: for (const url of (candidates.length ? candidates : [finalUrl]).filter(Boolean)) {
        setServerKeycaptureHintFromHubUrl(url);

        for (let attempt = 1; attempt <= 4; attempt++) {
            setStatus(`Connectiing to: ${url} (test ${attempt}/4)`);

            connection = new signalR.HubConnectionBuilder()
                .withUrl(url, {
                    accessTokenFactory: () => localStorage.getItem("authToken") || ""
                })
                .withAutomaticReconnect()
                .build();

            // Make reconnects less trigger-happy on slow operations
            connection.serverTimeoutInMilliseconds = 120000;
            connection.keepAliveIntervalInMilliseconds = 15000;

            wireHubHandlers();

            try {
                await connection.start();
                isConnected = true;
                updateConnectionUI(true);
                setStatus("Successfully connected!");

                await connection.invoke("GetServerStatus");
                try { await connection.invoke("GetWebcamProofList"); } catch {}

                if (authCheckTimer) clearInterval(authCheckTimer);
                authCheckTimer = setInterval(() => {
                    if (connection && connection.state === "Connected") {
                        connection.invoke("GetServerStatusSilent").catch(() => { });
                    }
                }, 30_000);
                lastErr = null;
                break outer;
            } catch (err) {
                lastErr = err;
                try { await connection.stop(); } catch {}
                connection = null;
                await new Promise(r => setTimeout(r, 350 * attempt));
            }
        }
    }

    if (!isConnected && lastErr) {
        console.error(lastErr);
        const errText = (lastErr && lastErr.toString) ? lastErr.toString() : String(lastErr);
        setStatus("Failed to connect: " + errText);
        // Avoid blocking alert popups; keep errors in status bar/console.

        // [SPA LOGIC] Nếu lỗi kết nối, quay lại màn hình Auth/Login
        document.getElementById("auth-screen").style.display = 'flex';
        document.getElementById("main-screen").style.display = 'none';
        handleServerStatus("LOGIN_REQUIRED");
        return;
    }

    connection.onreconnecting(() => {
        isConnected = false;
        updateConnectionUI(false);
        setStatus("Reconnecting...");
        // Không ép về màn hình login; giữ UI hiện tại.
    });

    connection.onreconnected(async () => {
        isConnected = true;
        updateConnectionUI(true);
        setStatus("Reconnected.");
        try { await connection.invoke("GetServerStatus"); } catch {}
        try { await connection.invoke("GetWebcamProofList"); } catch {}
    });

    connection.onclose(() => {
        isConnected = false;
        updateConnectionUI(false);
        setStatus("Lost connection to the server.");
        if (authCheckTimer) {
            clearInterval(authCheckTimer);
            authCheckTimer = null;
        }

        if (manualDisconnect) return;

        // Nếu đã từng authenticated, đừng ép logout; chỉ hiện auth screen khi thực sự chưa auth.
        if (!wasAuthenticated) {
            document.getElementById("auth-screen").style.display = 'flex';
            document.getElementById("main-screen").style.display = 'none';
            handleServerStatus("LOGIN_REQUIRED");
        }
    });
}

// [SỬA LỚN] LOGIC XỬ LÝ AUTH FLOW (SPA SWITCHING)
function handleServerStatus(status) {
    const authScreen = document.getElementById('auth-screen');
    const mainScreen = document.getElementById('main-screen');
    const loadingMsg = document.getElementById('loading-message');
    const logoutBtn = document.getElementById('logoutBtn');
    
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
        wasAuthenticated = true;
        preferredUnauthForm = "LOGIN";
        if (logoutBtn) logoutBtn.style.display = "inline-flex";
        // 1. Đăng nhập thành công -> Chuyển sang Dashboard
        authScreen.style.display = 'none';
        mainScreen.style.display = 'flex'; 
    } else if (status === "LOADING_WAIT") {
        // [THÊM LOGIC] Trạng thái chờ kết nối (sau khi bấm Connect)
        if (logoutBtn) logoutBtn.style.display = "none";
        authScreen.style.display = 'flex';
        mainScreen.style.display = 'none';
        loadingMsg.classList.remove('hidden'); 
    }
     else {
        wasAuthenticated = false;
        if (logoutBtn) logoutBtn.style.display = "none";
        // 2. Cần xác thực -> Hiện màn hình Auth và form tương ứng
        authScreen.style.display = 'flex';
        mainScreen.style.display = 'none';
        
        if (status === "REGISTRATION_REQUIRED" && forms.REGISTER) {
            forms.REGISTER.classList.remove('hidden');
        } else if (status === "LOGIN_REQUIRED") {
            // Mặc định là login; nhưng nếu user đang muốn nhập master code thì ưu tiên setup.
            if (preferredUnauthForm === "SETUP" && forms.SETUP) forms.SETUP.classList.remove('hidden');
            else if (forms.LOGIN) forms.LOGIN.classList.remove('hidden');
        } else {
            if (preferredUnauthForm === "SETUP" && forms.SETUP) forms.SETUP.classList.remove('hidden');
            else if (forms.LOGIN) forms.LOGIN.classList.remove('hidden');
        }
    }
}

function renderProofSelect() {
    const sel = document.getElementById("proofSelect");
    if (!sel) return;

    const current = sel.value;
    sel.innerHTML = `<option value="">(Choose evidence to play)</option>`;

    (webcamProofs || []).forEach(p => {
        const id = p.id || "";
        if (!id) return;
        const secs = p.durationSeconds != null ? Math.round(p.durationSeconds) : "";
        const frames = p.frameCount != null ? p.frameCount : "";
        const label = `${id}${secs !== "" ? ` (${secs}s` : ""}${frames !== "" ? `, ${frames}f` : ""}${secs !== "" || frames !== "" ? ")" : ""}`;
        const opt = document.createElement("option");
        opt.value = id;
        opt.textContent = label;
        sel.appendChild(opt);
    });

    if (current) sel.value = current;
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
    if (isRegistering) return;
    if (!checkConn()) {
        return;
    }
    const u = document.getElementById('reg-username').value;
    const p = document.getElementById('reg-password').value;
    const p2El = document.getElementById('reg-password2');
    const p2 = p2El ? p2El.value : "";
    if(u && p) {
        if (p2El && p !== p2) {
            const msgEl = document.getElementById("authMessage");
            if (msgEl) {
                msgEl.textContent = "[AUTH] Passwords do not match.";
                msgEl.classList.remove("hidden", "ok");
                msgEl.classList.add("bad");
            } else {
                alert("Passwords do not match.");
            }
            return;
        }
        // Sau khi đăng ký xong sẽ về login.
        preferredUnauthForm = "LOGIN";
        setStatus("Registering Account...");
        isRegistering = true;
        try {
            await connection.invoke("RegisterUser", u, p);
        } finally {
            isRegistering = false;
        }
    } else {
        alert("Please enter both the username and password!");
    }
}

async function loginUser() {
    if (!checkConn()) {
        return;
    }
    const u = document.getElementById('login-username').value;
    const p = document.getElementById('login-password').value;
    if(u && p) {
        setStatus("Login authentication in progress...");
        await connection.invoke("Login", u, p);
    } else {
        alert("Please enter both the username and password!");
    }
}

// [BỎ HÀM KHÔNG CẦN THIẾT] Đã xóa showMainScreen() và hideAllAuthForms() vì đã có handleServerStatus()


async function disconnect() {
    manualDisconnect = true;
    if (connection) await connection.stop();
    isConnected = false;
    updateConnectionUI(false);
    setStatus("Disconnected.");
}

function checkConn() {
    if (!isConnected) {
        alert("Please connect to the server first!");
        return false;
    }
    return true;
}

function wireActionButtons() {
    // [SỬA LỚN] Gắn sự kiện cho các nút Auth và gán vào window
    const btnSetup = document.getElementById("btn-submit-setup");
    if(btnSetup) btnSetup.addEventListener("click", submitSetupCode);

    const btnReg = document.getElementById("btn-submit-register");
    if(btnReg) btnReg.addEventListener("click", registerUser);

    const btnLogin = document.getElementById("btn-submit-login");
    if(btnLogin) btnLogin.addEventListener("click", loginUser);

    const logoutBtn = document.getElementById("logoutBtn");
    if (logoutBtn) {
        logoutBtn.addEventListener("click", async () => {
            localStorage.removeItem("authToken");
            wasAuthenticated = false;
            try {
                if (connection && connection.state === "Connected") await connection.invoke("Logout");
            } catch {}
            handleServerStatus("LOGIN_REQUIRED");
        });
    }
    
    // [GẮN VÀO WINDOW] để HTML gọi trực tiếp (onkeydown)
    window.submitSetupCode = submitSetupCode;
    window.registerUser = registerUser;
    window.loginUser = loginUser;
    window.showSetupForm = showSetupForm;
    window.showLoginForm = showLoginForm;

    document.getElementById("refreshAppsBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Loading application list...");
            connection.invoke("GetProcessList", true);
        }
    });

    document.getElementById("refreshProcessesBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Loading process list...");
            connection.invoke("GetProcessList", false);
        }
    });

    document.getElementById("startAppBtn").addEventListener("click", () => {
        if (checkConn()) {
            const name = document.getElementById("appNameInput").value;
            if (name) connection.invoke("StartProcess", name);
            else alert("Please enter Application name!");
        }
    });

    document.getElementById("startProcessBtn").addEventListener("click", () => {
        if (checkConn()) {
            const name = document.getElementById("processNameInput").value;
            if (name) connection.invoke("StartProcess", name);
            else alert("Please enter Process name or path!");
        }
    });

    document.getElementById("stopSelectedAppBtn").addEventListener("click", () => {
        if (checkConn()) {
            const id = getSelectedId("selectedApp");
            if (id) connection.invoke("KillProcess", id);
            else alert("Please select an App from the list!");
        }
    });

    document.getElementById("stopSelectedProcessBtn").addEventListener("click", () => {
        if (checkConn()) {
            const id = getSelectedId("selectedProcess");
            if (id) connection.invoke("KillProcess", id);
            else alert("Please select an Process from the list!");
        }
    });

    document.getElementById("captureScreenBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Requesting screen capture...");
            connection.invoke("GetScreenshot");
        }
    });
    
    document.getElementById("webcamOnBtn").addEventListener("click", () => {
        if (checkConn()) {
            setStatus("Starting live webcam...");
            isWebcamLive = true;
            connection.invoke("StartWebcamLive", 10);
        }
    });

    // Stop live + save proof
    document.getElementById("webcamOffBtn").addEventListener("click", () => {
        if (checkConn()) {
            isWebcamLive = false;
            connection.invoke("StopWebcamLive");
        }
    });

    const refreshProofBtn = document.getElementById("refreshProofBtn");
    if (refreshProofBtn) {
        refreshProofBtn.addEventListener("click", () => {
            if (checkConn()) connection.invoke("GetWebcamProofList");
        });
    }

    const playProofBtn = document.getElementById("playProofBtn");
    if (playProofBtn) {
        playProofBtn.addEventListener("click", () => {
            if (!checkConn()) return;
            if (isWebcamLive) {
                alert("Please stop the live webcam before playing the evidence.");
                return;
            }
            const sel = document.getElementById("proofSelect");
            const id = sel ? sel.value : "";
            if (!id) {
                alert("Please select an evidence first.");
                return;
            }
            connection.invoke("PlayWebcamProof", id);
        });
    }

    document.getElementById("startKeylogBtn").addEventListener("click", () => {
        if (checkConn()) {
            setKeylogUiState(true);
            connection.invoke("StartKeyLogger");
        }
    });

    document.getElementById("stopKeylogBtn").addEventListener("click", () => {
        if (checkConn()) {
            setKeylogUiState(false);
            connection.invoke("StopKeyLogger");
        }
    });

    document.getElementById("clearKeylogBtn").addEventListener("click", () => {
        const el = document.getElementById("keylogArea");
        if (!el) return;
        if ("value" in el) el.value = "";
        else el.textContent = "";
    });

    const copyKeylogBtn = document.getElementById("copyKeylogBtn");
    if (copyKeylogBtn) {
        copyKeylogBtn.addEventListener("click", async () => {
            const el = document.getElementById("keylogArea");
            if (!el) return;
            const text = ("value" in el) ? el.value : (el.textContent || "");
            try {
                await navigator.clipboard.writeText(text);
                setStatus("Copied keylog.");
            } catch {
                alert("Failed to copy (The browser has blocked Clipboard access.).");
            }
        });
    }

    document.getElementById("restartBtn").addEventListener("click", () => {
        if (checkConn() && confirm("WARNING: Are you sure you want to RESTART the server machine immediately?")) {
            connection.invoke("ShutdownServer", true);
        }
    });

    document.getElementById("shutdownBtn").addEventListener("click", () => {
        if (checkConn() && confirm("WARNING: Are you sure you want to SHUTDOWN the server machine immediately?")) {
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
                if(window.ui) ui.addChatMessage("⚠️ Snowie AI service has not been loaded (please check the ai_service.js file)!", 'bot');
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
                    ui.addChatMessage("AI error: " + err.toString(), 'bot');
                }
            });
        });
    }
}

function setKeylogUiState(isOn) {
    const dot = document.getElementById("keylogDot");
    const text = document.getElementById("keylogStatusText");
    if (dot) {
        dot.classList.remove("on", "off", "warn");
        dot.classList.add(isOn ? "on" : "off");
    }
    if (text) text.textContent = isOn ? "Running" : "Idle";
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
