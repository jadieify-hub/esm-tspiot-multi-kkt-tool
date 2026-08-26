# ESM TS PIoT Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build two Windows desktop utilities for adding and registering a second KKT in ESM/TS PIoT, with shared validation and HTTP behavior.

**Architecture:** Use one .NET Standard 2.0 shared library for models, validation, error decoding, JSON handling, and HTTP service behavior. Add two thin WinForms frontends: .NET Framework 4.8 for Windows 7-11 compatibility and .NET 8 for self-contained Windows 10/11 publishing.

**Tech Stack:** C#, WinForms, .NET Standard 2.0, .NET Framework 4.8, .NET 8, MSTest, HttpClient.

---

## File Structure

- `EsmTspiotTool.sln`: solution containing all projects.
- `src/EsmTspiot.Shared/EsmTspiot.Shared.csproj`: shared .NET Standard 2.0 library.
- `src/EsmTspiot.Shared/Models/*.cs`: request, response, and validation result models.
- `src/EsmTspiot.Shared/Validation/TspiotInputValidator.cs`: form input validation.
- `src/EsmTspiot.Shared/Services/TspiotApiClient.cs`: HTTP requests with JSON bodies and raw response capture.
- `src/EsmTspiot.Shared/Services/TspiotErrorDecoder.cs`: maps status codes and body error codes to Russian explanations.
- `src/EsmTspiot.Shared/Services/InstanceInfoParser.cs`: extracts `id`, `port`, `softPort`, `dkktPort`, and `serviceState` from unknown JSON shapes.
- `src/EsmTspiot.Shared/Logging/LogFormatter.cs`: timestamped log text formatting.
- `src/EsmTspiot.Legacy.WinForms/`: .NET Framework 4.8 UI.
- `src/EsmTspiot.Modern.WinForms/`: .NET 8 UI.
- `tests/EsmTspiot.Shared.Tests/`: MSTest tests for shared logic.
- `README.md`: operator and developer documentation.

## Tasks

### Task 1: Create Solution And Shared Library

- [ ] Create the solution and folders.
- [ ] Create `EsmTspiot.Shared` targeting `netstandard2.0`.
- [ ] Add request and response models.
- [ ] Add shared constants for defaults and endpoint paths.
- [ ] Build the shared library.

### Task 2: Add Shared Tests And Validation

- [ ] Create MSTest project `EsmTspiot.Shared.Tests`.
- [ ] Add tests for valid input, invalid URL, non-digit serials, invalid INN, invalid ports, and equal `port`/`softPort`.
- [ ] Implement `TspiotInputValidator` until tests pass.

### Task 3: Add Error Decoding And Instance Parsing

- [ ] Add tests for error codes `1010`, `1001`, `1015`, `2046`, HTTP `403`, and connection failure text.
- [ ] Add tests that parse instance arrays and nested JSON containing `id`, `port`, `softPort`, `dkktPort`, and `serviceState`.
- [ ] Implement `TspiotErrorDecoder` and `InstanceInfoParser` until tests pass.

### Task 4: Add HTTP Client And Logging

- [ ] Implement `TspiotApiClient` with async GET, POST, and PUT.
- [ ] Send JSON as `application/json`.
- [ ] Return a response envelope with method, URL, request body, status code, raw response body, success flag, and decoded message.
- [ ] Implement `LogFormatter` for timestamped log entries.
- [ ] Test URL composition and log formatting without calling a real ESM service.

### Task 5: Add Legacy WinForms UI

- [ ] Create .NET Framework 4.8 WinForms app.
- [ ] Build a single main form with grouped sections: connection, second KKT data, ports, actions, log.
- [ ] Wire buttons to the shared validator and API client.
- [ ] Disable action buttons while requests are running.
- [ ] Enforce ATOL confirmation checkbox before PUT.
- [ ] Implement duplicate-id warning before POST using GET and `InstanceInfoParser`.

### Task 6: Add Modern WinForms UI

- [ ] Create .NET 8 WinForms app.
- [ ] Reuse equivalent UI behavior and shared library references.
- [ ] Ensure publish profiles or documented commands produce `win-x86` and `win-x64` self-contained builds.

### Task 7: Add Documentation And Verification

- [ ] Write `README.md` with usage, build, publish, and troubleshooting notes.
- [ ] Run shared tests.
- [ ] Build shared library and both UI projects where SDK/tooling is available.
- [ ] Record any environment limitation, especially if .NET Framework reference assemblies are missing on the current machine.

## Self-Review

- Spec coverage: the tasks cover two builds, shared behavior, validation, HTTP, logging, duplicate-id warning, manual ATOL confirmation, and documentation.
- Placeholder scan: no placeholder requirement remains.
- Type consistency: project and class names are consistent across tasks.
