# MultiKKT petrol redesign Implementation Plan

> Execution: superpowers:executing-plans. One fresh whole-branch review at the end.

**Goal:** deliver unpublished working 11.3.31.0-test with the approved visual style
and organization column from ATOL registration data.
**Architecture:** extend existing OperatorTheme and layouts; preserve all operational
controls, callbacks, workflows, settings formats, service and directory names.
**Tech:** existing WinForms/AntdUI 2.4.18, net48 x86 and net8 Windows.
**Spec:** docs/testing/petrol-redesign-requirements.md.

## Constraints

- Exact supplied palette; no theme selector, decorative gradients or new UI library.
- Full table fields remain available, with horizontal scroll at narrow widths.
- Organization is optional metadata, read through an existing ATOL lease; no extra
  hardware polling from inventory. Only legal-form prefixes are shortened.
- No helper/service algorithms changed. Keep working 11.3.30 ZIP intact.
- Use current isolated lm-server worktree; branch codex/petrol-redesign from f462fbc.
- Final output is a test ZIP and real screenshots, no push/release.

## Tasks

- [x] Baseline builds/checks. Add regression for log toolbar containment.
- [x] Central palette, typography and flat components; unified compact navigation;
  remove redundant step decoration without removing operational controls.
- [x] Restyle wizard, full inventory and journal; restyle supplementary windows.
- [x] Organization: test driver reads optional FN tag 1048 with registration INN,
  metadata round-trip and INN matching, formatting, manual/auto display.
- [x] Integrate metadata at existing scan boundary, inventory lookup without COM.
- [x] Build 31-test, automated size/scale geometry checks, screenshots, independent review,
  fix material findings, package/unpack smoke and deliver.

## Review focus

- Existing actions still reachable, busy/cancel and partial-result behavior intact.
- No names reused after KKT owner INN changes; metadata failure never blocks registration.
- Original credential masking, deletion constraints and Frontol behavior retained.
- Actual layout at narrow windows; DPI geometry checks distinguished from OS DPI tests.

## Ledger

- Ruling: user says they are going to sleep, not to stop; continue without questions.
- Ruling: reuse existing UI classes, not the rejected palette-demo host/architecture.
- Ruling: organization metadata uses a new optional cache file, leaving existing
  configuration formats untouched; UI can operate if cache is absent/unreadable.
- Ruling: visual-only changes verified by capture/layout checks; no tests for each color.
- Baseline UI suite passed. Log overlap regression failed on the original 28px
  toolbar row, then passed after AutoSize toolbar layout and existing button styles.
- Vendor source confirmed: installed ATOL examples/json_scripts/json_getRegistrationInfo.js
  uses REG_INFO then getParamString(1048/1018). Replaced the first two-query approach
  with that single query. Fake-driver regression observed RED then GREEN.
- Organization metadata tests cover optional read failure, cache round-trip,
  INN mismatch, legal-form formatting and unreadable cache; no extra COM polling.
- Full release matrix passed (Shared net8/net48, Operator net8/net48, 137 helper
  checks, C#5 builds, safety/UI gates, Modern build). Logs: artifacts/test/petrol.
- Independent review petrol_final_review found five issues: missing organization
  in inventory-to-wizard/orphan paths, editable organization column, remaining old
  colors/dialogs, net8 single-file icon, stale README. All fixed and confirmed by
  targeted follow-up review; UI regressions cover readonly and re-entry paths.
- Screenshot review exposed a second step defect: nested TableLayoutPanel rows
  were implicitly AutoSize and exceeded the strip. Explicit 100% rows, simple
  native containers and vertically centered icons fixed ancestor clipping and
  caption alignment. Both issues reproduced RED, then geometry100/125/150 passed.
- Real UI activation/capture was attempted: window discovered, capture blank,
  activation rejected. Own launched process was closed. OS DPI/manual interaction
  cannot be claimed verified; clipboard copy test also skipped due clipboard owner.
- Packaging security contract passed; ZIP SHA256:
  ed9be64f2cdd64e56caeb5554663d59ded3b88dce9c3e1241ddb1b46b4ada046.
- Unpacked into a fresh GUID directory: all 16 internal hashes match, integrity
  contract and full UI gate pass against the unpacked EXE via -ApplicationPath.
  Last UI geometry and Legacy/Modern builds also pass after step layout fixes.
- Delivery remains local test build; keep this branch/worktree, no merge/push/release.
- Pending external verification: organization read on registered physical KKT,
  real Windows100/125/150% and manual mouse/keyboard interaction.
