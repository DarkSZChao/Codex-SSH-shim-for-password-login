// Shared, minimal .env reader. Compiled into both standalone executables.
// Configuration lives next to the executable; no external .env package needed.
internal static class EnvConfig
{
    internal static bool TryGet(string name, out string value)
    {
        value = null;
        string path = System.IO.Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory, ".env");

        try
        {
            if (!System.IO.File.Exists(path)) return false;

            foreach (string original in System.IO.File.ReadAllLines(
                path, System.Text.Encoding.UTF8))
            {
                string line = original.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;

                string key = line.Substring(0, equals).Trim();
                if (!string.Equals(
                    key, name, System.StringComparison.OrdinalIgnoreCase)) continue;

                string candidate = line.Substring(equals + 1).Trim();
                if (candidate.Length >= 2 &&
                    ((candidate[0] == '"' && candidate[candidate.Length - 1] == '"') ||
                     (candidate[0] == '\'' && candidate[candidate.Length - 1] == '\'')))
                {
                    candidate = candidate.Substring(1, candidate.Length - 2);
                }

                if (candidate.Length == 0) return false;
                value = candidate;
                return true;
            }
        }
        catch (System.IO.IOException) { }
        catch (System.UnauthorizedAccessException) { }

        return false;
    }
}
