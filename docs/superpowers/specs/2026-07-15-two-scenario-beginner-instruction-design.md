# Two-Scenario Beginner Instruction Design

## Goal

Create one beginner-friendly instruction that clearly separates two workflows: manually adding exactly the second KKT and automatically processing all connected fiscal registrars.

## Structure

The document starts with prerequisites and a simple scenario choice. Scenario 1 contains only the manual second-KKT sequence. Scenario 2 contains only the experimental automatic sequence. Shared diagnostics and safety notes appear after both scenarios.

## Safety Rules

- State exactly when Frontol should be running and when it should be closed.
- Explain that automatic processing starts only after the user reviews the plan and clicks the start button.
- Explain sequential port assignment and the special case where the first instance has `softPort = 0`.
- Explain that stopping does not roll back completed actions.
- Do not include real INN, KKT/FN serials, usernames, or personal computer paths.
- Keep API and error descriptions consistent with the implemented application.

## Outputs

- `INSTRUCTION_FOR_DUMMIES.md`
- `output/pdf/instrukciya_dlya_chaynikov_esm_tspiot.pdf`
- Release copy: `artifacts/release/Instruction-MultiKKT-v10.pdf`
