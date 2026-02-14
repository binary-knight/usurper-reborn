using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Server;

/// <summary>
/// Thin relay client that bridges stdin/stdout to the MUD TCP game server.
/// Used as the SSH ForceCommand target: when a player SSHes in, sshd runs this
/// relay which connects to the game server on localhost and passes bytes back
/// and forth.
///
/// Usage: UsurperReborn --mud-relay [--user "PlayerName"] [--mud-port 4001]
///
/// Flow:
///   1. If no --user flag, prompt for username and password via stdin/stdout
///   2. Connect to localhost:port
///   3. Send AUTH:username:password:connectionType\n
///   4. Read response (OK or ERR:reason)
///   5. If ERR, show error, disconnect, go back to step 1
///   6. If OK, bridge stdin↔socket and socket↔stdout until either side closes
/// </summary>
public static class RelayClient
{
    private const int MAX_AUTH_ATTEMPTS = 5;

    public static async Task RunAsync(string username, int port, string connectionType, CancellationToken ct)
    {
        // If the user is pre-authenticated (e.g. --user flag), connect directly
        if (!string.IsNullOrEmpty(username) && username != "anonymous")
        {
            await ConnectAndBridge(username, null, port, connectionType, ct);
            return;
        }

        // Otherwise, prompt for credentials and try connecting in a loop
        var stdout = Console.OpenStandardOutput();

        for (int attempt = 0; attempt < MAX_AUTH_ATTEMPTS; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            // Show auth menu
            await ShowAuthMenu(stdout, ct);

            var choice = (await ReadStdinLine(ct))?.Trim();
            if (string.IsNullOrEmpty(choice)) continue;

            var choiceUpper = choice.ToUpperInvariant();
            if (choiceUpper == "Q") return;

            // Support direct AUTH passthrough from game client (OnlinePlaySystem sends AUTH via SSH)
            if (choice.StartsWith("AUTH:", StringComparison.Ordinal))
            {
                var authResult = ParseDirectAuth(choice);
                if (authResult.HasValue)
                {
                    bool directConnected = await ConnectAndBridge(
                        authResult.Value.username, authResult.Value.password,
                        port, authResult.Value.connectionType, ct,
                        isRegistration: authResult.Value.isRegistration);
                    if (directConnected) return;
                    continue; // Auth failed, loop back
                }
            }

            string? authUsername = null;
            string? authPassword = null;

            if (choiceUpper == "L")
            {
                // Login
                await WriteAnsi(stdout, "\r\n\u001b[1;37m  Username: \u001b[0m");
                await stdout.FlushAsync(ct);
                authUsername = (await ReadStdinLine(ct))?.Trim();
                if (string.IsNullOrEmpty(authUsername)) continue;

                await WriteAnsi(stdout, "\u001b[1;37m  Password: \u001b[0m");
                await stdout.FlushAsync(ct);
                authPassword = (await ReadPasswordLine(stdout, ct))?.Trim();
                if (string.IsNullOrEmpty(authPassword)) continue;
            }
            else if (choiceUpper == "R")
            {
                // Register
                await WriteAnsi(stdout, "\r\n\u001b[1;32m  Choose a username: \u001b[0m");
                await stdout.FlushAsync(ct);
                authUsername = (await ReadStdinLine(ct))?.Trim();
                if (string.IsNullOrEmpty(authUsername)) continue;

                if (authUsername.Length < 2 || authUsername.Length > 20)
                {
                    await WriteAnsi(stdout, "\r\n\u001b[1;31m  Username must be 2-20 characters.\u001b[0m\r\n\r\n");
                    await stdout.FlushAsync(ct);
                    continue;
                }

                await WriteAnsi(stdout, "\u001b[1;32m  Choose a password: \u001b[0m");
                await stdout.FlushAsync(ct);
                authPassword = (await ReadPasswordLine(stdout, ct))?.Trim();
                if (string.IsNullOrEmpty(authPassword)) continue;

                if (authPassword.Length < 4)
                {
                    await WriteAnsi(stdout, "\r\n\u001b[1;31m  Password must be at least 4 characters.\u001b[0m\r\n\r\n");
                    await stdout.FlushAsync(ct);
                    continue;
                }

                await WriteAnsi(stdout, "\u001b[1;32m  Confirm password: \u001b[0m");
                await stdout.FlushAsync(ct);
                var confirm = (await ReadPasswordLine(stdout, ct))?.Trim();
                if (authPassword != confirm)
                {
                    await WriteAnsi(stdout, "\r\n\u001b[1;31m  Passwords do not match.\u001b[0m\r\n\r\n");
                    await stdout.FlushAsync(ct);
                    continue;
                }
            }
            else
            {
                continue; // Invalid choice
            }

            // Try connecting with these credentials
            // ConnectAndBridge returns true if successfully connected and played,
            // false if auth failed (we should retry)
            bool isReg = choiceUpper == "R";
            bool connected = await ConnectAndBridge(authUsername!, authPassword, port, connectionType, ct, isRegistration: isReg);
            if (connected)
                return; // Session completed (player quit or disconnected)

            // Auth failed — loop back to prompt
        }

        await WriteAnsi(stdout, "\r\n\u001b[1;31m  Too many attempts. Goodbye.\u001b[0m\r\n");
        await stdout.FlushAsync(ct);
    }

    /// <summary>
    /// Parse a direct AUTH header sent by the game client through SSH.
    /// Format: AUTH:username:password:connectionType or AUTH:username:password:REGISTER:connectionType
    /// </summary>
    private static (string username, string password, string connectionType, bool isRegistration)?
        ParseDirectAuth(string authLine)
    {
        // Strip the "AUTH:" prefix
        var payload = authLine.Substring(5);
        var parts = payload.Split(':');

        if (parts.Length == 3)
        {
            // AUTH:username:password:connectionType
            return (parts[0], parts[1], parts[2], false);
        }
        else if (parts.Length == 4 && parts[2].Equals("REGISTER", StringComparison.OrdinalIgnoreCase))
        {
            // AUTH:username:password:REGISTER:connectionType
            return (parts[0], parts[1], parts[3], true);
        }

        Console.Error.WriteLine($"[RELAY] Invalid AUTH format: {parts.Length} parts");
        return null;
    }

    /// <summary>
    /// Connect to the MUD server, authenticate, and bridge stdin/stdout.
    /// Returns true if the session ran (even if it ended), false if auth failed.
    /// </summary>
    private static async Task<bool> ConnectAndBridge(
        string username, string? password, int port, string connectionType, CancellationToken ct,
        bool isRegistration = false)
    {
        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, ct);
            client.NoDelay = true;

            var stream = client.GetStream();

            // Send AUTH header
            string authLine;
            if (password != null && isRegistration)
                authLine = $"AUTH:{username}:{password}:REGISTER:{connectionType}\n";
            else if (password != null)
                authLine = $"AUTH:{username}:{password}:{connectionType}\n";
            else
                authLine = $"AUTH:{username}:{connectionType}\n";
            var authBytes = Encoding.UTF8.GetBytes(authLine);
            await stream.WriteAsync(authBytes, 0, authBytes.Length, ct);

            // Read response line
            var response = await ReadLineAsync(stream, ct);
            if (response == null)
            {
                Console.Error.WriteLine("[RELAY] Server closed connection during auth");
                return false;
            }

            if (response.StartsWith("ERR:"))
            {
                Console.Error.WriteLine($"[RELAY] Auth failed: {response}");
                var stdout = Console.OpenStandardOutput();
                // Echo raw ERR response for programmatic clients (OnlinePlaySystem)
                await WriteAnsi(stdout, $"{response}\r\n");
                await WriteAnsi(stdout, $"\r\n\u001b[1;31m  {response.Substring(4)}\u001b[0m\r\n\r\n");
                await stdout.FlushAsync(ct);
                client.Close();
                return false; // Auth failed — caller should retry
            }

            if (response != "OK")
            {
                Console.Error.WriteLine($"[RELAY] Unexpected auth response: {response}");
                return false;
            }

            // Echo OK back to stdout for programmatic clients (OnlinePlaySystem reads this through SSH)
            {
                var stdout = Console.OpenStandardOutput();
                await WriteAnsi(stdout, "OK\r\n");
                await stdout.FlushAsync(ct);
            }

            // Auth succeeded — bridge stdin/stdout ↔ TCP socket
            Console.Error.WriteLine($"[RELAY] Connected as '{username}', bridging I/O");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var stdinToSocket = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[4096];
                    var stdin = Console.OpenStandardInput();
                    while (!linkedCts.Token.IsCancellationRequested)
                    {
                        int read = await stdin.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
                        if (read == 0) break; // stdin closed (SSH disconnected)
                        await stream.WriteAsync(buffer, 0, read, linkedCts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                finally
                {
                    linkedCts.Cancel();
                }
            });

            var socketToStdout = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[4096];
                    var stdout = Console.OpenStandardOutput();
                    while (!linkedCts.Token.IsCancellationRequested)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
                        if (read == 0) break; // Server closed connection
                        await stdout.WriteAsync(buffer, 0, read, linkedCts.Token);
                        await stdout.FlushAsync(linkedCts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                finally
                {
                    linkedCts.Cancel();
                }
            });

            await Task.WhenAny(stdinToSocket, socketToStdout);
            linkedCts.Cancel();
            try { await Task.WhenAll(stdinToSocket, socketToStdout); } catch { }

            return true; // Session completed
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"[RELAY] Cannot connect to game server on port {port}: {ex.Message}");
            var stdout = Console.OpenStandardOutput();
            await WriteAnsi(stdout, "\r\n\u001b[1;31m  Game server is not available. Please try again later.\u001b[0m\r\n");
            await stdout.FlushAsync(ct);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true; // Normal shutdown
        }
        finally
        {
            client?.Close();
        }
    }

    private static async Task ShowAuthMenu(Stream stdout, CancellationToken ct)
    {
        await WriteAnsi(stdout, "\u001b[2J\u001b[H"); // Clear screen
        await WriteAnsi(stdout, "\u001b[1;36m");
        await WriteAnsi(stdout, "╔══════════════════════════════════════════════════════════════════════════════╗\r\n");
        await WriteAnsi(stdout, "\u001b[1;37m");
        await WriteAnsi(stdout, "║                    Welcome to Usurper Reborn Online                        ║\r\n");
        await WriteAnsi(stdout, "\u001b[1;36m");
        await WriteAnsi(stdout, "╠══════════════════════════════════════════════════════════════════════════════╣\r\n");
        await WriteAnsi(stdout, "\u001b[0;37m");
        await WriteAnsi(stdout, "║                                                                            ║\r\n");
        await WriteAnsi(stdout, "║  \u001b[1;36m[L]\u001b[0;37m Login to existing account                                           ║\r\n");
        await WriteAnsi(stdout, "║  \u001b[1;32m[R]\u001b[0;37m Register new account                                                ║\r\n");
        await WriteAnsi(stdout, "║  \u001b[1;31m[Q]\u001b[0;37m Quit                                                                ║\r\n");
        await WriteAnsi(stdout, "║                                                                            ║\r\n");
        await WriteAnsi(stdout, "\u001b[1;36m");
        await WriteAnsi(stdout, "╚══════════════════════════════════════════════════════════════════════════════╝\r\n");
        await WriteAnsi(stdout, "\u001b[0m");
        await WriteAnsi(stdout, "\r\n  Choice: ");
        await stdout.FlushAsync(ct);
    }

    /// <summary>Write ANSI text to a stream.</summary>
    private static async Task WriteAnsi(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>Read a line from stdin (blocking read with async wrapper).</summary>
    private static async Task<string?> ReadStdinLine(CancellationToken ct)
    {
        return await Task.Run(() => Console.ReadLine(), ct);
    }

    /// <summary>Read a password from stdin, echoing '*' for each character.</summary>
    private static async Task<string?> ReadPasswordLine(Stream stdout, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            var password = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Enter)
                    {
                        await WriteAnsi(stdout, "\r\n");
                        await stdout.FlushAsync(ct);
                        return password.ToString();
                    }

                    if (key.Key == ConsoleKey.Backspace || key.KeyChar == '\x7f')
                    {
                        if (password.Length > 0)
                        {
                            password.Remove(password.Length - 1, 1);
                            await WriteAnsi(stdout, "\b \b");
                            await stdout.FlushAsync(ct);
                        }
                        continue;
                    }

                    if (key.KeyChar >= ' ') // printable character
                    {
                        password.Append(key.KeyChar);
                        await WriteAnsi(stdout, "*");
                        await stdout.FlushAsync(ct);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // No terminal (redirected stdin) — fall back to plain readline
                Console.Error.WriteLine("[RELAY] Console.ReadKey not available, falling back to plain input");
                var line = Console.ReadLine();
                await WriteAnsi(stdout, "\r\n");
                return line;
            }
            return null;
        }, ct);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var line = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, 0, 1, ct);
            if (read == 0) return null;

            char c = (char)buffer[0];
            if (c == '\n') return line.ToString().TrimEnd('\r');
            line.Append(c);

            if (line.Length > 1024) return null;
        }

        return null;
    }
}
