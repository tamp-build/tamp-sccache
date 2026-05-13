using System;
using System.Collections.Generic;
using System.Linq;
using Tamp;
using Tamp.Sccache;
using Xunit;

namespace Tamp.Sccache.Tests;

public sealed class SccacheTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/sccache"));

    // ─── Start: backend configurations ───────────────────────────────────

    [Fact]
    public void Start_Local_Backend_Sets_Env_Vars()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetLocal("/tmp/sccache", "10G")));
        Assert.Equal("--start-server", plan.Arguments[0]);
        Assert.Equal("/tmp/sccache", plan.Environment["SCCACHE_DIR"]);
        Assert.Equal("10G", plan.Environment["SCCACHE_CACHE_SIZE"]);
    }

    [Fact]
    public void Start_S3_Backend_Sets_Bucket_Region_Prefix()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetS3("my-bucket", "us-east-1", "rust/")));
        Assert.Equal("my-bucket", plan.Environment["SCCACHE_BUCKET"]);
        Assert.Equal("us-east-1", plan.Environment["SCCACHE_REGION"]);
        Assert.Equal("rust/", plan.Environment["SCCACHE_S3_KEY_PREFIX"]);
    }

    [Fact]
    public void Start_S3_Compatible_Endpoint()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetS3("bucket", "us-east-1")
            .SetS3Endpoint("https://minio.example.com")));
        Assert.Equal("https://minio.example.com", plan.Environment["SCCACHE_ENDPOINT"]);
    }

    [Fact]
    public void Start_S3_No_Credentials_Public_Bucket()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetS3("public-bucket", "us-east-1").SetS3NoCredentials()));
        Assert.Equal("true", plan.Environment["SCCACHE_S3_NO_CREDENTIALS"]);
    }

    [Fact]
    public void Start_AzureBlob_Backend_Reveals_Connection_String()
    {
        var conn = new Secret("azure_conn", "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=fake==");
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetAzureBlob("sccache", conn, "ci-runners/")));
        Assert.Equal("sccache", plan.Environment["SCCACHE_AZURE_BLOB_CONTAINER"]);
        Assert.Equal(
            "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=fake==",
            plan.Environment["SCCACHE_AZURE_CONNECTION_STRING"]);
        Assert.Equal("ci-runners/", plan.Environment["SCCACHE_AZURE_KEY_PREFIX"]);
        Assert.Contains(conn, plan.Secrets);  // Secret flows for masking
    }

    [Fact]
    public void Start_Gcs_Backend()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetGcs("my-bucket", "/path/to/sa.json", "READ_WRITE", "rust/")));
        Assert.Equal("my-bucket", plan.Environment["SCCACHE_GCS_BUCKET"]);
        Assert.Equal("/path/to/sa.json", plan.Environment["SCCACHE_GCS_KEY_PATH"]);
        Assert.Equal("READ_WRITE", plan.Environment["SCCACHE_GCS_RW_MODE"]);
        Assert.Equal("rust/", plan.Environment["SCCACHE_GCS_KEY_PREFIX"]);
    }

    [Fact]
    public void Start_Redis_Backend_With_Password()
    {
        var pwd = new Secret("redis_pwd", "redispwd");
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetRedis("redis://redis.example.com:6379", pwd, db: 2, ttlSeconds: 86400)));
        Assert.Equal("redis://redis.example.com:6379", plan.Environment["SCCACHE_REDIS"]);
        Assert.Equal("redispwd", plan.Environment["SCCACHE_REDIS_PASSWORD"]);
        Assert.Equal("2", plan.Environment["SCCACHE_REDIS_DB"]);
        Assert.Equal("86400", plan.Environment["SCCACHE_REDIS_EXPIRATION"]);
        Assert.Contains(pwd, plan.Secrets);
    }

    [Fact]
    public void Start_Memcached_Backend()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetMemcached("memcached.example.com:11211", ttlSeconds: 3600)));
        Assert.Equal("memcached.example.com:11211", plan.Environment["SCCACHE_MEMCACHED"]);
        Assert.Equal("3600", plan.Environment["SCCACHE_MEMCACHED_EXPIRATION"]);
    }

    [Fact]
    public void Start_GitHub_Actions_Cache_Backend()
    {
        var plan = Sccache.Start(FakeTool(), s => s.WithStorage(b => b
            .SetGitHubActionsCache("v2")));
        Assert.Equal("true", plan.Environment["SCCACHE_GHA_ENABLED"]);
        Assert.Equal("v2", plan.Environment["SCCACHE_GHA_VERSION"]);
    }

    [Fact]
    public void Start_Multiple_Backends_Throws_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sccache.Start(FakeTool(), s => s.WithStorage(b => b
                .SetLocal("/tmp/sccache")
                .SetS3("bucket", "us-east-1")))
            .Arguments.ToList());

        Assert.Throws<InvalidOperationException>(() =>
            Sccache.Start(FakeTool(), s => s.WithStorage(b => b
                .SetGitHubActionsCache()
                .SetMemcached("memcached:11211")))
            .Arguments.ToList());

        Assert.Throws<InvalidOperationException>(() =>
            Sccache.Start(FakeTool(), s => s.WithStorage(b => b
                .SetRedis("redis://x")
                .SetGcs("bucket")))
            .Arguments.ToList());
    }

    [Fact]
    public void Start_Without_Storage_Falls_Back_To_Default()
    {
        // sccache itself falls back to ~/.cache/sccache or %LOCALAPPDATA% — Tamp doesn't force a backend.
        var plan = Sccache.Start(FakeTool(), s => { });
        Assert.Single(plan.Arguments);
        Assert.Equal("--start-server", plan.Arguments[0]);
        Assert.Empty(plan.Environment);
    }

    [Fact]
    public void Start_Log_Level_And_Server_Port_And_Idle_Timeout()
    {
        var plan = Sccache.Start(FakeTool(), s => s
            .SetLogLevel("debug")
            .SetServerPort(4226)
            .SetIdleTimeoutSeconds(0));  // 0 = never timeout
        Assert.Equal("debug", plan.Environment["SCCACHE_LOG"]);
        Assert.Equal("4226", plan.Environment["SCCACHE_SERVER_PORT"]);
        Assert.Equal("0", plan.Environment["SCCACHE_IDLE_TIMEOUT"]);
    }

    [Fact]
    public void Start_Negative_Idle_Timeout_Rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sccache.Start(FakeTool(), s => s.SetIdleTimeoutSeconds(-1)).Arguments.ToList());
    }

    // ─── Stop / Stats / ZeroStats / Version ──────────────────────────────

    [Fact]
    public void Stop_Has_Verb()
    {
        var plan = Sccache.Stop(FakeTool());
        Assert.Equal(new[] { "--stop-server" }, plan.Arguments);
    }

    [Fact]
    public void Stats_Default_Is_Standard_Variant()
    {
        var plan = Sccache.Stats(FakeTool());
        Assert.Equal(new[] { "--show-stats" }, plan.Arguments);
    }

    [Fact]
    public void Stats_Advanced_Variant()
    {
        var plan = Sccache.Stats(FakeTool(), s => s.SetAdvanced());
        Assert.Equal(new[] { "--show-adv-stats" }, plan.Arguments);
    }

    [Fact]
    public void Stats_Json_Format()
    {
        var plan = Sccache.Stats(FakeTool(), s => s.SetJson());
        Assert.Equal(new[] { "--show-stats", "--stats-format", "json" }, plan.Arguments);
    }

    [Fact]
    public void Stats_Advanced_Json_Combined()
    {
        var plan = Sccache.Stats(FakeTool(), s => s.SetAdvanced().SetJson());
        Assert.Equal(new[] { "--show-adv-stats", "--stats-format", "json" }, plan.Arguments);
    }

    [Fact]
    public void ZeroStats_Has_Verb()
    {
        var plan = Sccache.ZeroStats(FakeTool());
        Assert.Equal(new[] { "--zero-stats" }, plan.Arguments);
    }

    [Fact]
    public void Version_Has_Verb()
    {
        var plan = Sccache.Version(FakeTool());
        Assert.Equal(new[] { "--version" }, plan.Arguments);
    }

    // ─── RustcWrapperEnv helper (the Tamp.Cargo integration point) ───────

    [Fact]
    public void RustcWrapperEnv_Default_Returns_Bare_Name()
    {
        var env = Sccache.RustcWrapperEnv();
        Assert.Equal("sccache", env["RUSTC_WRAPPER"]);
        Assert.Single(env);  // Only RUSTC_WRAPPER, nothing else
    }

    [Fact]
    public void RustcWrapperEnv_With_Absolute_Path()
    {
        var env = Sccache.RustcWrapperEnv("/usr/local/bin/sccache");
        Assert.Equal("/usr/local/bin/sccache", env["RUSTC_WRAPPER"]);
    }

    // ─── Raw escape hatch ─────────────────────────────────────────────────

    [Fact]
    public void Raw_Allows_Arbitrary_Args()
    {
        var plan = Sccache.Raw(FakeTool(), "rustc", "--crate-name", "foo");
        Assert.Equal(new[] { "rustc", "--crate-name", "foo" }, plan.Arguments);
    }

    [Fact]
    public void Raw_Rejects_Empty()
    {
        Assert.Throws<ArgumentException>(() => Sccache.Raw(FakeTool()));
    }

    // ─── WorkingDirectory + environment propagation ───────────────────────

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = Sccache.Stats(FakeTool(), s => s.SetWorkingDirectory("/repo"));
        Assert.Equal("/repo", plan.WorkingDirectory);
    }

    [Fact]
    public void Extra_Env_Vars_Pass_Through()
    {
        var plan = Sccache.Start(FakeTool(), s => s
            .SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "AKIA…")
            .WithStorage(b => b.SetS3("b", "r")));
        Assert.Equal("AKIA…", plan.Environment["AWS_ACCESS_KEY_ID"]);
    }
}
