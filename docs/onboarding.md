# CodeCompass Guided Developer Onboarding

CodeCompass replaces unstructured, unguided repository browsing with role-tailored learning paths and safe starter tasks.

---

## 1. Onboarding Workflow

The developer onboarding journey proceeds through 6 structured phases:

1. **Role Specialization**:
   - Developers select their active role from the header switcher or modal:
     - `Backend Developer`: Server architectures, Web APIs, data access, and domain logic.
     - `Frontend Developer`: UI components, client state, and API integration.
     - `Full Stack Developer`: Cross-tier workflows from user interactions down to database entities.
     - `QA Engineer`: Test coverage, API contracts, verification filters, and regression defense.
   - The selected role is persisted locally and passed to the backend API.

2. **Repository Intelligence Mapping**:
   - [`OnboardingService`](file:///d:/CodeCompress/backend/CodeCompass.Api/Services/OnboardingService.cs) scans the analyzed repository for matching modules, architectural layers, and file structures.
   - Nonexistent modules are never included in the curriculum.

3. **Step-by-Step Learning**:
   - Each step features:
     - **Title & Step Type** (`Overview`, `Module`, `Contribution`)
     - **What You'll Learn**: Concrete technical objectives.
     - **Why It Matters**: Architectural significance and system role.
     - **Related Module**: Module layer, file count, and path with a direct link to view it in the Architecture Graph.
     - **Relevant Files**: Ranked list of core source files with languages and byte sizes.
     - **AI Architecture Guidance**: Grounded explanation powered by IBM watsonx / repository intelligence.

4. **Progress Tracking**:
   - Clicking **Mark Complete ✓** records the completion timestamp in SQL Server.
   - Overall onboarding readiness is dynamically calculated:
     $$\text{Progress \%} = \frac{\text{Completed Steps}}{\text{Total Steps}} \times 100$$
   - Visualized in real time in the UI (`██████░░░░ 60%`).

5. **Starter Task Discovery**:
   - Safe beginner-friendly tasks identified by repository intelligence.
   - Evaluates:
     - Low dependency blast radius (&lt;3 files)
     - Understandable module scope
     - Existing code pattern availability

6. **First Contribution Preparation**:
   - Full task specification with:
     - What the task does & why it is recommended
     - Relevant files & modules
     - Suggested first step
     - Existing code pattern reference
     - Integrated Change Impact analysis table (direct, potential, needs-verification)
     - Pre-filled deep link into Codebase Assistant.
