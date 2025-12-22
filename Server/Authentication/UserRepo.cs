using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Server.Models; 
using System.Text.Json; // [GIỮ NGUYÊN] Dù không dùng cho TXT, giữ để tránh lỗi compile nếu có tham chiếu
using System.IO; 
using System; 

namespace Server.Services
{
    public class UserRepository
    {
        private const string USER_FILE_PATH = "users.txt";

        private readonly List<User> _users = new List<User>();
        private readonly object _lock = new();

        public UserRepository()
        {
            LoadUsers();
        }

        private void LoadUsers()
        {
            if (!File.Exists(USER_FILE_PATH)) return;

            try
            {
                string[] lines = File.ReadAllLines(USER_FILE_PATH);
                var loadedUsers = new List<User>();

                foreach (var line in lines)
                {
                    var parts = line.Split(',', 2);
                    
                    if (parts.Length == 2)
                    {
                        loadedUsers.Add(new User
                        {
                            Username = parts[0].Trim(),
                            PasswordHash = parts[1].Trim()
                        });
                    }
                }

                if (loadedUsers.Any())
                {
                    lock (_lock)
                    {
                        _users.Clear();
                        _users.AddRange(loadedUsers);
                    }
                    Console.WriteLine($"[UserRepo] Loaded {loadedUsers.Count} users from {USER_FILE_PATH} (TXT format).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserRepo ERROR] Failed to load user data: {ex.Message}");
            }
        }

        private void SaveUsers()
        {
            try
            {
                string[] lines;
                lock (_lock)
                {
                    lines = _users.Select(u => $"{u.Username},{u.PasswordHash}").ToArray();
                }

                var temp = USER_FILE_PATH + ".tmp";
                File.WriteAllLines(temp, lines);
                File.Move(temp, USER_FILE_PATH, overwrite: true);

                Console.WriteLine($"[UserRepo] Saved {lines.Length} users to {USER_FILE_PATH}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserRepo ERROR] Error saving user data: {ex.Message}");
            }
        }

        public bool IsAnyUserRegistered()
        {
            lock (_lock)
            {
                return _users.Any();
            }
        }

        public bool IsUsernameTaken(string username)
        {
            lock (_lock)
            {
                return _users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public User? GetUser(string username)
        {
            lock (_lock)
            {
                return _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task<bool> AddUserAsync(string username, string password)
        {
            if (IsUsernameTaken(username)) return false;

            var hash = PasswordHashing.HashPassword(password);
            lock (_lock)
            {
                _users.Add(new User
                {
                    Username = username,
                    PasswordHash = hash
                });
            }
            
            SaveUsers(); 

            await Task.Delay(1); 
            return true;
        }

        public bool TryUpdatePasswordHash(string username, string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(passwordHash)) return false;

            var updated = false;
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (user == null) return false;
                if (string.Equals(user.PasswordHash, passwordHash, StringComparison.Ordinal)) return false;

                user.PasswordHash = passwordHash;
                updated = true;
            }

            if (updated) SaveUsers();
            return updated;
        }
    }
}
