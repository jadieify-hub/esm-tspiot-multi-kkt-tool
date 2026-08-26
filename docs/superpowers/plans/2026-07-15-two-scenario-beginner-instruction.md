# Two-Scenario Beginner Instruction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce beginner-friendly Markdown and PDF instructions for manual second-KKT setup and automatic processing of all connected KKT devices.

**Architecture:** Keep `INSTRUCTION_FOR_DUMMIES.md` as the single source of truth. Generate the PDF with the existing ReportLab script, then verify extracted text and rendered pages.

**Tech Stack:** Markdown, Python, ReportLab, pdfplumber, pypdfium2.

## Global Constraints

- Do not modify application code or EXE files.
- Do not include personal data or user-specific paths.
- Describe only behavior implemented in v10.

---

### Task 1: Rewrite the instruction around two scenarios

**Files:**
- Modify: `INSTRUCTION_FOR_DUMMIES.md`

**Interfaces:**
- Consumes: Current v10 button labels, validation rules, port assignment, recovery behavior.
- Produces: Complete Markdown source for the PDF generator.

- [ ] **Step 1:** Replace the mixed workflow with prerequisites and a scenario selector.
- [ ] **Step 2:** Write the numbered manual second-KKT workflow.
- [ ] **Step 3:** Write the numbered automatic all-KKT workflow.
- [ ] **Step 4:** Add shared error, logging, and safety sections.
- [ ] **Step 5:** Scan the Markdown for personal data and ambiguous instructions.

### Task 2: Generate and verify the PDF

**Files:**
- Modify: `output/pdf/instrukciya_dlya_chaynikov_esm_tspiot.pdf`
- Modify: `artifacts/release/Instruction-MultiKKT-v10.pdf`

**Interfaces:**
- Consumes: `INSTRUCTION_FOR_DUMMIES.md` and `scripts/generate_instruction_pdf.py`.
- Produces: Visually verified PDF release artifact.

- [ ] **Step 1:** Run `python scripts/generate_instruction_pdf.py` and require exit code 0.
- [ ] **Step 2:** Extract text with `pdfplumber` and scan for personal data.
- [ ] **Step 3:** Render every page with `pypdfium2` and inspect the contact sheet.
- [ ] **Step 4:** Copy the verified PDF to `artifacts/release/Instruction-MultiKKT-v10.pdf`.
