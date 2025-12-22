namespace Server.Shared
{
    public static class ConnectionStatus
    {
        public const string RegistrationRequired = "REGISTRATION_REQUIRED"; 
        public const string Authenticated = "AUTHENTICATED"; 
        public const string LoginRequired = "LOGIN_REQUIRED"; 
    }

    public static class StatusType
    {
        public const string Auth = "AUTH";
        public const string App = "APP";
        public const string Keylog = "KEYLOG";
        public const string Screen = "SCREENSHOT";
        public const string Webcam = "WEBCAM";
        public const string System = "SYSTEM";
    }

    /// <summary>
    /// Các thông báo lỗi tiêu chuẩn.
    /// </summary>
    public static class ErrorMessages
    {
        public const string SetupCodeInvalid = "Master Code is not correct.";
        public const string RegistrationNotAllowed = "You must enter the Master Code before registering a new account.";
        public const string RegistrationExpired = "The registration session has expired. Please re-enter the Master Code.";
        public const string InvalidUsername = "Invalid username (3–32 characters, letters/numbers only, and . _ - are allowed).";
        public const string InvalidPassword = "Invalid password (minimum 8 characters).";
        public const string RegistrationFailed = "Registration failed. Please try again.";
        public const string UsernameTaken = "Username already exists.";
        public const string InvalidCredentials = "Incorrect username or password.";
    }
}
