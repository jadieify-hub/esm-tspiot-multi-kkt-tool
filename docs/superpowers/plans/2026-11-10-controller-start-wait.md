# Controller startup wait

Execute inline with one final independent review.

Scope: direct-controller readiness limit 45→180 seconds, retain immediate success,
report actual SCM state and PID during waiting and on timeout. Do not alter service
start/recovery policy, LM initialization, ownership checks or binding rules.

- [x] Regression: platform forwards 180000ms; failed probe identifies SCM state/PID;
  ready service returns immediately and a pending service can become ready.
- [x] Update common limit/text and probe/trace messages, suppress duplicate trace writes.
- [x] Helper tests, Legacy/Modern builds, review, new32-test package and unpack check.

Ledger: existing isolated worktree, branch codex/controller-start-timeout from eb07274.
Field log has two 45s failures of the base controller; actual SCM state/PID is absent.
The change adds evidence, without claiming that a longer wait fixes a crashed service.

RED: tests observed 45000 instead of 180000 and missing StartPending/PID.
GREEN: all138 helper tests pass, including pending→ready polling and early return.
Legacy C#5 and Modern build pass; independent controller_wait_review has no material
findings. Package and unpacked security contract pass; all16 internal hashes match
and the UI suite passes against the unpacked EXE. Safety gate and diff check pass.
ZIP SHA256: b5ded1eacd4b30e9a86f65cb51619641b81aec831e9decca5a9500be470ee4b8.
Keep the branch locally, no publication. Physical problem KKT remains untested.
