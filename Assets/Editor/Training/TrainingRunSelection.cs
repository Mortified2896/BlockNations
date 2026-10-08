using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

// New-run identity and saved-run selection are separate launch intentions.
internal static class TrainingRunSelection
{
    public static string ResolveId(string root, string requestedId, bool resume, int boardSize, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            if (resume) throw new ArgumentException("Enter the saved run ID to resume.");
            string prefix = "mac-" + boardSize + "x" + boardSize + "-" +
                utcNow.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string generated = prefix;
            for (int suffix = 2; Exists(root, generated); suffix++)
                generated = prefix + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            return generated;
        }

        string id = requestedId.Trim();
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,80}$"))
            throw new ArgumentException("Use letters, numbers, dash, or underscore for the run ID.");
        if (!resume && Exists(root, id))
            throw new InvalidOperationException("That run ID already exists. Start with an automatic ID, or resume the saved run.");
        return id;
    }

    private static bool Exists(string root, string id)
    {
        string path = Path.Combine(root, "runs", id);
        return Directory.Exists(path) || File.Exists(path);
    }
}
