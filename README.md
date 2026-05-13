# Tamp.Sccache

> Wrapper for [mozilla/sccache](https://github.com/mozilla/sccache) — a shared compilation cache that drops in transparently via `RUSTC_WRAPPER` (and `cc`/`c++` wrappers). Turns the cold-CI Rust-compile bottleneck into a warm-cache lookup. Pairs with [`Tamp.Cargo`](https://github.com/tamp-build/tamp-cargo) for the Rust toolchain side.

| Package | Status |
|---|---|
| `Tamp.Sccache` | 0.1.0 (initial) |

## Why this exists

Cargo's incremental-compile only helps within a single working tree. On a fresh CI runner, every PR pays the cold-rustc tax — compile every transitive dep from scratch. For a non-trivial Rust app like DasBook (Tauri shell + axum service + sqlite-bundled), that's the dominant CI wall-clock cost.

`sccache` solves this by caching `rustc` outputs keyed by source hash + flags + dep-tree fingerprint. With an S3 / Azure Blob / Redis backend, every runner shares the same cache: PR #501's compilation feeds PR #502's. Cold-cache compile drops from minutes to seconds for the parts that didn't actually change.

`Tamp.Sccache` makes this a typed step in the Tamp build graph:

- **Storage backends are typed** — `SetLocal(...)`, `SetS3(...)`, `SetAzureBlob(...)`, `SetGcs(...)`, `SetRedis(...)`, `SetMemcached(...)`, `SetGitHubActionsCache(...)`. Mutually exclusive (validated at `ToCommandPlan` time, not after the slow tool launches).
- **Secrets are masked** — `Secret`-typed Azure connection strings and Redis passwords flow through `CommandPlan.Secrets` so they don't appear in Tamp's printed process trace.
- **Daemon lifecycle is typed** — `Sccache.Start(...)`, `Sccache.Stop(...)`, `Sccache.Stats(...)`, `Sccache.ZeroStats(...)` for the daemon-control verbs.
- **Tamp.Cargo integration is one line** — `Sccache.RustcWrapperEnv()` returns the env-var dictionary that wires sccache as Cargo's rustc wrapper.

## Install

```bash
dotnet add package Tamp.Sccache
```

Multi-targets net8 / net9 / net10. Requires `Tamp.Core` ≥ **1.6.0** (which made `Secret.Reveal()` public + ships the TAMP004 analyzer — see [Tamp.Core 1.6.0 changelog](https://github.com/tamp-build/tamp/blob/main/CHANGELOG.md#160--2026-05-13)). No per-satellite `InternalsVisibleTo` entry is needed; the analyzer recognizes the `*Settings` / `*SettingsBase` class shape that Tamp.Sccache uses for the call sites that reveal Azure / Redis credentials.

## Tool installation

sccache is a single Go-style binary distributed via:

- **Cargo:** `cargo install sccache`
- **Brew:** `brew install sccache`
- **GitHub releases:** [mozilla/sccache/releases](https://github.com/mozilla/sccache/releases) (.tar.gz)
- **GitHub Actions:** [`mozilla-actions/sccache-action`](https://github.com/mozilla-actions/sccache-action) handles install + version-pinning

Pin the version in CI for reproducibility.

## Quick start — wiring sccache into a Cargo build

```csharp
using Tamp;
using Tamp.Cargo;
using Tamp.Sccache;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("cargo")]   readonly Tool Cargo = null!;
    [FromPath("sccache")] readonly Tool SccacheTool = null!;

    [Secret] readonly Secret AzureConn = null!;

    AbsolutePath ServiceCrate => RootDirectory / "dasbook-service";

    // ── Start the daemon with an Azure Blob backend (CI runners share the cache) ──
    Target StartCache => _ => _
        .Executes(() => Sccache.Start(SccacheTool, s => s
            .WithStorage(b => b.SetAzureBlob("sccache", AzureConn, "ci-rust/"))
            .SetIdleTimeoutSeconds(0)));   // never auto-stop in CI

    // Reset counters so Stats reflects only this build's hits/misses
    Target ResetCacheStats => _ => _
        .DependsOn(nameof(StartCache))
        .Executes(() => Sccache.ZeroStats(SccacheTool));

    // ── Build the Rust service with sccache wired as RUSTC_WRAPPER ──
    Target BuildService => _ => _
        .DependsOn(nameof(ResetCacheStats))
        .Executes(() =>
        {
            var env = Sccache.RustcWrapperEnv();    // { "RUSTC_WRAPPER": "sccache" }
            return Cargo.Build(s =>
            {
                s.SetWorkingDirectory(ServiceCrate)
                 .SetRelease()
                 .SetTarget("x86_64-pc-windows-msvc")
                 .SetLocked();
                foreach (var (k, v) in env) s.SetEnvironmentVariable(k, v);
            });
        });

    // Print stats at the end so CI logs show cache hit rate
    Target ShowCacheStats => _ => _
        .DependsOn(nameof(BuildService))
        .Executes(() => Sccache.Stats(SccacheTool, s => s.SetAdvanced()));
}
```

Run `dotnet tamp BuildService` once → cold cache, all crates compiled. Run again → 95%+ hit rate on the same machine. Run on a different runner with the same Azure Blob backend → still 95%+. The cache crosses runners, machines, and CI workflows.

## Backend matrix

| Backend | Setter | Env vars set | Best for |
|---|---|---|---|
| Local disk | `SetLocal(dir, cacheSize?)` | `SCCACHE_DIR`, `SCCACHE_CACHE_SIZE` | Single-developer machine, single-runner CI |
| Amazon S3 | `SetS3(bucket, region, keyPrefix?)` + optional `SetS3Endpoint(...)` / `SetS3NoCredentials()` | `SCCACHE_BUCKET`, `SCCACHE_REGION`, `SCCACHE_S3_KEY_PREFIX`, `SCCACHE_ENDPOINT`, `SCCACHE_S3_NO_CREDENTIALS` | Multi-runner CI on AWS; also works with MinIO / R2 via endpoint override |
| Azure Blob | `SetAzureBlob(container, Secret connString, keyPrefix?)` | `SCCACHE_AZURE_BLOB_CONTAINER`, `SCCACHE_AZURE_CONNECTION_STRING`, `SCCACHE_AZURE_KEY_PREFIX` | Azure-hosted CI (App Service / ADO pipelines) |
| Google Cloud Storage | `SetGcs(bucket, keyPath?, rwMode?, keyPrefix?)` | `SCCACHE_GCS_BUCKET`, `SCCACHE_GCS_KEY_PATH`, `SCCACHE_GCS_RW_MODE`, `SCCACHE_GCS_KEY_PREFIX` | GCP-hosted CI |
| Redis | `SetRedis(url, Secret? password, db?, ttlSeconds?)` | `SCCACHE_REDIS`, `SCCACHE_REDIS_PASSWORD`, `SCCACHE_REDIS_DB`, `SCCACHE_REDIS_EXPIRATION` | Self-hosted CI with a Redis pool |
| Memcached | `SetMemcached(endpoint, ttlSeconds?)` | `SCCACHE_MEMCACHED`, `SCCACHE_MEMCACHED_EXPIRATION` | Self-hosted CI with memcached |
| GitHub Actions cache | `SetGitHubActionsCache(version?)` | `SCCACHE_GHA_ENABLED=true`, `SCCACHE_GHA_VERSION` | GitHub Actions — uses the actions cache as backend, no separate infra |

Backends are **mutually exclusive** — set one, validation enforces this at `ToCommandPlan` time:

```csharp
// throws InvalidOperationException — two backends set
Sccache.Start(tool, s => s.WithStorage(b => b
    .SetLocal("/tmp/sccache")
    .SetS3("bucket", "us-east-1")));
```

## Verb surface

| Tamp method | sccache cli | Notes |
|---|---|---|
| `Sccache.Start(...)` | `sccache --start-server` | Backend storage knobs flow through env. `SetLogLevel("debug")`, `SetServerPort(int)`, `SetIdleTimeoutSeconds(int)`. |
| `Sccache.Stop(...)` | `sccache --stop-server` | Stop daemon. |
| `Sccache.Stats(...)` | `sccache --show-stats` / `--show-adv-stats` | `SetAdvanced()` for per-cacheable-op breakdown; `SetJson()` for machine-readable. |
| `Sccache.ZeroStats(...)` | `sccache --zero-stats` | Reset counters. Useful at build start so end-of-build stats reflect just this run. |
| `Sccache.Version(...)` | `sccache --version` | Diagnostic. |
| `Sccache.Raw(...)` | `sccache <anything>` | Escape hatch — including `sccache <compiler> <args>` for direct compiler-wrapper invocation. |

### `Sccache.RustcWrapperEnv()` — the load-bearing helper

```csharp
public static IReadOnlyDictionary<string, string> RustcWrapperEnv(string? sccacheExecutable = null);
```

Returns `{ "RUSTC_WRAPPER": "sccache" }`. Merge into any `Tamp.Cargo` settings:

```csharp
foreach (var (k, v) in Sccache.RustcWrapperEnv())
    cargoSettings.SetEnvironmentVariable(k, v);
```

Pass an absolute path to pin to a specific sccache binary (avoids PATH lookup at exec time):

```csharp
var env = Sccache.RustcWrapperEnv("/usr/local/bin/sccache");
```

## DasBook wishlist #3 — how this fits

DasBook flagged: *"Reproducibility / cache hits across Rust + Node (currently the npm `ci` and Rust deps caches are the slowest parts of the CI run)."*

Two distinct caches map to different solutions:

| Cache | Tool | Tamp coverage |
|---|---|---|
| **Rust compile output** (rustc artifacts) | sccache | **`Tamp.Sccache`** (this package) |
| **Rust deps** (`~/.cargo/registry`, `~/.cargo/git`) | `actions/cache` + `Cargo.lock` | Standard CI YAML — Tamp doesn't replace |
| **npm deps** (`~/.npm`, `node_modules/`) | `actions/cache` + `package-lock.json` | Standard CI YAML — Tamp doesn't replace |
| **Vite build output** (`.vite/`, `dist/`) | `actions/cache` or [Turbo](https://turbo.build) | [`Tamp.Turbo.V2`](https://github.com/tamp-build/tamp-turbo) handles the Turbo path |

The sccache satellite covers the highest-value gap: the rustc-compile bottleneck. Dep restoration and npm cache stay at the CI YAML layer (one `actions/cache` block per ecosystem).

## Secrets handling

Backend credentials that flow through env vars use `Tamp.Core.Secret`:

- `SetAzureBlob(container, Secret connectionString, ...)`
- `SetRedis(url, Secret? password, ...)`

These are `Reveal()`'d into the env dictionary at exec time, and the `Secret` instance is listed in `CommandPlan.Secrets` so Tamp's process trace masks the value in printed output.

`Tamp.Sccache` requires Tamp.Core ≥ 1.6.0 (which made `Reveal()` public; no `InternalsVisibleTo` dance needed anymore).

## Sibling packages

- [`Tamp.Cargo`](https://github.com/tamp-build/tamp-cargo) — Rust toolchain. The package sccache wraps for.
- [`Tamp.Turbo.V2`](https://github.com/tamp-build/tamp-turbo) — for the JS-side build-output cache (Turbo / Turborepo).

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md).

## License

MIT. See [LICENSE](LICENSE).
