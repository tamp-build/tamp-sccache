using System.Security.Cryptography;
using System.Text;

namespace Tamp.Sccache;

/// <summary>
/// Resolves a short, stable per-worker discriminator so N agents in N worktrees don't poison
/// each other's sccache cache (tamp-build/tamp#18, ADR 0019 Pillar 4 — correct-by-default).
///
/// Resolution: explicit <c>TAMP_WORKER_ID</c> (sanitized) → short hash of the worktree root path.
/// Both yield a filesystem- and cache-key-safe token that is stable for a given worktree/worker
/// and distinct across worktrees.
/// </summary>
internal static class WorkerScope
{
    /// <summary>
    /// A safe discriminator token (e.g. <c>w-a1b2c3d4e5</c>). <paramref name="getEnv"/> and
    /// <paramref name="worktree"/> are injectable so callers/tests get deterministic output.
    /// </summary>
    public static string Discriminator(Func<string, string?>? getEnv = null, string? worktree = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        var explicitId = getEnv("TAMP_WORKER_ID");
        if (!string.IsNullOrWhiteSpace(explicitId))
            return Sanitize(explicitId!);

        worktree ??= SafeWorktree();
        return "w-" + ShortHash(worktree);
    }

    /// <summary>Normalize an arbitrary id to a short, safe token (alphanumerics + <c>-</c>, lowercased, capped).</summary>
    private static string Sanitize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var token = sb.ToString().Trim('-');
        while (token.Contains("--")) token = token.Replace("--", "-");
        if (token.Length > 40) token = token[..40].Trim('-');
        return token.Length == 0 ? "w-unknown" : token;
    }

    /// <summary>First 10 hex chars of the SHA-256 of the normalized path — stable per worktree, collision-safe in practice.</summary>
    private static string ShortHash(string value)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..10];
    }

    private static string SafeWorktree()
    {
        // Prefer the tamp worktree root; fall back to the current directory off-repo.
        try { return TampBuild.RootDirectory.Value; }
        catch { }
        try { return Directory.GetCurrentDirectory(); }
        catch { return "unknown"; }
    }
}
