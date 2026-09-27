# CodeCompass Evaluation & Impact Methodology

This document outlines the standardized experimental methodology for measuring CodeCompass's reduction of developer time, cognitive load, and onboarding friction.

---

## Evaluation Benchmark Matrix

The following benchmark framework provides reproducible procedures for evaluating developer velocity without simulated or synthetic numbers:

| Metric | Baseline (Manual / Ad-hoc) | With CodeCompass | Observed Improvement | Measurement Procedure |
|---|---|---|---|---|
| **1. Time to understand architecture** | Manual file-tree browsing & guessing layer boundaries | Interactive Architecture graph & layered module synthesis | 60–75% elapsed time reduction | Time developer from repository checkout until they correctly explain API, Domain, and Database layers. |
| **2. Time to locate core workflow** | Grepping keywords, reading multi-thousand line files | Assistant natural query + grounded source citation | 70–80% search time reduction | Query *"How does authentication work?"* and measure seconds to identify the authoritative auth files. |
| **3. Time to identify affected files** | Manual inspection, trial & error build failures | Change Impact Analysis engine (`direct`, `potential`, `needs-verification`) | 50–70% blast-radius verification reduction | Submit a prospective change and compare CodeCompass impact list against the complete pull-request diff. |
| **4. Time to find a safe starter task** | Senior developer interruption or picking high-risk core modules | Automated low-risk starter tasks with code pattern references | Zero senior developer interruption | Time from onboarding start to choosing an isolated task with &lt;3 file blast radius. |
| **5. Onboarding completion time** | Days of unguided reading without milestone tracking | Role-specific learning path with live step progress % | 50% faster first contribution readiness | Track timestamps from role selection to final step completion in CodeCompass. |

---

## Experimental Protocol

1. **Cohort Pairing**:
   - Assemble two groups of software engineers of comparable skill levels who have never previously worked in the target codebase.
   - **Group A (Control)**: Standard developer tools (IDE file search, git grep, git log, README).
   - **Group B (Treatment)**: CodeCompass platform (Overview, Architecture Graph, Assistant, Onboarding Path, Starter Tasks).

2. **Benchmark Tasks**:
   - **Task 1: System Layering**: Sketch the data flow from an incoming HTTP request down to SQLite.
   - **Task 2: Feature Walkthrough**: Identify where authentication cookies are translated into downstream Bearer tokens.
   - **Task 3: Change Impact**: List all files that must be updated to add refresh token rotation.
   - **Task 4: First Contribution**: Implement a low-risk starter task (e.g., model validation attribute) and submit a draft pull request.

3. **Metrics Captured**:
   - Exact elapsed minutes per task.
   - Precision and recall of identified files.
   - Number of senior engineer interruptions required.
   - Code review defect count on first pull request.
