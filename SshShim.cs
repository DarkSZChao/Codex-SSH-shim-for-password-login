using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// Windows SSH wrapper for Codex Desktop and a password-authenticated remote host.
// Only intercepts calls that contain both the SSH alias in .env
// and the OpenSSH option "-o BatchMode=yes" (or "-oBatchMode=yes").
// No credential is embedded in this source or executable.
public static class SshShim
{
    public static int Main(string[] args)
    {
        try
        {
            // All unrelated SSH commands pass through without modified arguments
            // or environment variables. This includes ssh -G and normal logins.
            // Without a matching BatchMode option, or without a valid
            // TARGET_HOST in .env, leave ordinary SSH completely unchanged.
            string targetHost;
            if (!HasBatchModeYes(args) ||
                !EnvConfig.TryGet("TARGET_HOST", out targetHost) ||
                !ContainsTargetHost(args, targetHost))
            {
                return RunRealSsh(args, false);
            }

            List<string> cleanedArgs = RemoveBatchModeYes(args);
            List<string> finalArgs = new List<string>
            {
                "-o", "BatchMode=no",
                "-o", "PreferredAuthentications=keyboard-interactive,password",
                "-o", "NumberOfPasswordPrompts=3"
            };
            finalArgs.AddRange(cleanedArgs);

            LogIntercept(finalArgs);
            return RunRealSsh(finalArgs.ToArray(), true);
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "codex-ssh-shim.log"),
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") +
                    "  SHIM-ERROR  " + ex + Environment.NewLine);
            }
            catch { /* Logging must not cause a second exception. */ }

            return 255;
        }
    }

    private static int RunRealSsh(string[] args, bool useAskPass)
    {
        string realSsh;
        if (!EnvConfig.TryGet("REAL_SSH", out realSsh) ||
            !Path.IsPathRooted(realSsh) ||
            !(realSsh.StartsWith(@"\\") ||
              (realSsh.Length >= 3 && realSsh[1] == ':' &&
               (realSsh[2] == '\\' || realSsh[2] == '/'))))
        {
            throw new InvalidOperationException("Set REAL_SSH to an absolute OpenSSH executable path in .env.");
        }

        realSsh = Path.GetFullPath(realSsh);
        string shimPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (string.Equals(realSsh, Path.GetFullPath(shimPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("REAL_SSH must not point to the shim itself.");
        if (!File.Exists(realSsh))
            throw new FileNotFoundException("Configured OpenSSH executable not found.", realSsh);

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = realSsh,
            Arguments = BuildCommandLine(args),
            UseShellExecute = false,
            // Critical for Codex's long-lived binary app-server proxy:
            // DO NOT copy, decode, buffer, or redirect stdin/stdout/stderr.
            // The real ssh.exe inherits the shim's original standard handles.
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };

        if (useAskPass)
        {
            string askpass = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "codex-askpass.exe");

            if (!File.Exists(askpass))
            {
                throw new FileNotFoundException("codex-askpass.exe not found.", askpass);
            }

            psi.EnvironmentVariables["SSH_ASKPASS"] = askpass;
            psi.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
            psi.EnvironmentVariables["DISPLAY"] = "codex-ssh-shim";
        }

        using (Process process = Process.Start(psi))
        {
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start the real ssh.exe.");
            }

            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static bool ContainsTargetHost(string[] args, string targetHost)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, targetHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasBatchModeYes(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-o", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && string.Equals(args[i + 1], "BatchMode=yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (args[i].StartsWith("-o", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[i].Substring(2), "BatchMode=yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static List<string> RemoveBatchModeYes(string[] args)
    {
        List<string> result = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-o", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && string.Equals(args[i + 1], "BatchMode=yes", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (args[i].StartsWith("-o", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[i].Substring(2), "BatchMode=yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(args[i]);
        }
        return result;
    }

    private static void LogIntercept(List<string> args)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "codex-ssh-shim.log"),
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") +
                "  CODEX-REMOTE  " + BuildCommandLine(args.ToArray()) +
                Environment.NewLine);
        }
        catch { /* Logging must never interrupt SSH. */ }
    }

    // Quote argv for Windows CreateProcess, preserving spaces, quotes and
    // trailing backslashes. OpenSSH receives the original individual arguments.
    private static string BuildCommandLine(string[] args)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i != 0) sb.Append(' ');
            sb.Append(QuoteWindowsArgument(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteWindowsArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";

        bool needQuotes = false;
        foreach (char c in arg)
        {
            if (char.IsWhiteSpace(c) || c == '"')
            {
                needQuotes = true;
                break;
            }
        }
        if (!needQuotes) return arg;

        StringBuilder sb = new StringBuilder();
        sb.Append('"');
        int backslashes = 0;

        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                sb.Append('\\', backslashes);
                backslashes = 0;
            }
            sb.Append(c);
        }

        if (backslashes > 0) sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
