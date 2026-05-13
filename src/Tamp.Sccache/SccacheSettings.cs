namespace Tamp.Sccache;

/// <summary>
/// Common shape shared by every <c>sccache</c> verb. sccache is configured almost entirely
/// through environment variables (storage backends, cache size, etc.); the CLI args are mostly
/// daemon-control verbs. Settings classes expose typed builders that emit the right env vars
/// rather than CLI flags.
/// </summary>
public abstract class SccacheSettingsBase
{
    /// <summary>Working directory for the spawned sccache process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables (storage backend config + any extras).</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Subclasses override to return their flag tokens (e.g. <c>["--start-server"]</c>).</summary>
    protected abstract IEnumerable<string> Flags { get; }

    /// <summary>Subclasses may collect additional positional/named args.</summary>
    protected virtual void AppendArguments(List<string> args) { }

    /// <summary>Subclasses may attach Secrets that are masked in the printed CommandPlan.</summary>
    protected virtual IReadOnlyList<Secret> CollectSecrets() => Array.Empty<Secret>();

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var args = new List<string>(Flags);
        AppendArguments(args);
        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = CollectSecrets(),
        };
    }
}

/// <summary>
/// Storage-backend configuration. Backends are mutually exclusive — pick exactly one of:
/// <c>Local</c>, <c>S3</c>, <c>AzureBlob</c>, <c>Gcs</c>, <c>Redis</c>, <c>Memcached</c>,
/// or <c>GitHubActionsCache</c>. Validation runs at <see cref="ApplyTo"/> time.
/// </summary>
public sealed class SccacheStorageConfig
{
    // Local
    public string? LocalDir { get; set; }
    public string? LocalCacheSize { get; set; }

    // S3
    public string? S3Bucket { get; set; }
    public string? S3Region { get; set; }
    public string? S3KeyPrefix { get; set; }
    public string? S3Endpoint { get; set; }       // for S3-compatible (MinIO etc.)
    public bool? S3UsePathStyle { get; set; }
    public bool? S3NoCredentials { get; set; }    // for public-read buckets

    // Azure Blob
    public Secret? AzureConnectionString { get; set; }
    public string? AzureBlobContainer { get; set; }
    public string? AzureBlobKeyPrefix { get; set; }

    // GCS
    public string? GcsBucket { get; set; }
    public string? GcsRwMode { get; set; }        // READ_ONLY | READ_WRITE
    public string? GcsKeyPath { get; set; }       // service account JSON path
    public string? GcsKeyPrefix { get; set; }

    // Redis
    public string? RedisUrl { get; set; }
    public Secret? RedisPassword { get; set; }
    public int? RedisDb { get; set; }
    public int? RedisTtlSeconds { get; set; }

    // Memcached
    public string? MemcachedEndpoint { get; set; }
    public int? MemcachedTtlSeconds { get; set; }

    // GitHub Actions cache
    public bool? GitHubActionsCacheEnabled { get; set; }
    public string? GitHubActionsCacheVersion { get; set; }

    public SccacheStorageConfig SetLocal(string dir, string? cacheSize = null) { LocalDir = dir; LocalCacheSize = cacheSize; return this; }
    public SccacheStorageConfig SetS3(string bucket, string region, string? keyPrefix = null) { S3Bucket = bucket; S3Region = region; S3KeyPrefix = keyPrefix; return this; }
    public SccacheStorageConfig SetS3Endpoint(string endpoint, bool usePathStyle = true) { S3Endpoint = endpoint; S3UsePathStyle = usePathStyle; return this; }
    public SccacheStorageConfig SetS3NoCredentials(bool v = true) { S3NoCredentials = v; return this; }
    public SccacheStorageConfig SetAzureBlob(string container, Secret connectionString, string? keyPrefix = null)
        { AzureBlobContainer = container; AzureConnectionString = connectionString; AzureBlobKeyPrefix = keyPrefix; return this; }
    public SccacheStorageConfig SetGcs(string bucket, string? keyPath = null, string? rwMode = null, string? keyPrefix = null)
        { GcsBucket = bucket; GcsKeyPath = keyPath; GcsRwMode = rwMode; GcsKeyPrefix = keyPrefix; return this; }
    public SccacheStorageConfig SetRedis(string url, Secret? password = null, int? db = null, int? ttlSeconds = null)
        { RedisUrl = url; RedisPassword = password; RedisDb = db; RedisTtlSeconds = ttlSeconds; return this; }
    public SccacheStorageConfig SetMemcached(string endpoint, int? ttlSeconds = null) { MemcachedEndpoint = endpoint; MemcachedTtlSeconds = ttlSeconds; return this; }
    public SccacheStorageConfig SetGitHubActionsCache(string? version = null) { GitHubActionsCacheEnabled = true; GitHubActionsCacheVersion = version; return this; }

    /// <summary>Count of backends that have any field set. Used to enforce mutual exclusion.</summary>
    internal int BackendCount =>
        (LocalDir is not null ? 1 : 0) +
        (S3Bucket is not null ? 1 : 0) +
        (AzureBlobContainer is not null ? 1 : 0) +
        (GcsBucket is not null ? 1 : 0) +
        (RedisUrl is not null ? 1 : 0) +
        (MemcachedEndpoint is not null ? 1 : 0) +
        (GitHubActionsCacheEnabled == true ? 1 : 0);

    /// <summary>Apply backend env vars to a target dictionary. Validates exactly-one-backend.</summary>
    internal void ApplyTo(Dictionary<string, string> env)
    {
        if (BackendCount > 1)
            throw new InvalidOperationException(
                "sccache backends are mutually exclusive — pick exactly one of Local / S3 / AzureBlob / Gcs / Redis / Memcached / GitHubActionsCache.");

        if (LocalDir is not null) env["SCCACHE_DIR"] = LocalDir;
        if (LocalCacheSize is not null) env["SCCACHE_CACHE_SIZE"] = LocalCacheSize;

        if (S3Bucket is not null) env["SCCACHE_BUCKET"] = S3Bucket;
        if (S3Region is not null) env["SCCACHE_REGION"] = S3Region;
        if (S3KeyPrefix is not null) env["SCCACHE_S3_KEY_PREFIX"] = S3KeyPrefix;
        if (S3Endpoint is not null) env["SCCACHE_ENDPOINT"] = S3Endpoint;
        if (S3UsePathStyle is bool ups) env["SCCACHE_S3_USE_SSL"] = ups ? "true" : "false";
        if (S3NoCredentials is true) env["SCCACHE_S3_NO_CREDENTIALS"] = "true";

        if (AzureBlobContainer is not null) env["SCCACHE_AZURE_BLOB_CONTAINER"] = AzureBlobContainer;
        if (AzureConnectionString is not null) env["SCCACHE_AZURE_CONNECTION_STRING"] = AzureConnectionString.Reveal();
        if (AzureBlobKeyPrefix is not null) env["SCCACHE_AZURE_KEY_PREFIX"] = AzureBlobKeyPrefix;

        if (GcsBucket is not null) env["SCCACHE_GCS_BUCKET"] = GcsBucket;
        if (GcsKeyPath is not null) env["SCCACHE_GCS_KEY_PATH"] = GcsKeyPath;
        if (GcsRwMode is not null) env["SCCACHE_GCS_RW_MODE"] = GcsRwMode;
        if (GcsKeyPrefix is not null) env["SCCACHE_GCS_KEY_PREFIX"] = GcsKeyPrefix;

        if (RedisUrl is not null) env["SCCACHE_REDIS"] = RedisUrl;
        if (RedisPassword is not null) env["SCCACHE_REDIS_PASSWORD"] = RedisPassword.Reveal();
        if (RedisDb is int rd) env["SCCACHE_REDIS_DB"] = rd.ToString();
        if (RedisTtlSeconds is int rt) env["SCCACHE_REDIS_EXPIRATION"] = rt.ToString();

        if (MemcachedEndpoint is not null) env["SCCACHE_MEMCACHED"] = MemcachedEndpoint;
        if (MemcachedTtlSeconds is int mt) env["SCCACHE_MEMCACHED_EXPIRATION"] = mt.ToString();

        if (GitHubActionsCacheEnabled is true) env["SCCACHE_GHA_ENABLED"] = "true";
        if (GitHubActionsCacheVersion is not null) env["SCCACHE_GHA_VERSION"] = GitHubActionsCacheVersion;
    }

    internal IReadOnlyList<Secret> CollectSecrets()
    {
        var list = new List<Secret>();
        if (AzureConnectionString is not null) list.Add(AzureConnectionString);
        if (RedisPassword is not null) list.Add(RedisPassword);
        return list;
    }
}

/// <summary>Settings for <c>sccache --start-server</c> — start the background daemon.</summary>
public sealed class SccacheStartSettings : SccacheSettingsBase
{
    public SccacheStorageConfig Storage { get; } = new();

    /// <summary>Compiler-error log level (<c>SCCACHE_LOG</c>): <c>error</c>, <c>warn</c>, <c>info</c>, <c>debug</c>, <c>trace</c>.</summary>
    public string? LogLevel { get; set; }

    /// <summary>Override the sccache listen address (<c>SCCACHE_SERVER_PORT</c>).</summary>
    public int? ServerPort { get; set; }

    /// <summary>Idle timeout before the daemon shuts itself down (<c>SCCACHE_IDLE_TIMEOUT</c>), seconds. <c>0</c> = never.</summary>
    public int? IdleTimeoutSeconds { get; set; }

    public SccacheStartSettings WithStorage(Action<SccacheStorageConfig> configure) { configure(Storage); return this; }
    public SccacheStartSettings SetLogLevel(string level) { LogLevel = level; return this; }
    public SccacheStartSettings SetServerPort(int port) { ServerPort = port; return this; }
    public SccacheStartSettings SetIdleTimeoutSeconds(int seconds) { IdleTimeoutSeconds = seconds; return this; }
    public SccacheStartSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }
    public SccacheStartSettings SetEnvironmentVariable(string name, string value) { EnvironmentVariables[name] = value; return this; }

    protected override IEnumerable<string> Flags => new[] { "--start-server" };

    protected override IReadOnlyList<Secret> CollectSecrets() => Storage.CollectSecrets();

    protected override void AppendArguments(List<string> args)
    {
        // sccache --start-server takes no positional args; all config flows through env.
        Storage.ApplyTo(EnvironmentVariables);
        if (LogLevel is not null) EnvironmentVariables["SCCACHE_LOG"] = LogLevel;
        if (ServerPort is int port) EnvironmentVariables["SCCACHE_SERVER_PORT"] = port.ToString();
        if (IdleTimeoutSeconds is int idle)
        {
            if (idle < 0) throw new InvalidOperationException($"IdleTimeoutSeconds must be >= 0; got {idle}.");
            EnvironmentVariables["SCCACHE_IDLE_TIMEOUT"] = idle.ToString();
        }
    }
}

/// <summary>Settings for <c>sccache --stop-server</c>.</summary>
public sealed class SccacheStopSettings : SccacheSettingsBase
{
    public SccacheStopSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }
    protected override IEnumerable<string> Flags => new[] { "--stop-server" };
}

/// <summary>Settings for <c>sccache --show-stats</c> / <c>--show-adv-stats</c>.</summary>
public sealed class SccacheStatsSettings : SccacheSettingsBase
{
    /// <summary>Use the advanced (per-cacheable-operation breakdown) stats variant.</summary>
    public bool Advanced { get; set; }

    /// <summary>Emit JSON instead of human-readable (<c>--stats-format json</c>).</summary>
    public bool Json { get; set; }

    public SccacheStatsSettings SetAdvanced(bool v = true) { Advanced = v; return this; }
    public SccacheStatsSettings SetJson(bool v = true) { Json = v; return this; }
    public SccacheStatsSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }

    protected override IEnumerable<string> Flags
        => Advanced ? new[] { "--show-adv-stats" } : new[] { "--show-stats" };

    protected override void AppendArguments(List<string> args)
    {
        if (Json) { args.Add("--stats-format"); args.Add("json"); }
    }
}

/// <summary>Settings for <c>sccache --zero-stats</c>.</summary>
public sealed class SccacheZeroStatsSettings : SccacheSettingsBase
{
    public SccacheZeroStatsSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }
    protected override IEnumerable<string> Flags => new[] { "--zero-stats" };
}

/// <summary>Settings for <c>sccache --version</c>.</summary>
public sealed class SccacheVersionSettings : SccacheSettingsBase
{
    public SccacheVersionSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }
    protected override IEnumerable<string> Flags => new[] { "--version" };
}
