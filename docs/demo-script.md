# CodeCompass 3–5 Minute Hackathon Demonstration Script

This script provides an exact step-by-step presentation flow for demonstrating CodeCompass to hackathon judges using a real public repository (e.g., `https://github.com/davidfowl/TodoApi`).

---

## Act 1: The Onboarding Problem & System Overview (0:00 – 0:45)

1. **Introduction**:
   - *"When engineers join a mature project, understanding the architecture and finding safe tasks takes days. CodeCompass transforms this into an interactive, repository-grounded onboarding platform."*
2. **Overview Dashboard (`http://localhost:5173`)**:
   - Point out the **Intelligence Engine** pill showing `IBM watsonx • Granite` connected.
   - Point out the active repository (`TodoApi` in C#) with 78 files across 6 detected modules.
   - Highlight the **Evaluation & Developer Impact Framework** showing 60–75% reductions in time-to-orientation.

---

## Act 2: Architecture Intelligence & Dependency Flow (0:45 – 1:30)

1. **Open Architecture Tab**:
   - Click **Architecture** in the left navigation sidebar.
   - Point out the layered arrangement (`Presentation`, `Backend`, `Data Access`, `Shared`).
2. **Module Inspection**:
   - Click the `Client` module or `Server` module.
   - Show the right drawer displaying encapsulated files, languages, and dependencies.
3. **Trace Dependency Flow**:
   - Use the **Dependency Flow** tool: From `Client` to `Server`.
   - Click **Trace Path →** to demonstrate shortest-path dependency traversal.

---

## Act 3: Senior Developer Mentor & AI Codebase Assistant (1:30 – 3:15)

1. **Open Codebase Assistant**:
   - Click **Codebase Assistant** in the sidebar.
2. **Ask Architecture Query**:
   - Ask: *"Explain the architecture of this project like I am a beginner."*
   - Point out: The assistant breaks down the Blazor client, ASP.NET Core server, and EF Core data layer, citing real repository files (`Todo.Web/Client/Program.cs`, `Todo.Web/Server/TodoApi.cs`).
3. **Ask Learning Roadmap Query**:
   - Ask: *"Teach me authentication from beginner to advanced using this repository."*
   - Point out:
     - The assistant produces a 6-level structured curriculum.
     - Notice the green `[IMPLEMENTED]` badges for cookie authentication and external OAuth in `AuthenticationExtensions.cs`.
     - Notice the purple `[NOT IMPLEMENTED — NEXT CONCEPT]` badges for refresh token rotation, MFA, and local password hashing.
     - Point out the interactive next-step action chips at the bottom (`"Start Level 1"`, `"Show login flow"`, `"Quiz me on authentication"`, `"Continue"`).
4. **Interactive Quiz**:
   - Click the chip or type: *"Quiz me on authentication."*
   - Show practical, repository-grounded questions testing knowledge of the reverse proxy token translation in `TodoApi.cs`.
5. **Change Impact Analysis**:
   - Switch to **Change Impact** mode.
   - Query: *"What happens if I modify authentication to add refresh token rotation?"*
   - Show the generated impact table categorizing files into `direct` (`AuthenticationExtensions.cs`, `TokenNames.cs`), `potential`, and `needs-verification`.

---

## Act 4: Guided Onboarding & Starter Tasks (3:15 – 4:30)

1. **Open Onboarding**:
   - Click **Onboarding** in the sidebar.
2. **Role Specialization**:
   - Click **Switch Role ⇄** and demonstrate switching between `Backend Developer`, `Frontend Developer`, and `QA Engineer`.
   - Observe how the learning path dynamically reshapes to match the domain.
3. **Step Inspection & Progress**:
   - Click on **Step 1 (Project Structure & Solution Architecture)**.
   - Show the concrete technical objectives, system rationale, and relevant source files.
   - Click **Mark Complete ✓** and watch the readiness progress bar update (`██████░░░░ 60%`).
4. **Starter Tasks**:
   - Click the **Starter Tasks** tab.
   - Open `"Add validation attribute to todo model"`.
   - Show:
     - Complexity pill (`🟢 Easy`)
     - Why recommended (low blast radius)
     - Suggested first step
     - Existing code pattern reference
     - Pre-computed change-impact table
   - Click **Ask Assistant About This Task ◆** to show seamless deep linking back to the Assistant.

---

## Conclusion (4:30 – 5:00)

- Summarize: *"CodeCompass empowers developers to master unfamiliar codebases in minutes instead of weeks, combining deep repository intelligence with IBM watsonx.ai."*
