using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq; 
using System; 
using Server.Models; // Cần thiết để gán Role User
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Shared;

namespace Server.Services
{
    /// <summary>
    /// Service quản lý phiên đăng nhập và xác thực theo cơ chế Master Code -> Register -> Login.
    /// Master Code được yêu cầu cho MỌI LẦN ĐĂNG KÝ TÀI KHOẢN.
    /// </summary>
        public class AuthService
        {
	        private const int DefaultTokenTtlMinutes = 10;
	        private const int DefaultRegistrationGrantMinutes = 10;

        public enum RegisterResult
        {
            Success = 0,
            NotAllowed = 1,
            GrantExpired = 2,
            UsernameTaken = 3,
            InvalidUsername = 4,
            InvalidPassword = 5,
            Failed = 6
        }
        
        // Key: ConnectionId của Client, Value: Username đã đăng nhập
        private sealed record AuthSession(string Username, long ExpUnixSeconds);

        private readonly ConcurrentDictionary<string, AuthSession> _authenticatedConnections =
            new ConcurrentDictionary<string, AuthSession>();
        
        // Key: ConnectionId đã nhập đúng Setup Code, chờ đăng ký tài khoản (TẠM THỜI)
        private readonly ConcurrentDictionary<string, long> _setupPendingConnections =
            new ConcurrentDictionary<string, long>();
        
        private readonly UserRepository _userRepository;
        private readonly byte[] _tokenKey;
        private readonly TimeSpan _tokenTtl;
        private readonly string _masterSetupCode;
        private readonly TimeSpan _registrationGrantTtl;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

	        public AuthService(UserRepository userRepository) // Inject UserRepository
	        {
	            _userRepository = userRepository;
	            var master = Environment.GetEnvironmentVariable("AUTH_MASTER_CODE");
	            if (string.IsNullOrWhiteSpace(master))
	            {
	                throw new InvalidOperationException(
	                    "Missing environment variable AUTH_MASTER_CODE. Please set AUTH_MASTER_CODE before starting the server.");
	            }
	            _masterSetupCode = master;

	            var secret = Environment.GetEnvironmentVariable("AUTH_TOKEN_SECRET");
	            if (string.IsNullOrWhiteSpace(secret))
	            {
                // Stable default for dev/demo (so tokens survive server restart).
                secret = _masterSetupCode + "::auth_token_secret";
            }
            _tokenKey = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

            var ttlHoursStr = Environment.GetEnvironmentVariable("AUTH_TOKEN_TTL_HOURS");
            var ttlMinutesStr = Environment.GetEnvironmentVariable("AUTH_TOKEN_TTL_MINUTES");

            if (int.TryParse(ttlMinutesStr, out var ttlMinutes) && ttlMinutes > 0 && ttlMinutes <= 24 * 60)
            {
                _tokenTtl = TimeSpan.FromMinutes(ttlMinutes);
            }
            else if (int.TryParse(ttlHoursStr, out var ttlHours) && ttlHours > 0 && ttlHours <= 24 * 7)
            {
                _tokenTtl = TimeSpan.FromHours(ttlHours);
            }
            else
            {
                _tokenTtl = TimeSpan.FromMinutes(DefaultTokenTtlMinutes);
            }

            var grantMinStr = Environment.GetEnvironmentVariable("AUTH_REGISTRATION_GRANT_MINUTES");
            if (!int.TryParse(grantMinStr, out var grantMin) || grantMin <= 0 || grantMin > 24 * 60)
                grantMin = DefaultRegistrationGrantMinutes;
            _registrationGrantTtl = TimeSpan.FromMinutes(grantMin);
        }
        
        // --- LOGIC QUẢN LÝ TRẠNG THÁI KHỞI ĐỘNG/MASTER CODE ---

        /// <summary>
        /// Kiểm tra Mã Khóa Chủ. Mã này có thể dùng nhiều lần để đăng ký tài khoản.
        /// </summary>
        public bool ValidateSetupCode(string connectionId, string code)
        {
            // So sánh mã
            if (string.Equals(code, _masterSetupCode, StringComparison.Ordinal))
            {
                // Đánh dấu Client này đã được phép ĐĂNG KÝ (tạm thời)
                _setupPendingConnections[connectionId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Kiểm tra xem kết nối này có đang ở trạng thái chờ ĐĂNG KÝ không (đã nhập Master Code).
        /// </summary>
        public bool IsRegistrationAllowed(string connectionId)
        {
            if (!_setupPendingConnections.TryGetValue(connectionId, out var grantedAtUnix))
                return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAt = grantedAtUnix + (long)_registrationGrantTtl.TotalSeconds;
            if (now > expiresAt)
            {
                _setupPendingConnections.TryRemove(connectionId, out _);
                return false;
            }

            return true;
        }

        // --- LOGIC ĐĂNG KÝ/ĐĂNG NHẬP ---

        private static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            var u = username.Trim();
            if (u.Length < 3 || u.Length > 32) return false;
            if (u.Contains(',') || u.Contains('\n') || u.Contains('\r')) return false;

            foreach (var ch in u)
            {
                var ok = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.';
                if (!ok) return false;
            }
            return true;
        }

        private static bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            if (password.Length < 8) return false;
            if (password.Contains('\n') || password.Contains('\r')) return false;
            return true;
        }
        
        /// <summary>
        /// Thử đăng ký người dùng mới (Yêu cầu phải nhập Master Code trước).
        /// </summary>
        public async Task<RegisterResult> TryRegisterAsync(string connectionId, string username, string password)
        {
            // [BẮT BUỘC] Phải nhập Master Code trước khi đăng ký
            if (!_setupPendingConnections.TryGetValue(connectionId, out var grantedAtUnix))
                return RegisterResult.NotAllowed;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAt = grantedAtUnix + (long)_registrationGrantTtl.TotalSeconds;
            if (now > expiresAt)
            {
                _setupPendingConnections.TryRemove(connectionId, out _);
                return RegisterResult.GrantExpired;
            }

            if (!IsValidUsername(username)) return RegisterResult.InvalidUsername;
            if (!IsValidPassword(password)) return RegisterResult.InvalidPassword;

            // 1. Kiểm tra username đã tồn tại chưa
            if (_userRepository.IsUsernameTaken(username)) return RegisterResult.UsernameTaken;

            // 2. Thêm người dùng mới vào UserRepository
            try
            {
                if (await _userRepository.AddUserAsync(username, password))
                {
                    // [TIÊU THỤ KEY] Chỉ xóa quyền đăng ký khi đã đăng ký thành công.
                    _setupPendingConnections.TryRemove(connectionId, out _);
                    return RegisterResult.Success;
                }
            }
            catch
            {
                return RegisterResult.Failed;
            }

            return RegisterResult.Failed;
        }

        /// <summary>
        /// Xác thực thông tin và lưu trạng thái đăng nhập.
        /// </summary>
        public bool TryAuthenticate(string connectionId, string username, string password)
        {
            // 1. Kiểm tra Server đã được Setup chưa (nếu chưa thì không thể đăng nhập)
            if (!_userRepository.IsAnyUserRegistered()) return false; 

            var user = _userRepository.GetUser(username);
            
            if (user == null) return false;

            // 2. Kiểm tra mật khẩu (PBKDF2 + salt, auto-upgrade nếu dữ liệu legacy/plaintext)
            if (PasswordHashing.VerifyPassword(user.PasswordHash, password, out var shouldUpgrade, out var upgradedHash))
            {
                if (shouldUpgrade && !string.IsNullOrWhiteSpace(upgradedHash))
                {
                    _userRepository.TryUpdatePasswordHash(user.Username, upgradedHash);
                }

                var exp = DateTimeOffset.UtcNow.Add(_tokenTtl).ToUnixTimeSeconds();
                _authenticatedConnections[connectionId] = new AuthSession(username, exp);
                return true;
            }
            return false;
        }

        public bool TryAuthenticateWithToken(string connectionId, string? token)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(token))
                return false;

            if (!TryValidateToken(token, out var username, out var expUnix)) return false;
            if (!_userRepository.IsUsernameTaken(username)) return false;
            _authenticatedConnections[connectionId] = new AuthSession(username, expUnix);
            return true;
        }

        public string IssueToken(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return string.Empty;

            return CreateToken(username, _tokenTtl);
        }

        private string CreateToken(string username, TimeSpan ttl)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object?>
            {
                ["u"] = username,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.Add(ttl).ToUnixTimeSeconds()
            };

            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var payloadB64 = Base64UrlEncode(payloadBytes);

            using var hmac = new HMACSHA256(_tokenKey);
            var sig = hmac.ComputeHash(Encoding.ASCII.GetBytes(payloadB64));
            var sigB64 = Base64UrlEncode(sig);

            return payloadB64 + "." + sigB64;
        }

        private bool TryValidateToken(string token, out string username, out long expUnix)
        {
            username = string.Empty;
            expUnix = 0;
            var parts = token.Split('.', 2);
            if (parts.Length != 2) return false;

            var payloadB64 = parts[0];
            var sigB64 = parts[1];

            byte[] expectedSig;
            using (var hmac = new HMACSHA256(_tokenKey))
            {
                expectedSig = hmac.ComputeHash(Encoding.ASCII.GetBytes(payloadB64));
            }
            var expectedB64 = Base64UrlEncode(expectedSig);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedB64), Encoding.ASCII.GetBytes(sigB64)))
                return false;

            var payloadBytes = Base64UrlDecode(payloadB64);
            if (payloadBytes.Length == 0) return false;

            try
            {
                using var doc = JsonDocument.Parse(payloadBytes);
                var root = doc.RootElement;
                if (!root.TryGetProperty("u", out var uProp)) return false;
                if (!root.TryGetProperty("iat", out var iatProp)) return false;
                if (!root.TryGetProperty("exp", out var expProp)) return false;
                var u = uProp.GetString();
                if (string.IsNullOrWhiteSpace(u)) return false;
                var iat = iatProp.GetInt64();
                var exp = expProp.GetInt64();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now > exp) return false;

                // Enforce current max token lifetime to invalidate old long-lived tokens.
                // Accept a small clock skew.
                const long skewSeconds = 60;
                if (iat > now + skewSeconds) return false;
                var lifetime = exp - iat;
                if (lifetime <= 0) return false;
                var maxLifetime = (long)_tokenTtl.TotalSeconds + skewSeconds;
                if (lifetime > maxLifetime) return false;

                username = u;
                expUnix = exp;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string s)
        {
            try
            {
                s = s.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                }
                return Convert.FromBase64String(s);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }
        
        // --- LOGIC TRUY VẤN VÀ QUẢN LÝ PHIÊN ---
        
        public bool IsUsernameTaken(string username)
        {
            return _userRepository.IsUsernameTaken(username);
        }
        
        public bool IsAnyUserRegistered()
        {
            return _userRepository.IsAnyUserRegistered();
        }

        /// <summary>
        /// Kiểm tra xem kết nối hiện tại đã được xác thực chưa.
        /// </summary>
        public bool IsAuthenticated(string connectionId)
        {
            if (!_authenticatedConnections.TryGetValue(connectionId, out var session))
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > session.ExpUnixSeconds)
            {
                _authenticatedConnections.TryRemove(connectionId, out _);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Xóa trạng thái xác thực khi Client ngắt kết nối.
        /// </summary>
        public void Logout(string connectionId)
        {
            _authenticatedConnections.TryRemove(connectionId, out _);
            _setupPendingConnections.TryRemove(connectionId, out _); // Xóa luôn cả trạng thái Setup tạm thời
        }
    }
}
