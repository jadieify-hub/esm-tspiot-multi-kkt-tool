# Local Module MSI Clones Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a complete automatic workflow that keeps the working KKT registration and direct controllers, installs one verified LM CHZ per unique INN through the official base MSI plus independent MSI clones, and removes every app-created artifact without persistent KRS supervisor processes.

**Architecture:** The operator application builds a secret-free, immutable plan and sends it to the one-shot elevated helper. The helper verifies the exact signed source MSI, creates and fully verifies a short-lived clone MSI in protected staging, installs it through Windows Installer APIs, verifies vendor services/config/listeners/firewall, then deletes staging. Direct controllers remain one per KKT, but their LM target port is supplied by the INN group rather than inferred from controller ordinal.

**Tech Stack:** C# 5-compatible .NET Framework 4.8 helper and shared code, .NET 8/net48 WinForms, Windows Installer API, WiX DTF 4.0.6 (`WixToolset.Dtf.WindowsInstaller.Package`, MS-RL), Windows SCM/registry/firewall APIs, PowerShell release and elevated-sandbox gates.

**Spec:** `docs/superpowers/specs/2026-09-02-local-module-msi-clones-design.md`

## Global Constraints

- Preserve sequential ATOL VCOM registration and always release each KKT lease in `finally`.
- Preserve direct controller 1.6.4.0 trust profile and services `esm-lm-controller`, `esm-lm-controller-N`.
- Support only exact source `regime-2.6.1-7.msi`: length `51007488`, SHA-256 `68A9633CEFC912C2C1DEFAE40D1C8F433BB794EE66060B822F0895410AB6C5C6`, ProductCode `{556FD8AD-43A3-4645-BC54-EBF3043ADF82}`, UpgradeCode `{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}`, signer thumbprint `6BA5F6BBE4BE27658253C78889334D0E24858C19`.
- Pin WiX DTF packages to `4.0.6`; do not upgrade to a later licensing generation as part of this feature.
- Never commit, package, archive, log, or retain source/output MSI, vendor runtime, cookie, password, token, private key, database, or real customer config.
- Generated MSI exists only under protected ProgramData staging and is deleted in `finally`; recovery deletes only an exact journal-owned workspace.
- API binds `0.0.0.0`; CouchDB binds `127.0.0.1`; firewall permits only the API port on Domain/Private for `LocalSubnet` or a supplied IP/CIDR.
- Base ports come from actual config. Clone 1 is API `6995`, DB `7984`; later clones add `1000`.
- Default clone volume equals the actual base-LM volume; only a fixed local drive and its `Program Files` directory are accepted.
- A pre-existing base LM is `PreExisting` and must survive “remove created”; only an operation-journal-proven base install may be uninstalled automatically.
- Business initialization never gates KKT registration, direct-controller creation, ESM binding, or later INN groups.
- No production `Process.Kill`, `taskkill`, `sc.exe`, `cmd.exe`, PowerShell child process, remote template, or broad ACL reset.
- Shared/helper/Legacy production code must compile with C# 5; both WinForms targets must remain functional.

---

## Phase A — MSI engine and deterministic plan

### Task 1: Pin DTF and make the helper dependency closure reproducible

**Files:**
- Modify: `src/EsmTspiot.ServiceProvisioner/EsmTspiot.ServiceProvisioner.csproj`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/EsmTspiot.ServiceProvisioner.Tests.csproj`
- Modify: `build/EsmTspiot.Provisioner.targets`
- Modify: `scripts/package_compact_release.ps1`
- Modify: `scripts/verify_compact_security_contract.ps1`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**
- Produces: helper-local DTF assemblies `WixToolset.Dtf.WindowsInstaller.dll`, `WixToolset.Dtf.WindowsInstaller.Package.dll`, `WixToolset.Dtf.Compression.dll`, `WixToolset.Dtf.Compression.Cab.dll`.
- Preserves: main/helper SHA pin and identical `EsmTspiot.Shared.dll` check.

- [x] **Step 1: Write the failing closure test**

Add `DtfDependencyClosureIsExactAndVendorFree` to the provisioner test runner. It must assert that the helper output contains the four exact DTF DLL names above, contains no MSI/vendor file, and the file version metadata is `4.0.6.0`.

- [x] **Step 2: Run the helper tests and verify the new test fails**

Run:

```powershell
. .\scripts\build_tools.ps1
$msbuild = Get-KrsMSBuildPath
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /t:Build /p:Configuration=Debug /v:minimal
& .\tests\EsmTspiot.ServiceProvisioner.Tests\bin\Debug\net48\EsmTspiot.ServiceProvisioner.Tests.exe
```

Expected: FAIL because the DTF assemblies are not in the helper closure.

- [x] **Step 3: Add the pinned package and copy exact runtime dependencies**

Add to both helper and helper-test projects:

```xml
<PackageReference Include="WixToolset.Dtf.WindowsInstaller.Package" Version="4.0.6" />
```

Change `EsmTspiot.Provisioner.targets` so `CopyProvisionerClosure` and publish copy the helper EXE, shared DLL, and the four exact DTF DLLs. Change the compact packager’s `helperClosure` and expected layout from a two-file list to that exact six-file helper closure. Keep `.msi` forbidden.

- [x] **Step 4: Run helper tests and compact-contract verification**

Run the command from Step 2, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1 -SkipPackage
```

Expected: all existing tests/gates PASS and the new closure test PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner/EsmTspiot.ServiceProvisioner.csproj tests/EsmTspiot.ServiceProvisioner.Tests/EsmTspiot.ServiceProvisioner.Tests.csproj build/EsmTspiot.Provisioner.targets scripts/package_compact_release.ps1 scripts/verify_compact_security_contract.ps1 tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Pin DTF runtime for local module MSI work"
```

### Task 2: Model one LM per INN and decouple controller ordinal from LM port

**Files:**
- Create: `src/EsmTspiot.Shared/Models/LocalModuleMsiAssignment.cs`
- Create: `src/EsmTspiot.Shared/Models/LocalModuleBaseInventory.cs`
- Create: `src/EsmTspiot.Shared/Models/LocalModuleMsiPlan.cs`
- Create: `src/EsmTspiot.Shared/Models/LocalModuleMsiProvisioningItemRequest.cs`
- Create: `src/EsmTspiot.Shared/Models/LocalModuleMsiProvisioningItemResult.cs`
- Create: `src/EsmTspiot.Shared/Services/LocalModuleMsiIdentity.cs`
- Create: `src/EsmTspiot.Shared/Services/LocalModuleMsiPlanner.cs`
- Create: `src/EsmTspiot.Shared/Services/LocalModuleInstallRootPolicy.cs`
- Create: `src/EsmTspiot.Shared/Services/LocalModuleDiskSpacePolicy.cs`
- Modify: `src/EsmTspiot.Shared/Models/DirectControllerAssignment.cs`
- Modify: `src/EsmTspiot.Shared/Models/DirectControllerProvisioningItemRequest.cs`
- Modify: `src/EsmTspiot.Shared/Services/DirectControllerPlanner.cs`
- Modify: `src/EsmTspiot.Shared/Services/DirectControllerIdentity.cs`
- Modify: `src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/DirectControllerManifest.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/DirectControllerManifestStore.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/DirectControllerProfileStore.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ProvisioningRequestValidator.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/WindowsDirectControllerPlatform.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/DirectControllerOperatorInventoryReader.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.DirectControllers.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class LocalModuleMsiAssignment
{
    public string Inn { get; set; }
    public int CloneOrdinal { get; set; } // 0 = official base, 1..31 = clones
    public int ApiPort { get; set; }
    public int DatabasePort { get; set; }
    public string InstallVolumeRoot { get; set; } // canonical fixed root, e.g. "D:\\"
    public bool BaseWasPreExisting { get; set; }
}

public static LocalModuleMsiPlanner
{
    public static LocalModuleMsiPlan Build(
        IList<LmGatewayKkt> kkts,
        IList<LocalModuleMsiAssignment> saved,
        LocalModuleBaseInventory baseInventory,
        string requestedVolumeRoot,
        IList<TcpListenerSnapshotItem> listeners);
}

[DataContract]
public sealed class LocalModuleMsiProvisioningItemRequest
{
    [DataMember(Order = 1)] public string Inn { get; set; }
    [DataMember(Order = 2)] public int CloneOrdinal { get; set; }
    [DataMember(Order = 3)] public int ApiPort { get; set; }
    [DataMember(Order = 4)] public int DatabasePort { get; set; }
    [DataMember(Order = 5)] public string InstallVolumeRoot { get; set; }
    [DataMember(Order = 6)] public string RemoteAddress { get; set; }
    [DataMember(Order = 7)] public string ExpectedManifestSha256 { get; set; }
}

[DataContract]
public sealed class LocalModuleMsiProvisioningItemResult
{
    [DataMember(Order = 1)] public string Inn { get; set; }
    [DataMember(Order = 2)] public int CloneOrdinal { get; set; }
    [DataMember(Order = 3)] public int ApiPort { get; set; }
    [DataMember(Order = 4)] public LmServiceProvisioningStatus Status { get; set; }
    [DataMember(Order = 5)] public string Message { get; set; }
    [DataMember(Order = 6)] public string ManifestSha256 { get; set; }
}
```

- Changes `DirectControllerAssignment.FutureLocalModulePort` to `TargetLocalModulePort` and requires `DirectControllerProvisioningItemRequest.TargetLocalModulePort`.
- Changes the planner entry point to accept the INN mapping explicitly:

```csharp
public static DirectControllerPlan Build(
    IList<LmGatewayKkt> kkts,
    IList<DirectControllerAssignment> savedAssignments,
    IList<DirectControllerServiceInventoryItem> services,
    IList<TcpListenerSnapshotItem> listeners,
    IDictionary<string, int> targetLmPortsByInn);
```

- [x] **Step 1: Write failing shared tests for grouping, ports, roots, and controller targets**

Add tests proving:

```csharp
// Two KKT, same INN: two controllers, one LM/API target.
AssertEqual(1, lmPlan.Assignments.Count);
AssertEqual(5995, controllers.Assignments[0].TargetLocalModulePort);
AssertEqual(5995, controllers.Assignments[1].TargetLocalModulePort);
AssertNotEqual(controllers.Assignments[0].GrpcPort, controllers.Assignments[1].GrpcPort);

// Different INN: base 5995/5984, clone 6995/7984.
// Base on D: => default clone root D:\, never hard-coded C:\.
// UNC, mapped/network, reparse, relative and non-Program-Files roots are rejected.
// Install volume reserves 256 MiB per new clone + 128 MiB headroom.
// System volume reserves 384 MiB transient staging + 64 MiB per new MSI cache entry
// + 128 MiB headroom; both observed/required values are returned for UI and recheck.
```

Add helper tests proving `DirectControllerProfileStore` writes the explicit target port supplied by the INN assignment and safely upgrades an owned old manifest whose target was previously derived from controller ordinal.

- [x] **Step 2: Run shared and helper tests and verify failures**

Run:

```powershell
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Debug
. .\scripts\build_tools.ps1
$msbuild = Get-KrsMSBuildPath
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /t:Build /p:Configuration=Debug /v:minimal
& .\tests\EsmTspiot.ServiceProvisioner.Tests\bin\Debug\net48\EsmTspiot.ServiceProvisioner.Tests.exe
```

Expected: FAIL because `TargetLocalModulePort` and the MSI planner do not exist.

- [x] **Step 3: Implement the minimal planner and root policy**

Implement explicit identity methods:

```csharp
public static int ApiPortForClone(int ordinal)
{
    if (ordinal < 1 || ordinal > 31) throw new ArgumentOutOfRangeException("ordinal");
    return 5995 + (1000 * ordinal);
}

public static int DatabasePortForClone(int ordinal)
{
    if (ordinal < 1 || ordinal > 31) throw new ArgumentOutOfRangeException("ordinal");
    return 6984 + (1000 * ordinal);
}
```

Use actual base config ports for ordinal 0. Allocate saved ordinals first, then the first free ordinal; group strictly by normalized INN. Pass the resulting `ApiPort` into every controller assignment of that INN.

- [x] **Step 4: Run tests and C# 5 builds**

Run the Step 2 commands, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1 -SkipPackage
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.Shared src/EsmTspiot.ServiceProvisioner/DirectControllerManifest.cs src/EsmTspiot.ServiceProvisioner/DirectControllerManifestStore.cs src/EsmTspiot.ServiceProvisioner/DirectControllerProfileStore.cs src/EsmTspiot.ServiceProvisioner/ProvisioningRequestValidator.cs src/EsmTspiot.ServiceProvisioner/WindowsDirectControllerPlatform.cs src/EsmTspiot.WinForms.Shared/DirectControllerOperatorInventoryReader.cs src/EsmTspiot.WinForms.Shared/LmGatewayPage.DirectControllers.cs tests/EsmTspiot.Shared.Tests/Program.cs tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Plan MSI local modules by INN"
```

### Task 3: Expand exact source-MSI verification from four properties to a structural profile

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiCapabilityProfile.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiCapabilityRegistry.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiDatabaseSnapshot.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiProfileReader.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/Profiles/local-module-msi-2.6.1-7-profile.json` (embedded canonical profile, linked into the test output as the sanitized fixture)
- Modify: `src/EsmTspiot.ServiceProvisioner/EsmTspiot.ServiceProvisioner.csproj`
- Modify: `src/EsmTspiot.ServiceProvisioner/WindowsInstallerPackageReader.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LocalModulePackageVerifier.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/EsmTspiot.ServiceProvisioner.Tests.csproj`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal interface ILocalModuleMsiProfileReader
{
    LocalModuleMsiDatabaseSnapshot Read(string lockedMsiPath);
}

internal sealed class LocalModuleMsiDatabaseSnapshot
{
    internal int FileRowCount { get; set; }
    internal int MsiFileHashRowCount { get; set; }
    internal IList<MsiMediaSnapshot> Media { get; private set; }
    internal IList<MsiProfileMismatch> Compare(LocalModuleMsiCapabilityProfile expected);
}

internal sealed class LocalModuleMsiCapabilityRegistry
{
    internal LocalModuleMsiCapabilityProfile FindExact(
        WindowsInstallerPackageMetadata metadata,
        TrustedFileExpectation trust);
}
```

- [x] **Step 1: Add failing profile tests**

Tests must accept the sanitized exact profile and reject one mutation at a time: Product/Package/Upgrade identity, `2246` File rows, `2226` MsiFileHash rows, Media names/LastSequence, required Directory, Registry, RegLocator/AppSearch, five config files, quoted service custom actions, `InstallAutoApdater`, and `StopEPMD`. Assert the exception names the table/key mismatch but contains no source path or secret property value. A registry test must return an explicit unsupported-profile result for an unknown exact identity without weakening the known profile.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because only four MSI properties are currently read.

- [x] **Step 3: Implement read-only DTF queries and exact comparison**

Open with `new Database(path, DatabaseOpenMode.ReadOnly)`. Read only allowlisted columns and normalize them into immutable snapshots. Keep existing Authenticode/hash lock before structural reads. Return all safe mismatches, for example `Media:#Disk1.cab LastSequence expected 2247 observed 2246`; never dump full table rows.

- [x] **Step 4: Run helper tests and safety gate**

Run the Task 1 helper-test command and:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify_lm_safety.ps1
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests
git commit -m "Verify the exact local module MSI schema"
```

### Task 4: Create deterministic clone identities and apply only profiled MSI edits

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiCloneIdentity.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiIdentityFactory.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiTransformPlan.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/TransformedLocalModuleMsi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiTransformer.cs`
- Create: `tests/EsmTspiot.ServiceProvisioner.Tests/MsiTestPackageFactory.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleMsiCloneIdentity
{
    internal string Inn { get; set; }
    internal int CloneOrdinal { get; set; }
    internal Guid ProductCode { get; set; }   // stable for version + INN
    internal Guid UpgradeCode { get; set; }   // stable for logical INN instance
    internal Guid PackageCode { get; set; }   // fresh per generated package
    internal string ProductName { get; set; }
    internal string InstallDirectoryName { get; set; }
    internal string ApiServiceName { get; set; }
    internal string DatabaseServiceName { get; set; }
}

internal interface ILocalModuleMsiTransformer
{
    TransformedLocalModuleMsi Transform(
        VerifiedLocalModulePackage source,
        LocalModuleMsiTransformPlan plan,
        string outputPath);
}
```

- [x] **Step 1: Build a minimal synthetic two-CAB MSI fixture and failing transform tests**

The factory creates a temporary MSI with the same relevant table shapes, two embedded CAB streams, quoted `"regime"`/`"yenisei"` commands, RegLocator, service names, and five small config files. Tests assert unique ProductCode/UpgradeCode/PackageCode, `RegimeN`, `regimeN`, `yeniseiN`, node names, ports, RegLocator path, disabled updater and clone `StopEPMD`.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because transformer and identity factory do not exist.

- [x] **Step 3: Implement stable IDs and allowlisted table updates**

Use an injected `Func<Guid>` only for PackageCode. Derive ProductCode and UpgradeCode using SHA-256 over fixed namespace bytes plus normalized version/INN, set RFC-4122 variant/version bits, and format uppercase braced GUIDs. Reject any SQL update count different from the exact capability profile. Never perform global string replacement.

- [x] **Step 4: Run helper tests twice to prove deterministic Product/Upgrade identity and fresh PackageCode**

Run the Task 1 helper-test command twice.

Expected: both PASS; stable IDs repeat, injected PackageCode changes.

- [ ] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests
git commit -m "Transform exact local module MSI clones"
```

### Task 5: Rebuild both CAB streams and verify every output byte relationship

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleCabinetRebuilder.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiOutputVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/VerifiedTransformedLocalModuleMsi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/MsiFileHashCalculator.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiTransformer.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/MsiTestPackageFactory.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleMsiOutputVerifier
{
    internal VerifiedTransformedLocalModuleMsi Verify(
        LocalModuleMsiDatabaseSnapshot source,
        string transformedMsiPath,
        LocalModuleMsiTransformPlan plan);
}
```

- [x] **Step 1: Add failing byte-level tests**

Cover: unchanged file byte mutation, unexpected new/missing file, wrong FileSize, wrong MsiFileHash part, `#media1.cab` LastSequence drift, `#Disk1.cab`/sequence 2247 drift, wrong modified config bytes, and a positive case where exactly four mutable configs differ. The fifth profiled file, CouchDB `default.ini`, has no instance-specific value in the real 2.6.1-7 MSI and must remain byte-identical.

- [x] **Step 2: Run helper tests and observe verifier failures**

Run the Task 1 helper-test command.

Expected: FAIL because CAB rebuild and output verifier do not exist.

- [x] **Step 3: Implement extract-modify-repack-verify**

Use DTF `CabInfo`/`Package` APIs to extract both embedded streams into the protected workspace. Hash source extraction before edits. Generate the four mutable config byte arrays in memory, write them, update only their FileSize/MsiFileHash rows, preserve the profiled CouchDB `default.ini` byte-for-byte, rebuild each CAB preserving sequence membership, then re-open the final MSI and compare every extracted file with either its source hash or its exact expected generated bytes.

- [x] **Step 4: Run helper tests and verify no vendor fixture is tracked**

Run the Task 1 helper-test command, then:

```powershell
git ls-files | Select-String -Pattern '\.(msi|cab|beam|exe|dll)$'
```

Expected: tests PASS; command returns no newly tracked binary/vendor artifact.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests
git commit -m "Verify rebuilt local module MSI cabinets"
```

### Task 6: Protect staging and recover it after cancellation or crash

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiStagingManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiStagingJournalStore.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiWorkspace.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/PathSafety.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleMsiWorkspace : IDisposable
{
    internal string RootPath { get; private set; }
    internal string SourceCopyPath { get; private set; }
    internal string OutputMsiPath { get; private set; }
    internal void MarkInstalled(string productCode, string packageCode);
    internal void Cleanup();
}
```

- [x] **Step 1: Add failing mutation-boundary tests**

Inject failure after journal creation, source copy, transformation, verification, Windows Installer return, and manifest persistence. Assert next-start recovery deletes only the exact journal-owned workspace, reconstructs exact SYSTEM+Administrators ACL when needed, rejects reparse points/path escape, and never uses a prefix match such as `HelperProof` matching `HelperProof2`.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because the staging journal/workspace does not exist.

- [x] **Step 3: Implement write-ahead ownership and `finally` cleanup**

Create `%ProgramData%\KRS\MultiKKT\Operations\LocalModuleMsi\<operation-id>`. Persist canonical path, nonce, expected files and state before copying. Cleanup validates nonce, exact path boundary, no reparse points and exact file allowlist before deletion. ACL repair applies an explicit protected descriptor to the owned tree; never call `icacls /reset`.

- [x] **Step 4: Run helper tests and LM safety gate**

Run the Task 1 helper-test command and `scripts\verify_lm_safety.ps1`.

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Journal temporary local module MSI workspaces"
```

## Phase B — Windows lifecycle, protocol, and operator flow

### Task 7: Install through Windows Installer API and persist exact ownership

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/IWindowsInstallerApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WindowsInstallerApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/InstalledLocalModuleProductReader.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiManifestStore.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal interface IWindowsInstallerApi
{
    uint Install(string packagePath, string hiddenProperties);
    uint Repair(string productCode);
    uint Uninstall(string productCode);
}

internal sealed class LocalModuleMsiManifest
{
    internal string Inn { get; set; }
    internal int CloneOrdinal { get; set; }
    internal string ProductCode { get; set; }
    internal string PackageCode { get; set; }
    internal string InstallRoot { get; set; }
    internal bool InstalledByApplication { get; set; }
    internal bool PreExisting { get; set; }
    internal string OwnershipNonce { get; set; }
}
```

- [x] **Step 1: Add failing API, registry, redaction, and ownership tests**

Assert native calls receive `ADMINLOGIN=admin ADMINPASSWORD=admin` but diagnostic records contain `ADMINLOGIN=<redacted> ADMINPASSWORD=<redacted>`; MSI error codes survive unchanged. Verify HKLM 32/64 uninstall inventory, exact ProductCode/InstallLocation/version, pre-existing base preservation, app-installed base removal eligibility, and hash-guarded manifest read-back.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because the Windows Installer adapter/manifests do not exist.

- [x] **Step 3: Implement native install/repair/uninstall and manifests**

Use `MsiInstallProductW` and `MsiConfigureProductExW`; do not spawn `msiexec`. Convert nonzero return codes to an exception that retains the numeric MSI code. Store no password/property command line. A base product discovered before the operation gets `PreExisting=true`, `InstalledByApplication=false`.

- [x] **Step 4: Run helper tests and secret scan**

Run the Task 1 helper-test command and:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify_lm_safety.ps1
```

Expected: PASS; only the existing allowlisted credential default appears in production code.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Install owned local module MSI products"
```

### Task 8: Verify config, cookies, services, and listener ownership

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleInstalledLayout.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleConfigurationInspector.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleServicePairController.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiReadinessProbe.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/TcpListenerOwnerReader.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleConfigurationObservation
{
    internal string ApiBindAddress { get; set; }
    internal int ApiPort { get; set; }
    internal string DatabaseBindAddress { get; set; }
    internal int DatabasePort { get; set; }
    internal string ApiNodeName { get; set; }
    internal string DatabaseNodeName { get; set; }
    internal string CookieDigest { get; set; } // one-way comparison digest only
}

internal interface ILocalModuleServicePairController
{
    void StartDatabaseThenApi(LocalModuleInstalledLayout layout);
    void StopApiThenDatabase(LocalModuleInstalledLayout layout);
}
```

- [x] **Step 1: Add failing config and readiness tests**

Test `WriteValuesToVmArgs` result in `vm.args`, `WriteIniFiles` result in `local.ini`, and `WriteErtsBinPath` result in `erl.ini`. Require equal 32-character cookie within a pair, different digest across pairs, API `0.0.0.0`, DB `127.0.0.1`, expected node names, service ImagePath inside exact install root, and listeners owned by the service process tree. Assert no cookie value is returned or logged.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because installed-layout inspection/readiness do not exist.

- [x] **Step 3: Implement strict parsers and ordered lifecycle**

Parse only the characterized INI/vm.args keys; reject duplicate keys, mixed instance paths and ambiguous quoting. Compare cookies in memory, expose only SHA-256 digest. Start DB and wait for its exact port/owner before API; stop in reverse and require listeners to disappear without killing processes.

- [x] **Step 4: Run helper tests**

Run the Task 1 helper-test command.

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Verify installed local module service pairs"
```

### Task 9: Own one narrow firewall rule per LM API

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/IWindowsFirewallApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WindowsFirewallApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleFirewallRule.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleFirewallManager.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiManifest.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleFirewallRule
{
    internal string RuleName { get; set; }
    internal string OwnershipId { get; set; }
    internal string ProgramPath { get; set; }
    internal int LocalPort { get; set; }
    internal string RemoteAddress { get; set; } // LocalSubnet or validated IP/CIDR
}
```

- [x] **Step 1: Add failing firewall ownership tests**

Require inbound TCP Allow, Domain|Private only, Public false, exact API port and instance `erl.exe`, `LocalSubnet` default, validated explicit IP/CIDR, no DB rule, idempotent exact match, refusal to adopt/overwrite/delete a foreign conflict, and exact owned removal.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because firewall interfaces do not exist.

- [x] **Step 3: Implement the COM firewall adapter and ownership description**

Use the Windows Firewall COM API (`HNetCfg.FwPolicy2`/`HNetCfg.FWRule`) through a narrow adapter. Encode `KRS MultiKKT LM <ownership-id>` in Name and Grouping; re-read every field after creation. Store rule name and expected-field hash in the manifest.

- [x] **Step 4: Run helper tests and safety gate**

Run the Task 1 helper-test command and `scripts\verify_lm_safety.ps1`.

Expected: PASS and no shell firewall command exists.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Manage narrow local module firewall rules"
```

### Task 10: Implement idempotent ensure, restart, clone removal, and base compensation

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiProvisioner.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiRemovalWorkflow.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiProvisioningContext.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiManifestStore.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
internal sealed class LocalModuleMsiProvisioner
{
    internal LocalModuleMsiProvisioningItemResult Ensure(
        LocalModuleMsiProvisioningItemRequest request,
        LocalModuleMsiProvisioningContext context);
    internal LocalModuleMsiProvisioningItemResult Restart(
        LocalModuleMsiProvisioningItemRequest request,
        LocalModuleMsiProvisioningContext context);
}
```

- [x] **Step 1: Add failing lifecycle state-machine tests**

Cover new base, pre-existing base, new clone, matching ready no-op, stopped matching restart, foreign ProductCode/service/port/rule conflict, failure after install with cleanup retry, clone removal while base runs, base removal while clone runs (`stop clones → uninstall base → wait EPMD → restart clones`), pre-existing base “remove created” preservation, and full clone-first removal.

- [x] **Step 2: Run helper tests and observe failure**

Run the Task 1 helper-test command.

Expected: FAIL because the new lifecycle workflows do not exist.

- [x] **Step 3: Implement the state machine using Tasks 3–9 only**

The provisioner must not duplicate MSI parsing, firewall logic, SCM logic or path checks. Persist a journal transition before each mutation. On a group failure, return that group’s exact result and continue independent groups; only source-profile/staging-integrity failures are batch-global because the shared source is untrusted.

- [x] **Step 4: Run helper tests twice to prove idempotence**

Run the Task 1 helper-test command twice.

Expected: PASS both times; second ensure performs no MSI/service/firewall mutation.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Provision independent local module MSI instances"
```

### Task 11: Replace the supervisor complete-stack protocol with MSI protocol v3

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleMsiProvisioningSession.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LocalModuleMsiProvisionerClient.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchRequest.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchResult.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceOperation.cs`
- Modify: `src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ProvisioningRequestValidator.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`
- Modify: `tests/EsmTspiot.Shared.Tests/EsmTspiot.Shared.Tests.csproj`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

```csharp
[DataContract]
public sealed class LocalModuleMsiProvisioningItemRequest
{
    [DataMember(Order = 1)] public string Inn { get; set; }
    [DataMember(Order = 2)] public int CloneOrdinal { get; set; }
    [DataMember(Order = 3)] public int ApiPort { get; set; }
    [DataMember(Order = 4)] public int DatabasePort { get; set; }
    [DataMember(Order = 5)] public string InstallVolumeRoot { get; set; }
    [DataMember(Order = 6)] public string RemoteAddress { get; set; }
    [DataMember(Order = 7)] public string ExpectedManifestSha256 { get; set; }
}
```

Add operations `EnsureMsiLocalModules`, `RestartMsiLocalModule`, `RemoveMsiLocalModule`, `RemoveAllMsiLocalModules`; set `ProvisioningRequestValidator.CurrentSchemaVersion = 3`.

- [x] **Step 1: Add failing v3 protocol and client tests**

Test canonical hashing of every field, no password/cookie/command/full install path in protocol, duplicate INN/ordinal/port rejection, local-volume validation, unknown v1/v2 rejection with a clear version error, monotonic item exchange, and independent item failure continuation. Assert the new client validates helper version/hash before launch.

- [x] **Step 2: Run shared/helper tests and observe failure**

Run Task 2 Step 2.

Expected: FAIL because schema v3 and MSI client/session do not exist.

- [x] **Step 3: Implement schema v3 and route helper operations**

Keep direct-controller operations in the same request envelope. For MSI ensure, initialize and lock the shared source once, then process unique-INN items sequentially. Remove the old `_stopRemaining` canary behavior; only emit cancelled for user cancellation or a declared batch-global source-integrity failure.

- [x] **Step 4: Run shared/helper/operator matrices**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1 -SkipPackage
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/EsmTspiot.Shared src/EsmTspiot.ServiceProvisioner src/EsmTspiot.WinForms.Shared src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj tests
git commit -m "Add local module MSI provisioning protocol v3"
```

### Task 12: Integrate the nonblocking full automatic workflow and UI

**Files:**
- Create: `src/EsmTspiot.WinForms.Shared/LocalModuleMsiOperatorInventoryReader.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LocalModuleInstallVolumeDialog.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.DirectControllers.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Layout.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmAutomaticSetupDialog.cs`
- Modify: `src/EsmTspiot.Shared/Services/LmAutomaticSetupCoordinator.cs`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `tests/EsmTspiot.Operator.Tests/Program.cs`

**Interfaces:**
- Produces workflow stages: `Registration`, `ControllerEnsure`, `LocalModuleEnsure`, `EsmBinding`, `InitializationDeferred`.
- Consumes the INN→API mapping from Task 2 for controller profiles and the KKT→controller gRPC mapping for ESM PUT.

- [ ] **Step 1: Add failing workflow/UI tests**

Test: two KKT/same INN create two controller requests but one LM request; unsupported MSI still creates controllers and records LM deferred; LM failure does not cancel later INN; initialization status never blocks completion; root defaults to base volume and shows free-space/system-cache estimates; removal wording preserves pre-existing base; logs use `Попытка N из M`; controller ESM binding remains `127.0.0.1:50063+`, while controller `lmConfig.port` uses the INN’s `5995/6995+`.

- [ ] **Step 2: Run shared/operator tests and observe failure**

Run:

```powershell
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Debug
dotnet run --project tests\EsmTspiot.Operator.Tests\EsmTspiot.Operator.Tests.csproj -f net8.0-windows -c Debug
dotnet run --project tests\EsmTspiot.Operator.Tests\EsmTspiot.Operator.Tests.csproj -f net48 -c Debug
```

Expected: FAIL because the MSI inventory/root UI and nonblocking coordinator do not exist.

- [ ] **Step 3: Replace the active complete-stack path**

Order the automatic flow as: finish registration → build stable LM mapping → ensure direct controllers using explicit target LM ports → attempt unique-INN MSI products → bind every ready controller to ESM → report LM initialization separately. Replace old managed-runtime columns/actions with Base/Clone, install root, API endpoint, state, and ownership. Keep technical details in redacted logs.

- [ ] **Step 4: Run full non-elevated matrix and UI gate**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1 -SkipPackage
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EsmTspiot.Shared src/EsmTspiot.WinForms.Shared src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj tests
git commit -m "Integrate automatic MSI local module setup"
```

### Task 13: Remove the old supervisor from production and retain only exact legacy cleanup

**Files:**
- Delete: `src/EsmTspiot.ServiceProvisioner/CompleteStackProvisioningSession.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/EpmdInstanceController.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ErlangChildStartPlan.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/LmGatewaySupervisorService.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/LocalModuleConfiguration.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/LocalModuleConfigurationWriter.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/LocalModuleRuntimeInstaller.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ManagedChildProcess.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ManagedChildServiceHost.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleProvisioner.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleServiceReadinessProbe.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleUpdateWorkflow.cs`
- Delete: `src/EsmTspiot.ServiceProvisioner/WindowsManagedLocalModulePlatform.cs`
- Delete: `src/EsmTspiot.WinForms.Shared/CompleteStackProvisionerClient.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ProvisionerCommandLine.cs`
- Modify: `scripts/verify_lm_safety.ps1`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**
- Retains: `LegacyManagedStateMigrationWorkflow`, `LegacyOwnedProcessTerminator`, old manifest/journal readers needed to prove ownership and clean `krs-esm-lm-*` and `krs-lm-db/api-*` leftovers.
- Removes: every command-line mode and service creation path that can start persistent KRS supervisor/Erlang services.

- [ ] **Step 1: Add a failing production-surface test**

Assert `ProvisionerCommandLine` rejects `--supervise` and `--supervise-local-module`, production assembly contains no service entry point for KRS LM supervisor, and legacy migration can still recognize both old service families only for removal.

- [ ] **Step 2: Run helper tests and confirm failure**

Run the Task 1 helper-test command.

Expected: FAIL because supervisor modes/classes are still active.

- [ ] **Step 3: Remove active supervisor code and obsolete tests**

Delete the listed files, remove their command-line dispatch, and remove tests that assert creation/start/readiness/update of the old architecture. Keep and strengthen tests for exact elevated legacy inventory, PID reuse protection, path boundary, graceful stop, native owned-process termination and retry cleanup.

- [ ] **Step 4: Prove no production KRS supervisor path remains**

Run:

```powershell
rg -n --glob '*.cs' 'supervise-local-module|--supervise\b|ManagedChildServiceHost|LmGatewaySupervisorService' src | Where-Object { $_ -notmatch 'LegacyManagedStateMigrationWorkflow|LegacyOwnedProcessTerminator' }
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1 -SkipPackage
```

Expected: `rg` returns no non-migration source hit; full matrix PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A src tests scripts/verify_lm_safety.ps1
git commit -m "Retire persistent KRS local module supervisors"
```

## Phase C — real-machine proof, cleanup, and release

### Task 14: Add an elevated two-LM sandbox that always restores the baseline

**Files:**
- Create: `scripts/verify_local_module_msi_sandbox.ps1`
- Create: `docs/testing/2026-09-02-local-module-msi-clone-acceptance.md`
- Modify: `scripts/run_release_matrix.ps1`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**
- Script parameters:

```powershell
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][string]$LocalModuleMsiPath,
    [string]$RemoteAddress = 'LocalSubnet',
    [switch]$AllowRebootPhase
)
```

- Without `-AllowRebootPhase`, the script must never reboot or schedule a reboot.

- [ ] **Step 1: Add failing production-sandbox assertions behind an explicit test mode**

Add helper-test mode `--local-module-msi-sandbox <msi> <remote-address>` that snapshots services, processes, registry, MSI products, listeners, firewall, install roots and `C:\Windows\Installer` size; verifies source profile; then exposes machine-readable stage results.

- [ ] **Step 2: Run the script in inventory-only mode and verify no mutation**

Run:

```powershell
$lmMsi = Join-Path $env:USERPROFILE 'Downloads\regime-2.6.1-7.msi'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify_local_module_msi_sandbox.ps1 -LocalModuleMsiPath $lmMsi -RemoteAddress LocalSubnet -WhatIf
```

Expected: exact signed source accepted; baseline report written under a temporary directory and deleted; no service/product/rule/path changes.

- [ ] **Step 3: Implement the destructive sandbox with `finally` restoration**

The script must verify and, only after an exact test-origin check, remove stale `D:\krs-lm-2.6.1-proof` and its four exact rules. Then exercise base start, clone install, simultaneous listeners, cookies, rules, stop/start, staging deletion + repair, clone removal, reinstall, base-removal compensation only if the base was installed by this sandbox, and full cleanup. A pre-existing base is never uninstalled. Every created product/rule/path is recorded before mutation and removed in `finally`.

- [ ] **Step 4: Run the real two-LM matrix on the authorized work computer**

Run the script without `-WhatIf`. Expected final checks:

```text
KRS supervisor services/processes: 0
clone MSI products/services/rules/install roots: 0
source/output/staging MSI: 0
pre-existing regime/yenisei and esm services: restored to snapshot
unrelated services/processes/firewall rules: unchanged
```

Do not run the reboot phase until the user separately authorizes it.

- [ ] **Step 5: Commit the gate and evidence template, not machine evidence**

```powershell
git add scripts/verify_local_module_msi_sandbox.ps1 scripts/run_release_matrix.ps1 tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs docs/testing/2026-09-02-local-module-msi-clone-acceptance.md
git commit -m "Add two-instance local module MSI sandbox"
```

### Task 15: Package exact helper closure, update field docs, and run final gates

**Files:**
- Modify: `scripts/package_compact_release.ps1`
- Modify: `scripts/verify_compact_security_contract.ps1`
- Modify: `scripts/verify_lm_safety.ps1`
- Modify: `docs/testing/2026-09-01-field-acceptance-1.6.4.0.md`
- Modify: `README.md`

**Interfaces:**
- Compact ZIP contains app EXE, shared DLL, helper EXE/shared DLL, four pinned DTF DLLs, README, field guide and checksums only.
- ZIP contains no MSI/CAB/vendor file, generated package, staging, customer config, secret or test evidence.

- [ ] **Step 1: Add failing compact-layout and secret-exclusion checks**

Require exactly the DTF helper closure; reject any `.msi`/`.cab`, `regime`/`yenisei`/`nssm`/`erl`, package cache, `-setcookie`/`ADMINPASSWORD=<value>`/unredacted password context, real absolute customer path, or file over the existing justified limit except allowlisted DTF DLLs by exact name/hash.

- [ ] **Step 2: Run package gate and observe expected layout failure**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package_compact_release.ps1 -Configuration Release
```

Expected: FAIL until expected layout/readme are updated for DTF and LM installation.

- [ ] **Step 3: Update packaging and operator documentation**

Document: exact source MSI selection, default base-volume clone root, system-cache space, LAN API firewall scope, DB loopback, one LM per INN, initialization deferred, pre-existing base preservation, removal order, expected vendor Erlang process trees, and no reboot without approval. Remove the old statement that LM installation is intentionally deferred.

- [ ] **Step 4: Run all automated and real-machine gates**

Run:

```powershell
$lmMsi = Join-Path $env:USERPROFILE 'Downloads\regime-2.6.1-7.msi'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_release_matrix.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify_local_module_msi_sandbox.ps1 -LocalModuleMsiPath $lmMsi -RemoteAddress LocalSubnet
git status --short
```

Expected: all tests/gates PASS; sandbox restores baseline; only intentional source/doc changes are present. Reboot acceptance remains explicitly pending until separately authorized.

- [ ] **Step 5: Perform final review and commit**

Review for duplicated MSI parsing, generic SQL/string replacement, secret-bearing DTOs/logs, path-prefix ownership, foreign service/rule adoption, old supervisor reachability, canary blocking, and incomplete cleanup. Then:

```powershell
git add README.md docs/testing scripts
git commit -m "Complete local module MSI clone release gates"
```

## Execution checkpoints

- After Task 5: review the offline transformation/verifier before any installer execution exists.
- After Task 10: review lifecycle/ownership before connecting it to IPC or UI.
- After Task 13: run the complete non-elevated matrix and inspect the production assembly for removed supervisor entry points.
- After Task 14: stop and inspect the restored work-computer baseline before packaging.
- Reboot phase: a separate user authorization is mandatory.
