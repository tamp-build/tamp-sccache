namespace Tamp.Sccache;

/// <summary>
/// Top-level facade for the [mozilla/sccache](https://github.com/mozilla/sccache) shared
/// compilation cache. sccache transparently caches <c>rustc</c> outputs (and <c>cc</c>/<c>c++</c>
/// outputs when configured as those wrappers) keyed by source-hash + compiler-flags +
/// dependency-hash, with pluggable backends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Integration with Tamp.Cargo:</b> the typical use pattern is to start the sccache daemon
/// at the top of your build and set <c>RUSTC_WRAPPER=sccache</c> on every cargo invocation:
/// <code>
/// var env = Sccache.RustcWrapperEnv();
/// Cargo.Build(s => s.SetEnvironmentVariables(env).SetTarget(...));
/// </code>
/// or just inline:
/// <code>
/// Cargo.Build(s => s.SetEnvironmentVariable("RUSTC_WRAPPER", "sccache"));
/// </code>
/// </para>
/// <para>
/// <b>Tool resolution:</b>
/// <code>
/// [FromPath("sccache")] readonly Tool Sccache = null!;
/// </code>
/// Install via <c>cargo install sccache</c>, <c>brew install sccache</c>, or download from
/// [releases](https://github.com/mozilla/sccache/releases).
/// </para>
/// </remarks>
public static class Sccache
{
    /// <summary><c>sccache --start-server</c> — start the daemon with the supplied storage backend.</summary>
    public static CommandPlan Start(Tool tool, Action<SccacheStartSettings> configure)
        => Run<SccacheStartSettings>(tool, configure);

    /// <summary><c>sccache --stop-server</c> — stop the daemon.</summary>
    public static CommandPlan Stop(Tool tool, Action<SccacheStopSettings>? configure = null)
        => Run<SccacheStopSettings>(tool, configure);

    /// <summary><c>sccache --show-stats</c> (or <c>--show-adv-stats</c>) — print cache hit/miss counters.</summary>
    public static CommandPlan Stats(Tool tool, Action<SccacheStatsSettings>? configure = null)
        => Run<SccacheStatsSettings>(tool, configure);

    /// <summary><c>sccache --zero-stats</c> — reset counters (useful at build start).</summary>
    public static CommandPlan ZeroStats(Tool tool, Action<SccacheZeroStatsSettings>? configure = null)
        => Run<SccacheZeroStatsSettings>(tool, configure);

    /// <summary><c>sccache --version</c> — diagnostic.</summary>
    public static CommandPlan Version(Tool tool, Action<SccacheVersionSettings>? configure = null)
        => Run<SccacheVersionSettings>(tool, configure);

    /// <summary>
    /// Helper: the canonical env-var dictionary that tells Cargo to route <c>rustc</c> through
    /// <c>sccache</c>. Merge into your <see cref="Tool"/>'s settings:
    /// <code>
    /// foreach (var (k, v) in Sccache.RustcWrapperEnv())
    ///     cargoSettings.SetEnvironmentVariable(k, v);
    /// </code>
    /// </summary>
    /// <param name="sccacheExecutable">Optional absolute path to the sccache binary. Defaults to the bare name <c>sccache</c> for PATH-resolution.</param>
    public static IReadOnlyDictionary<string, string> RustcWrapperEnv(string? sccacheExecutable = null)
        => new Dictionary<string, string>
        {
            ["RUSTC_WRAPPER"] = sccacheExecutable ?? "sccache",
        };

    /// <summary>Raw escape hatch.</summary>
    public static CommandPlan Raw(Tool tool, params string[] arguments)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one argument.", nameof(arguments));
        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = arguments.ToList(),
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = tool.WorkingDirectory,
            Secrets = Array.Empty<Secret>(),
        };
    }

    // ---- Object-init overloads (TAM-161) ----
    // Parallel surface to the fluent verbs above. Both styles produce identical
    // CommandPlans; fluent stays canonical in docs and `tamp init` templates.
    public static CommandPlan Start(Tool tool, SccacheStartSettings settings) => Plan(tool, settings);
    public static CommandPlan Stop(Tool tool, SccacheStopSettings settings) => Plan(tool, settings);
    public static CommandPlan Stats(Tool tool, SccacheStatsSettings settings) => Plan(tool, settings);
    public static CommandPlan ZeroStats(Tool tool, SccacheZeroStatsSettings settings) => Plan(tool, settings);
    public static CommandPlan Version(Tool tool, SccacheVersionSettings settings) => Plan(tool, settings);

    private static CommandPlan Run<T>(Tool tool, Action<T>? configure) where T : SccacheSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : SccacheSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }
}
