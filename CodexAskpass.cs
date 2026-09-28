using System;
using System.Text;

// OpenSSH executes this helper via SSH_ASKPASS when it needs the SSH password.
// It reads PASSWORD directly from .env next to the executable.
// Keep that file private. Never include a real password in source control.
public static class CodexAskpass
{
    public static int Main(string[] args)
    {
        try
        {
            // Keep the credential in the private .env alongside the executables.
            // Quote PASSWORD to preserve leading/trailing spaces. The .env reader
            // ignores file line endings without trimming spaces inside quotes.
            string password;
            if (!EnvConfig.TryGet("PASSWORD", out password)) return 1;

            // Reject missing or multi-line passwords; print no diagnostics.
            if (password.Length == 0 || password.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                return 1;
            }

            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Out.WriteLine(password);
            Console.Out.Flush();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
