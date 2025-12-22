using Microsoft.AspNetCore.SignalR;
using Server.Services;
using Server.Shared;
using Server.helper;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Net;


namespace Server.Hubs
{
    public class ControlHub : Hub
    {
        private readonly SystemService _systemService;
        private readonly WebcamService _webcamService;
        private readonly WebcamStreamManager _webcamStreamManager;
        private readonly WebcamProofStore _webcamProofStore;
        private readonly InputService _inputService;
        private readonly IHubContext<ControlHub> _hubContext;
        // [THÊM] Dịch vụ xác thực
        private readonly AuthService _authService; 

        public ControlHub(
            SystemService systemService, 
            WebcamService webcamService, 
            WebcamStreamManager webcamStreamManager,
            WebcamProofStore webcamProofStore,
            InputService inputService,
            IHubContext<ControlHub> hubContext,
            AuthService authService // [THÊM] Inject AuthService
        )
        {
            _systemService = systemService;
            _webcamService = webcamService;
            _webcamStreamManager = webcamStreamManager;
            _webcamProofStore = webcamProofStore;
            _inputService = inputService;
            _hubContext = hubContext;
            _authService = authService; // [THÊM] Gán AuthService
        }
        
        // --- XỬ LÝ KẾT NỐI VÀ NGẮT KẾT NỐI ---
        public override async Task OnConnectedAsync()
        {
            // Auto-auth via token (to survive reconnects)
            var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            _authService.TryAuthenticateWithToken(Context.ConnectionId, token);

            // [SỬA] Gửi status chung, sau đó check trạng thái Auth
            await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, true, "SignalR connection successful.");
            await GetServerStatus(); 
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // [THÊM] Xóa phiên khi người dùng ngắt kết nối
            _authService.Logout(Context.ConnectionId);
            await StopKeyLogSessionInternal(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        // --- LOGIC AUTHENTICATION (3 BƯỚC: Setup Code -> Register -> Login) ---
        
        /// <summary>
        /// Xác định trạng thái hiện tại của Server để Client hiển thị UI thích hợp.
        /// </summary>
        public async Task GetServerStatus()
        {
            string status;
            string message;
            
            if (_authService.IsAuthenticated(Context.ConnectionId))
            {
                status = ConnectionStatus.Authenticated;
                message = "Authenticated. Ready to control.";
            }
            else if (_authService.IsRegistrationAllowed(Context.ConnectionId))
            {
                 // Nếu đã nhập Master Code nhưng chưa hoàn tất đăng ký
                status = ConnectionStatus.RegistrationRequired;
                message = "Master Code verified. Please complete account registration.";
            }
            else 
            {
                // Mặc định yêu cầu Login (User phải tự nhập Master Code nếu muốn đăng ký)
                status = ConnectionStatus.LoginRequired; 
                message = "Please log in or enter the Master Code to register.";
            }

            // Gửi trạng thái chi tiết về Client
            await Clients.Caller.SendAsync("ReceiveStatus", "SERVER_STATUS", true, message);
            // Client sẽ dùng code này để chuyển đổi giữa Setup/Register/Login Form
            await Clients.Caller.SendAsync("ReceiveServerStatus", status); 
        }

        // Poll-only variant (avoid spamming status bar).
        public async Task GetServerStatusSilent()
        {
            string status;

            if (_authService.IsAuthenticated(Context.ConnectionId))
            {
                status = ConnectionStatus.Authenticated;
            }
            else if (_authService.IsRegistrationAllowed(Context.ConnectionId))
            {
                status = ConnectionStatus.RegistrationRequired;
            }
            else
            {
                status = ConnectionStatus.LoginRequired;
            }

            await Clients.Caller.SendAsync("ReceiveServerStatus", status);
        }

        /// <summary>
        /// Xử lý Master Setup Code
        /// </summary>
        public async Task SubmitSetupCode(string code)
        {
            if (_authService.ValidateSetupCode(Context.ConnectionId, code))
            {
                // Nếu code đúng, chuyển sang trạng thái chờ đăng ký
                await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, true, "Master code verified. Please register a new account.");
                await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.RegistrationRequired);
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, false, ErrorMessages.SetupCodeInvalid);
                await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.LoginRequired); // Về trạng thái Login
            }
        }

        /// <summary>
        /// Xử lý Đăng ký tài khoản mới (Chỉ được gọi sau khi SubmitSetupCode thành công)
        /// </summary>
        public async Task RegisterUser(string username, string password)
        {
            var result = await _authService.TryRegisterAsync(Context.ConnectionId, username, password);
            if (result != AuthService.RegisterResult.Success)
            {
                var msg = result switch
                {
                    AuthService.RegisterResult.NotAllowed => ErrorMessages.RegistrationNotAllowed,
                    AuthService.RegisterResult.GrantExpired => ErrorMessages.RegistrationExpired,
                    AuthService.RegisterResult.UsernameTaken => ErrorMessages.UsernameTaken,
                    AuthService.RegisterResult.InvalidUsername => ErrorMessages.InvalidUsername,
                    AuthService.RegisterResult.InvalidPassword => ErrorMessages.InvalidPassword,
                    _ => ErrorMessages.RegistrationFailed
                };

                await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, false, msg);
                if (result is AuthService.RegisterResult.NotAllowed or AuthService.RegisterResult.GrantExpired)
                    await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.LoginRequired);
                return;
            }

            // Đăng ký thành công, tự động đăng nhập và chuyển sang Dashboard
            await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, true, $"Successfully registered account: {username}. Please Log in.");
            await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.LoginRequired);
        }

        /// <summary>
        /// Xử lý Đăng nhập
        /// </summary>
        public async Task Login(string username, string password)
        {
            if (_authService.TryAuthenticate(Context.ConnectionId, username, password))
            {
                var token = _authService.IssueToken(username);
                if (!string.IsNullOrEmpty(token))
                    await Clients.Caller.SendAsync("ReceiveAuthToken", token);

                await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, true, $"Login successful, welcome {username}.");
                await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.Authenticated);
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, false, ErrorMessages.InvalidCredentials);
            }
        }

        public async Task Logout()
        {
            _authService.Logout(Context.ConnectionId);
            await StopKeyLogSessionInternal(Context.ConnectionId);
            await Clients.Caller.SendAsync("ReceiveStatus", StatusType.Auth, true, "Logged out.");
            await Clients.Caller.SendAsync("ReceiveServerStatus", ConnectionStatus.LoginRequired);
        }


        // --- NHÓM 1: HỆ THỐNG (LIST, START, KILL, SHUTDOWN) ---

        public async Task GetProcessList(bool isAppOnly)
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            var list = _systemService.ListProcessOrApp(isAppOnly);
            // Gửi kết quả về cho người gọi (Caller)
            string json = JsonHelper.ToJson(list);
            await Clients.Caller.SendAsync("ReceiveProcessList", json);
        }

        public async Task StartProcess(string path)
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            bool result = _systemService.startProcessOrApp(path);
            await Clients.Caller.SendAsync("ReceiveStatus", "START", result, result ? "Open command sent" : "File open error");
        }

        public async Task KillProcess(int id)
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            bool result = _systemService.killProcessOrApp(id);
            await Clients.Caller.SendAsync("ReceiveStatus", "KILL", result, result ? "Successfully terminated" : "Cannot terminate");
        }

        public async Task ShutdownServer(bool isRestart)
        {
           if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
           
            bool result = _systemService.shutdownOrRestart(isRestart);
            await Clients.Caller.SendAsync("ReceiveStatus", "POWER", result, "Executing Power command......");
        }

        // --- NHÓM 2: MÀN HÌNH & WEBCAM ---

        public async Task GetScreenshot()
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            byte[] image = _webcamService.captureScreen();
            // Gửi ảnh về Client
            await Clients.Caller.SendAsync("ReceiveImage", "SCREENSHOT", image);
        }

        // Live webcam + record proof first 10 seconds.
        public async Task StartWebcamLive(int fps = 10)
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]

            var connectionId = Context.ConnectionId;

            if (_webcamStreamManager.IsActive(connectionId))
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Webcam is running.");
                return;
            }

            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Starting live webcam and recording the first 10 seconds as evidence...");

            var ok = await _webcamStreamManager.StartAsync(
                connectionId,
                fps,
                TimeSpan.FromSeconds(10),
                async (frame, ct) =>
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveImage", "WEBCAM_LIVE", frame, ct);
                },
                async (meta) =>
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveStatus", "WEBCAM", true, $"The first 10-second evidence has been saved: {meta.Id}");
                    var list = _webcamProofStore.List();
                    var json = JsonHelper.ToJson(list);
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveWebcamProofList", json);
                },
                Context.ConnectionAborted);

            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", ok, ok ? "Webcam live has started." : "Unable to enable the webcam live.");
        }

        public async Task StopWebcamLive()
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]

            var connectionId = Context.ConnectionId;
            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Stopping the webcam (evidence will be saved immediately)...");

            _ = Task.Run(async () =>
            {
                try
                {
                    var meta = await _webcamStreamManager.StopAndSaveAsync(connectionId, CancellationToken.None);
                    if (meta == null)
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveStatus", "WEBCAM", true, "Webcam turned off (no evidence recorded).");
                    }
                    else
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveStatus", "WEBCAM", true, $"Evidence saved: {meta.Id} ({meta.FrameCount} frames).");
                    }

                    var list = _webcamProofStore.List();
                    var json = JsonHelper.ToJson(list);
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveWebcamProofList", json);
                }
                catch
                {
                    // ignore
                }
            }, CancellationToken.None);
        }

        public async Task GetWebcamProofList()
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            await SendWebcamProofList();
        }

        public async Task PlayWebcamProof(string proofId)
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]

            if (string.IsNullOrWhiteSpace(proofId))
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", false, "No evidence selected.");
                return;
            }

            var token = Context.ConnectionAborted;
            var frames = await _webcamProofStore.LoadFramesAsync(proofId, token);
            if (frames.Count == 0)
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", false, "Evidence not found.");
                return;
            }

            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, $"Playing evidence {proofId} ({frames.Count} frames)...");
            foreach (var frame in frames)
            {
                await Clients.Caller.SendAsync("ReceiveImage", "WEBCAM_PROOF_FRAME", frame, token);
                await Task.Delay(100, token);
            }
            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Playback completed.");
        }

        private Task SendWebcamProofList()
        {
            var list = _webcamProofStore.List();
            var json = JsonHelper.ToJson(list);
            return Clients.Caller.SendAsync("ReceiveWebcamProofList", json);
        }

        // Back-compat: old buttons call these.
        public async Task RequestWebcam()
        {
            await StartWebcamLive(10);
        }
        public async Task CloseWebcam()
        {
            await StopWebcamLive();
        }

        // --- NHÓM 3: KEYLOGGER (INPUT) ---

        public async Task StartKeyLogger()
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            string connectionId = Context.ConnectionId;
            
            _inputService.StartKeyLoggerSession(connectionId);
            await Clients.Caller.SendAsync("ReceiveKeyLog", "\n--- keylog session enabled ---\n");

            // Windows: thử bật global keylogger (SharpHook). macOS/Linux: dùng trang /server-keycapture (tab phải focus).
            if (OperatingSystem.IsWindows())
            {
                await _inputService.StartKeyLogger((keyData) => RelayKeyLog(keyData));
                await Clients.Caller.SendAsync("ReceiveStatus", "KEYLOG", true, "Keylogger system is running.");
                return;
            }

            await Clients.Caller.SendAsync("ReceiveStatus", "KEYLOG", true, "Enable key logging (JS). On the server machine, open /server-keycapture and click Enable (the browser tab must be focused).");
        }

        public async Task StopKeyLogger()
        {
            if (!_authService.IsAuthenticated(Context.ConnectionId)) return; // [AUTH CHECK]
            
            await StopKeyLogSessionInternal(Context.ConnectionId);
            await Clients.Caller.SendAsync("ReceiveKeyLog", "\n--- keylog session disabled ---\n");
            await Clients.Caller.SendAsync("ReceiveStatus", "KEYLOG", false, "Keylogger has stopped.");
        }

        /// <summary>
        /// Receive key-capture events from local server page (/server-keycapture).
        /// This is NOT system-wide: only captures while the tab is focused.
        /// </summary>
        public async Task SendServerCapturedKey(string keyData)
        {
            var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress;
            if (remoteIp == null)
                return;
            if (!IPAddress.IsLoopback(remoteIp) && !(remoteIp.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteIp.MapToIPv4())))
                return;

            if (string.IsNullOrWhiteSpace(keyData))
                return;

            await RelayKeyLog(keyData);
        }

        private async Task RelayKeyLog(string keyData)
        {
            var targets = _inputService.GetActiveKeyLoggerSessionConnectionIds();
            if (targets.Count == 0) return;

            var payload = keyData.Replace("\r\n", "\n");
            foreach (var targetConnectionId in targets)
            {
                try
                {
                    await _hubContext.Clients.Client(targetConnectionId).SendAsync("ReceiveKeyLog", payload);
                }
                catch
                {
                    // ignore transient network errors
                }
            }
        }

        private async Task StopKeyLogSessionInternal(string connectionId)
        {
            _inputService.StopKeyLoggerSession(connectionId);
            if (_inputService.ActiveKeyLoggerSessionCount == 0)
            {
                await _inputService.StopKeyLogger();
            }
        }

    }
}
