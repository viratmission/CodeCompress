# CodeCompass

## One-Line Pitch

CodeCompass is an AI-powered developer onboarding and codebase intelligence platform that helps developers understand unfamiliar repositories faster.

---

## Problem

Entering a large, mature, or unfamiliar software repository is one of the most frustrating experiences in software engineering. When developers join an existing project, inherit legacy code, or step in to replace departing team members, they face significant friction:

- **Architectural Opacity**: Understanding how layers, modules, and boundaries fit together requires days of manual file browsing or reading stale documentation.
- **Lost in the Codebase**: Finding authoritative entry points, core service flows, and domain models often leads to fruitless keyword grepping across thousands of files.
- **Hidden Request Flows**: Tracing an end-to-end user interaction from frontend UI components through API gateways down to database transactions is non-intuitive.
- **Fear of Breaking Changes**: Calculating the downstream blast radius and indirect ripple effects of modifying a shared model or utility is error-prone.
- **First-Contribution Paralysis**: Selecting an isolated, low-risk starter task without interrupting senior teammates is difficult.

This onboarding friction slows down engineering teams, increases time-to-first-PR, and risks introducing architectural regressions into production.

---

## Solution

CodeCompass turns complex codebases into interactive, structured, and mentored onboarding experiences through deep repository analysis:

- **Repository Analysis**: Automatically indexes GitHub repositories, categorizing files, modules, technologies, and dependency relationships into a structured relational knowledge base.
- **Architecture Intelligence**: Synthesizes interactive layered architecture graphs, computes module coupling metrics, and traces dependency flow paths.
- **Senior Developer Mentor (AI Codebase Assistant)**: Connects to **IBM watsonx.ai** to provide repository-grounded technical guidance, multi-level learning paths, step-by-step code walkthroughs, and interactive understanding checks.
- **Guided Developer Onboarding Paths**: Delivers role-specialized learning curricula (`Backend Developer`, `Frontend Developer`, `Full Stack Developer`, `QA Engineer`) with persistent milestone progress tracking.
- **Change Impact Analysis**: Evaluates direct and indirect downstream impacts before code is modified.
- **Curated Starter Tasks**: Identifies low-risk, decoupled tasks with pattern references and one-click deep links into the AI Assistant.

---

## Key Features

### 1. Repository Analyzer
- **Automated GitHub Analysis**: Clones public GitHub repositories into isolated temporary workspaces and extracts AST/import relationships.
- **Metadata & Language Detection**: Classifies source files by language (C#, TypeScript, JavaScript, Python, SQL, CSS, JSON, XML, etc.) and file sizes.
- **Structural Module Detection**: Groups directories into architectural layers (`Presentation`, `API`, `Backend`, `Business Logic`, `Data Access`, `Infrastructure`, `Shared`).
- **Dependency Mapping**: Maps inter-file imports and using statements to build a local dependency graph.

### 2. Architecture Intelligence
- **Interactive Multi-Layer Visualization**: Graph layout grouping modules by layer with file counts and coupling metrics.
- **Module Inspection Drawer**: Inspects all encapsulated source files, inbound dependencies, and outbound consumers.
- **Dependency Flow Tracer**: Traces bidirectional execution paths and shortest dependencies between components (e.g., from Frontend Client to Backend Server).

### 3. AI Codebase Assistant (Powered by IBM watsonx.ai)
- **Senior Developer Mentor Role**: Behaves as a senior software engineer, technical mentor, and repository onboarding guide.
- **15-Category Intent Classifier**: Classifies queries internally (`LEARNING_ROADMAP`, `REQUEST_FLOW`, `FEATURE_EXPLANATION`, `FILE_EXPLANATION`, `ARCHITECTURE_EXPLANATION`, `CHANGE_IMPACT`, `DEBUGGING`, etc.) before assembling context.
- **Repository Fact Auditing**: Strictly distinguishes between verified repository facts and general framework concepts, labeling items as:
  - `[IMPLEMENTED]`
  - `[PARTIALLY IMPLEMENTED]`
  - `[NOT IMPLEMENTED — NEXT CONCEPT]`
- **Target File Non-Existence Handling**: Explicitly informs the developer if a queried file (e.g., `AuthService.cs`) does not exist and redirects them to the actual implementation files.
- **Conversation State Continuity**: Maintains active learning level, topic, and context across turns using session IDs, enabling seamless `"Continue."` and follow-up commands.
- **Interactive Quizzing & Mini Exercises**: Generates practical, repository-specific questions to verify code comprehension.
- **Change Impact Mode**: Predicts direct and indirect downstream blast radius, risk factors, and recommended verification steps.
- **Offline Fallback Mode**: Delivers deterministic, repository-grounded structured context when live AI credentials are not configured.

### 4. Guided Developer Onboarding
- **Role-Based Curricula**: Tailors learning paths to developer specialization (`Backend Developer`, `Frontend Developer`, `Full Stack Developer`, `QA Engineer`).
- **Interactive Step Viewer**: Presents technical learning objectives, system rationale, relevant source files, and AI architectural explanations.
- **Real-Time Progress Tracking**: Persists step completion in SQL Server and calculates dynamic completion percentages (`██████░░░░ 60%`).

### 5. Starter Tasks & First Contribution
- **Low-Risk Task Discovery**: Discovers beginner-friendly tasks with low downstream coupling (&lt;3 file blast radius).
- **First Contribution Guidance**: Outlines task description, rationale, suggested first steps, existing code pattern references, and change-impact matrices.
- **Assistant Deep Linking**: Pre-fills context-aware questions directly into the Assistant with a single click.

---

## Technology Stack

### Frontend
- **React 18/19** (Component-driven UI)
- **TypeScript** (Strict type safety and interfaces)
- **Vite** (Fast dev server and optimized production bundling)
- **Vanilla CSS Design System** (Dark developer platform theme, glassmorphic accents, monospaced badges, responsive layouts)

### Backend
- **ASP.NET Core Web API (.NET 10.0)** (RESTful controllers, dependency injection, CORS middleware)
- **C# 14** (Pattern matching, asynchronous task orchestration, LINQ)

### Data Persistence
- **Microsoft SQL Server / LocalDB** (Relational repository storage, analyzed files, modules, dependencies, onboarding state, conversations)
- **Entity Framework Core 10.0** (Code-first schema migrations, parameterized queries, relationship mapping)

### AI & LLM Engine
- **IBM watsonx.ai** (Cloud foundation model inference)
- **IBM Granite / Meta-Llama Models** (`meta-llama/llama-3-3-70b-instruct` / `ibm/granite-3-8b-instruct`)
- **IBM Cloud IAM** (Token exchange and regional API orchestration)

---

## Architecture

### System Architecture Flow
```
User / Browser
      ↓
React + Vite Frontend (Port 5173)
      ↓ REST / JSON
ASP.NET Core Web API (Port 5000)
      ├── Entity Framework Core 10.0 ──→ SQL Server / LocalDB (CodeCompassDb)
      ├── Repository Analyzer ──────────→ Public GitHub Repositories (git clone)
      └── Context Retrieval Service ────→ Ranked Code Excerpts & Fact Audit
                                                  ↓
                                      IBM watsonx.ai (REST API)
```

### AI Context & Grounding Pipeline
```
User Question
      ↓
Intent Classifier & Domain Detector (15 Intent Categories)
      ↓
Repository Intelligence Engine (Score Files, Modules, Dependencies)
      ↓
Feature Fact Audit (Implemented vs. Partially Implemented vs. Missing)
      ↓
Curated Context Assembly (Excludes Secrets, Injects Verified Source Snippets)
      ↓
IBM watsonx.ai Inference (Greedy decoding, regional fallback)
      ↓
Markdown Synthesis with Badges & Interactive Next-Step Action Chips
```

---

## AI Grounding

CodeCompass strictly avoids the hallucinations common in generic LLM coding assistants:

1. **Curated Context Assembly**: The Assistant never prompts the model in isolation. It scores repository files, modules, and dependency relationships, extracting verified code snippets up to token limits.
2. **Fact vs. General Knowledge Auditing**: CodeCompass explicitly tells the model what is implemented and what is missing in the target repository. If a pattern (like Refresh Tokens or MFA) is absent, the model explains it as a production next step rather than claiming it exists.
3. **Target File Verification**: If a user asks about a file not in the codebase (e.g. `AuthService.cs`), the engine intercepts the filename, confirms non-existence, and routes the explanation to the actual files.
4. **Secret-Bearing Exclusions**: The context retriever filters out `.env`, `secrets.json`, `.pem`, `id_rsa`, `.key`, and credential files before prompts are created.
5. **Deterministic Fallback**: If IBM watsonx is unconfigured or offline, CodeCompass automatically falls back to structured, repository-grounded markdown without breaking.

---

## Security

CodeCompass was validated against rigorous security criteria:

- **No Hard-Coded Secrets**: Zero API keys, Bearer tokens, or credentials committed to source code or returned via frontend endpoints.
- **Configuration & Environment-Based Credentials**: IBM watsonx keys and project IDs are injected through environment variables or configuration providers.
- **Repository URL Validation**: Enforces HTTPS GitHub URLs (`https://github.com/owner/repo`). Rejects command injection characters (`;`, `&`, `..`).
- **Sandboxed Operations**: Clones run in isolated GUID-named directories in temporary storage and are removed in `finally` cleanup blocks.
- **Sensitive File Masking**: Strips all `.env`, private keys, certificates, and secrets from AI prompts.
- **Database Safety**: All queries use parameterized Entity Framework Core LINQ statements, eliminating SQL injection vectors.
- **Input Validation & Safe Error Handling**: Validates route parameters, rejects empty questions with HTTP 400, and returns clean HTTP 404 for missing resources without exposing internal stack traces.
- **CORS Guardrails**: Configured exclusively for local frontend development origins (`http://localhost:5173`, `http://localhost:3000`).

---

## Demo Flow

A complete 3–5 minute hackathon demonstration:

1. **Overview Dashboard**: Open `http://localhost:5173` to view indexed repositories, language statistics, and developer evaluation benchmarks.
2. **Architecture Visualization**: Open **Architecture** to inspect layered module cards, inspect encapsulated files in the drawer, and trace dependency paths between frontend and backend.
3. **AI Architecture Explanation**: Open **Codebase Assistant** and ask:
   > *"Explain the architecture of this project like I am a beginner."*
4. **Authentication Learning Path**: Ask:
   > *"Teach me authentication from beginner to advanced using this repository."*
   Observe the 6-level roadmap with `[IMPLEMENTED]` and `[NOT IMPLEMENTED — NEXT CONCEPT]` status badges.
5. **Request Flow Walkthrough**: Ask:
   > *"Trace a request from the frontend to the backend."*
   Observe the step-by-step trace from UI components through reverse proxy to backend handlers with verified code snippets.
6. **Change Impact Analysis**: Switch to Change Impact mode and ask:
   > *"What happens if I modify this feature?"*
   Observe direct, potential, and needs-verification blast-radius tables.
7. **Guided Onboarding**: Open **Onboarding**, switch between developer roles (`Backend Developer`, `Frontend Developer`, `Full Stack Developer`, `QA Engineer`), review step technical details, and click **Mark Complete ✓** to see real-time progress update.
8. **Starter Tasks & First Contribution**: Open **Starter Tasks**, view recommended beginner tasks with low coupling, inspect existing code patterns, and click **Ask Assistant About This Task ◆** to deep-link back to the Assistant.

---

## Setup & Installation

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (`dotnet --version`)
- [Node.js 18+](https://nodejs.org/) & npm (`node -v`)
- SQL Server LocalDB (`sqllocaldb start MSSQLLocalDB`)
- Git CLI (`git --version`)

### 1. Database Setup
Ensure LocalDB is started and update the database schema:
```powershell
sqllocaldb start MSSQLLocalDB

cd backend\CodeCompass.Api
dotnet ef database update
```

### 2. Run the Backend API
```powershell
cd backend\CodeCompass.Api
dotnet run --urls "http://localhost:5000"
```
Verify the API health:
```powershell
curl http://localhost:5000/api/health
```

### 3. Run the Frontend Client
In a new terminal:
```powershell
cd frontend
npm install
npm run dev
```
Open `http://localhost:5173` in your browser.

### 4. Optional: IBM watsonx.ai Configuration
Set your IBM watsonx credentials in your environment or in `backend/CodeCompass.Api/appsettings.json`:
```powershell
$env:WatsonX__ApiKey="<YOUR_IBM_CLOUD_API_KEY>"
$env:WatsonX__ProjectId="<YOUR_WATSONX_PROJECT_ID>"
$env:WatsonX__Url="https://eu-de.ml.cloud.ibm.com"
$env:WatsonX__ModelId="meta-llama/llama-3-3-70b-instruct"
```
*Note: If omitted, CodeCompass automatically operates in repository-grounded fallback mode.*

### 5. Build Verification
```powershell
# Backend (Release)
dotnet build backend/CodeCompass.Api -c Release

# Frontend (Production bundle)
npm --prefix frontend run build
```
