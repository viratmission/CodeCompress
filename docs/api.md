# CodeCompass API Reference

Base URL: `http://localhost:5000`  
Interactive OpenAPI / Swagger Documentation: `http://localhost:5000/swagger`

---

## 1. System Health

### `GET /api/health`
Checks backend and AI provider connection status.

**Response (200 OK):**
```json
{
  "status": "healthy",
  "service": "CodeCompass API",
  "watsonxConfigured": true,
  "aiProvider": "IBM watsonx • Granite"
}
```

---

## 2. Repositories & Analysis

### `GET /api/repositories`
Lists all analyzed repositories stored in the database.

**Response (200 OK):**
```json
[
  {
    "id": 2,
    "name": "TodoApi",
    "gitUrl": "https://github.com/davidfowl/TodoApi",
    "description": "Analyzed repository",
    "primaryLanguage": "C#",
    "createdAt": "2026-09-26T07:15:30Z"
  }
]
```

### `POST /api/repositories/analyze`
Clones and indexes a public GitHub repository.

**Request:**
```json
{
  "gitUrl": "https://github.com/davidfowl/TodoApi"
}
```

**Response (200 OK):**
```json
{
  "repositoryId": 2,
  "name": "TodoApi",
  "totalFiles": 78,
  "totalDirectories": 12,
  "totalModules": 6,
  "totalDependencies": 41,
  "primaryLanguage": "C#"
}
```

**Errors:**
- `400 Bad Request`: Missing URL, invalid URI, non-GitHub host, or disallowed characters (`;`, `&`, `..`).
- `422 Unprocessable Entity`: Repository not found or git clone failed.

### `GET /api/repositories/{id}/analysis`
Returns file counts, detected modules, dependencies, and language distribution.

---

## 3. Architecture Intelligence

### `GET /api/repositories/{id}/architecture`
Returns graph nodes and edges for multi-layer architectural visualization.

**Response (200 OK):**
```json
{
  "repositoryId": 2,
  "repositoryName": "TodoApi",
  "nodes": [
    {
      "id": "module-4",
      "type": "module",
      "name": "Client",
      "path": "Todo.Web/Client",
      "description": "Detected Frontend module at Todo.Web/Client",
      "moduleType": "Frontend",
      "layer": "Presentation",
      "fileCount": 10,
      "outgoingDeps": 1,
      "incomingDeps": 0
    }
  ],
  "edges": [
    {
      "id": "edge-4-1",
      "source": "module-4",
      "target": "module-1",
      "relationshipType": "import",
      "weight": 2
    }
  ],
  "layers": ["Presentation", "Backend", "Data Access", "Shared"]
}
```

### `GET /api/repositories/{id}/modules/{moduleId}`
Returns detailed file list and dependencies for a single module. Supports both integer IDs (`4`) and graph node strings (`module-4`).

**Response (200 OK):**
```json
{
  "id": 4,
  "name": "Client",
  "path": "Todo.Web/Client",
  "moduleType": "Frontend",
  "description": "Detected Frontend module at Todo.Web/Client",
  "layer": "Presentation",
  "fileCount": 10,
  "files": [
    {
      "id": 66,
      "filePath": "Todo.Web/Client/TodoClient.cs",
      "fileName": "TodoClient.cs",
      "extension": ".cs",
      "language": "C#",
      "fileSize": 2382,
      "isDirectory": false
    }
  ],
  "outgoingDependencies": [],
  "incomingDependencies": [],
  "relatedModules": []
}
```

### `GET /api/repositories/{id}/flow?from={source}&to={target}`
Computes the shortest dependency flow path between two modules using BFS.

---

## 4. AI Codebase Assistant

### `POST /api/repositories/{id}/assistant/ask`
Submits a query to the repository-grounded Assistant (powered by IBM watsonx.ai). Supports multi-turn memory via `sessionId`.

**Request:**
```json
{
  "question": "Teach me authentication from beginner to advanced using this repository.",
  "questionType": "ask",
  "sessionId": "demo-session-uuid"
}
```

**Response (200 OK):**
```json
{
  "answer": "# Authentication Learning Path: Beginner to Advanced\n...",
  "questionType": "ask",
  "references": [
    {
      "filePath": "Todo.Web/Server/Authentication/AuthenticationExtensions.cs",
      "language": "C#",
      "reason": "Matched terms: authentication, auth"
    }
  ],
  "modules": [
    {
      "moduleId": 1,
      "moduleName": "Server",
      "modulePath": "Todo.Web/Server",
      "moduleType": "Backend"
    }
  ],
  "changeImpact": [],
  "context": {
    "filesExamined": 21,
    "modulesExamined": 6,
    "dependenciesExamined": 30,
    "sourceCodeRead": true,
    "repositoryName": "TodoApi"
  },
  "sessionId": "demo-session-uuid"
}
```

**Change Impact Analysis:**
Set `"questionType": "change-impact"` to receive structured change impact assessments populated in `changeImpact`:
```json
{
  "changeImpact": [
    {
      "filePath": "Todo.Web/Server/Authentication/AuthenticationExtensions.cs",
      "moduleName": "Server",
      "impact": "direct",
      "reason": "Must register new token handler in AddAuthentication pipeline."
    }
  ]
}
```

### `GET /api/repositories/{id}/assistant/suggestions`
Returns grounded question suggestions based on actual repository modules and entry points.

### `GET /api/repositories/{id}/assistant/history?sessionId={sessionId}`
Retrieves previous conversation turns for a given session.

---

## 5. Guided Developer Onboarding

### `GET /api/repositories/{repositoryId}/onboarding/{role}`
Returns or creates the role-tailored onboarding path.

**Supported Roles**:
- `Backend Developer`
- `Frontend Developer`
- `Full Stack Developer`
- `QA Engineer`

**Response (200 OK):**
```json
{
  "id": 1,
  "repositoryId": 2,
  "role": "Backend Developer",
  "title": "Backend Developer Onboarding Guide for TodoApi",
  "userOnboardingId": 1,
  "progressPercentage": 60.0,
  "steps": [
    {
      "id": 1001,
      "title": "Project Structure & Solution Architecture",
      "order": 1,
      "stepType": "overview",
      "status": "completed"
    }
  ],
  "relatedModules": [],
  "relevantFiles": []
}
```

### `GET /api/onboarding/{onboardingId}/steps/{stepId}`
Returns detailed learning objectives, why it matters, relevant files, and AI explanation.

### `POST /api/onboarding/{onboardingId}/steps/{stepId}/complete`
Marks a learning step completed and recalculates overall onboarding progress.

**Response (200 OK):**
```json
{
  "userOnboardingId": 1,
  "stepId": 1001,
  "status": "completed",
  "completedAt": "2026-09-27T14:37:00Z",
  "progressPercentage": 60.0,
  "completedSteps": 3,
  "totalSteps": 5,
  "isFinished": false
}
```

---

## 6. Starter Tasks

### `GET /api/repositories/{repositoryId}/starter-tasks`
Returns low-risk beginner tasks identified by repository intelligence.

**Response (200 OK):**
```json
[
  {
    "id": 1,
    "repositoryId": 2,
    "title": "Add validation attribute to todo model",
    "description": "Add model validation annotations to validate request bodies before processing.",
    "difficulty": "Easy",
    "reason": "Low-risk change that introduces the project's existing validation pattern.",
    "relatedModuleName": "General",
    "fileCount": 3
  }
]
```

### `GET /api/starter-tasks/{id}`
Returns complete task specification, suggested first steps, code pattern references, and pre-computed change-impact table.
