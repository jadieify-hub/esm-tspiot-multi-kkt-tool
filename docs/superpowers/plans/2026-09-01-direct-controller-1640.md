# Direct Controller 1.6.4.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register every KKT through deterministic ATOL VCOM sessions, then run one direct official ESM LM controller service per KKT without persistent KRS supervisor or managed Erlang processes.

**Architecture:** Registration and controller provisioning are independent phases. The first KKT uses the verified official `esm-lm-controller`; later KKT use direct SCM clones of the verified 1.6.4.0 `lmcontroller.exe`, each with an isolated ProgramData profile and stable ports. A one-shot elevated helper owns service/config mutations and legacy cleanup; the operator application owns ESM HTTP binding.

**Tech Stack:** C# 5-compatible shared/net48 code, .NET 8 operator tests, Windows SCM/registry APIs, WinForms, PowerShell release gates.

**Spec:** Approved conversation design dated 2026-09-01; this file is its executable checklist.

## Global Constraints

- Support only installed product `ЕСП Контроллер ЛМ ЧЗ` version `1.6.4.0`.
- Pin controller binary length `14668016`, SHA-256 `0A25B29A39B100FE461EB3FFA06A6B18F2B474F337EFDBA9F7A9CA89740FFD0A`, PE `Amd64`, and the existing JSC ESP signer/thumbprint.
- Preserve the working sequential VCOM registration flow and close every lease in `finally`.
- Do not ship vendor binaries, add an application installer, push, or modify `config-orchestrator.yml`.
- Do not persist ESM credentials or log YAML secrets.
- No production `Process.Kill`; native termination is confined to the ownership-proven legacy terminator and a narrow safety-gate allowlist.
- Legacy and helper production code must compile with C# 5.

---

### Task 1: Reproducible baseline and 1.6.4.0 capability

- [x] Add a shared build-tool resolver and one release-matrix entry point that finds MSBuild through `vswhere` and net48 csc by full system path.
- [x] Add failing tests for the exact 1.6.4.0 installed-product, binary, signer, Amd64, empty file metadata, and `SERVICE_SID_TYPE_NONE` contract.
- [x] Implement the 1.6.4.0 capability and reject the old 1.6.3.2 binary even when the service path matches.
- [x] Run shared/helper tests and C# 5 builds; commit.

### Task 2: Direct assignments, profiles, and CA

- [x] Add failing tests for stable assignments, first/base role, foreign service/port conflicts, ordinal gaps, and exhaustion.
- [x] Implement the direct-controller models and planner.
- [x] Add failing tests that copy only `ca.crt`/`ca.pem`, replace owned read-only CA atomically, and never clone `server.*` or the official config.
- [x] Port only the trusted-CA behavior from `b3c07c7`/`66f3ef2`, capability-driven for 1.6.4.0; commit.

### Task 3: Protocol v2, manifests, and direct SCM services

- [ ] Add failing tests for schema v2, canonical hashes, no arbitrary paths/commands/secrets, and pre-launch main/helper mismatch rejection.
- [ ] Implement `IDirectControllerProvisioner`, protocol v2 requests/results, and hash verification.
- [ ] Add failing tests for per-service Environment, SID None, direct vendor ImagePath, listener ownership, and hash-guarded backup/restore.
- [ ] Implement direct manifests, protected profiles/backups, read-only nonsecret inventory, SCM clone creation, restart, readiness, and removal; commit.

### Task 4: Legacy migration without persistent KRS processes

- [ ] Add failing tests for complete ownership proof, PID reuse, foreign-process refusal, cancellation, partial failure, and cleanup retry.
- [ ] Implement graceful service cleanup plus `VerifiedLegacyProcessIdentity` and the isolated native terminator.
- [ ] Narrowly allowlist only that terminator in `verify_lm_safety.ps1`; keep all other termination patterns forbidden.
- [ ] Prove no app-owned supervisor/Erlang processes or locked runtime directories remain; commit.

### Task 5: Per-instance ESM configuration and secret handling

- [ ] Add failing tests for structural discovery of the unique `settings.ldbControl` node, indentation/order variations, duplicate/anchor rejection, and absent instance config deferral.
- [ ] Add failing tests for YAML password masking, protected backup ACLs, no YAML content in logs/manifests/diagnostics, and package exclusions.
- [ ] Implement polling after registration, atomic per-instance patching, sequential service restart, rollback, and hash-guarded restore without touching the orchestrator or dkkt port.
- [ ] Extend masking for YAML scalar/block values and keep backups SYSTEM/Administrators-only; commit.

### Task 6: Independent automatic flow and UI

- [ ] Add failing tests for one-KKT regression, multi-INN registration, controller deferral, per-KKT failure continuation, and 2025/2055 warning semantics.
- [ ] Split automatic setup into registration then controller phases and bind ESM to controller gRPC ports `50063+` with transient admin/admin credentials and omitted empty `newPassword`.
- [ ] Replace the LM-complete-stack UI with controller inventory/actions and honest cleanup wording; commit.

### Task 7: Integration, documentation, and release

- [ ] Add an elevated sandbox gate that creates two high-ordinal direct clones, verifies distinct vendor PIDs/listeners and generated `server.*`, then restores a clean baseline in `finally`.
- [ ] Update README and field acceptance for ESM/controller 1.6.4.0 and future LM ports `5995+`.
- [ ] Run the complete release matrix, LM/UI/security gates, Modern build, compact package, and seven-file content verification.
- [ ] Produce the unpacked field folder, ZIP, and SHA-256 values; review the branch without pushing.
