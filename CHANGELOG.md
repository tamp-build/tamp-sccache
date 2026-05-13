# Changelog

All notable changes to **Tamp.Sccache** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-05-13

### Added

- Initial release. Wraps [mozilla/sccache](https://github.com/mozilla/sccache) —
  the shared compilation cache that drops in transparently via
  `RUSTC_WRAPPER`. Filed under TAM-193. Addresses DasBook wishlist #3 for the
  Rust-compile-output side (the dep-restore and Vite-build-output sides stay
  with `actions/cache` + `Tamp.Turbo.V2` respectively).

#### Daemon lifecycle verbs

- **`Sccache.Start(...)`** — `sccache --start-server`. Backend storage knobs +
  log level + server port + idle timeout.
- **`Sccache.Stop(...)`** — `sccache --stop-server`.
- **`Sccache.Stats(...)`** — `sccache --show-stats` (default) or
  `--show-adv-stats` (`.SetAdvanced()`). `SetJson()` for machine-readable
  output.
- **`Sccache.ZeroStats(...)`** — `sccache --zero-stats`. Useful at build start
  so end-of-build stats reflect only this run.
- **`Sccache.Version(...)`** — diagnostic.
- **`Sccache.Raw(...)`** — escape hatch (covers `sccache <compiler> <args>`
  direct-wrapper invocations).

#### Storage backends (mutually exclusive)

Backend configuration flows through env vars (sccache's actual API surface for
backend selection is environmental, not CLI flags). Mutual exclusion is
validated at `ToCommandPlan(...)` time — `InvalidOperationException` with a
message naming the exclusion.

- **Local disk** — `SetLocal(dir, cacheSize?)` →
  `SCCACHE_DIR`, `SCCACHE_CACHE_SIZE`.
- **Amazon S3** (and S3-compatible like MinIO / R2) —
  `SetS3(bucket, region, keyPrefix?)` + optional `SetS3Endpoint(...)`,
  `SetS3NoCredentials(...)`.
- **Azure Blob** — `SetAzureBlob(container, Secret connectionString, keyPrefix?)`.
  Connection string is `Secret`-typed and masked in CommandPlan trace.
- **Google Cloud Storage** —
  `SetGcs(bucket, keyPath?, rwMode?, keyPrefix?)`.
- **Redis** — `SetRedis(url, Secret? password, db?, ttlSeconds?)`.
  Password is `Secret`-typed.
- **Memcached** — `SetMemcached(endpoint, ttlSeconds?)`.
- **GitHub Actions cache** — `SetGitHubActionsCache(version?)` —
  uses the GHA cache as the storage backend with no separate infra.

#### Helper

- **`Sccache.RustcWrapperEnv(sccacheExecutable?)`** — returns the canonical
  env-var dict `{ "RUSTC_WRAPPER": "sccache" }` to merge into any `Tamp.Cargo`
  settings. The one-liner that turns "sccache is installed" into "sccache is
  actually wired into your build".

### Requires

- **Tamp.Core ≥ 1.6.0.** Tamp.Core 1.6.0 (TAM-196) made `Secret.Reveal()` public
  and shipped the TAMP004 analyzer that flags non-approved Reveal sites. Older
  Tamp.Core versions used per-satellite `InternalsVisibleTo` entries; this
  satellite shipped under the new regime — no IVT entry required, just the
  canonical `*Settings` class shape that the TAMP004 analyzer recognizes as
  approved.

### Validation

- Multiple-backend configurations rejected with a message naming the constraint.
- `IdleTimeoutSeconds >= 0` validated.

### Tests

- 26 unit tests covering positive paths plus negative cases (mutual exclusion,
  range validation, Secret flow-through to `CommandPlan.Secrets`).

### Notes

- Fifth non-.NET-ish satellite, following Cargo → Tauri.V2 → Msix →
  MicrosoftStoreCli. First infrastructure-tier satellite (not a "do a verb"
  wrapper — more a "configure a daemon" wrapper). Pattern still mirrors the
  others: settings classes, fluent setters, facade, `Raw` escape hatch,
  multi-target net8/9/10.

- This satellite covers the Rust-compile side of DasBook wishlist #3.
  Other parts of that wishlist line (dep-restore, Vite build output) stay at
  the CI YAML layer (`actions/cache`) or with `Tamp.Turbo.V2` respectively —
  Tamp.Sccache doesn't try to be a build-system replacement, just a
  drop-in compile cache for the slowest part of a Rust CI run.
