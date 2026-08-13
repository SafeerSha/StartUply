# StartUply / TranspileAI — Complete Application Knowledge Base & Documentation

**StartUply** (branded as **TranspileAI**) is an AI-powered code transformation, transpilation, and auto-generation platform. It allows developers to clone public or private GitHub repositories, inspect repository structures, convert codebases between tech stacks (e.g., React to Vue, Express to Django), auto-generate matching backend/frontend starter projects, and download the resulting projects as `.zip` archives with real-time progress feedback driven by SignalR WebSockets.

---

## Table of Contents
1. [Architecture Overview](#architecture-overview)
2. [Tech Stack & Dependencies](#tech-stack--dependencies)
3. [System Component Diagram](#system-component-diagram)
4. [Backend Deep-Dive (StartUply)](#backend-deep-dive-startuply)
   - [Clean Architecture Layer Breakdown](#clean-architecture-layer-breakdown)
   - [AI Integration & Prompt Parsing (OpenRouter / Gemini)](#ai-integration--prompt-parsing-openrouter--gemini)
   - [Git Operation Engine (LibGit2Sharp)](#git-operation-engine-libgit2sharp)
   - [SignalR Progress Tracking](#signalr-progress-tracking)
5. [Frontend Deep-Dive (StartUply UI)](#frontend-deep-dive-startuply-ui)
   - [App Router & State Management](#app-router--state-management)
   - [Service Layer (ProjectService)](#service-layer-projectservice)
   - [UI Components & Dialogs](#ui-components--dialogs)
   - [Glassmorphism & Animation Design System](#glassmorphism--animation-design-system)
6. [Complete Core Workflows](#complete-core-workflows)
   - [Workflow 1: Repo Structure Extraction](#workflow-1-repo-structure-extraction)
   - [Workflow 2: Tech Stack Conversion](#workflow-2-tech-stack-conversion)
   - [Workflow 3: Frontend / Backend Code Generation](#workflow-3-frontend--backend-code-generation)
   - [Workflow 4: Private Repository Authentication](#workflow-4-private-repository-authentication)
7. [REST API Specifications & Data Contracts](#rest-api-specifications--data-contracts)
8. [Environment Configuration & Deployment](#environment-configuration--deployment)
9. [Local Development Setup](#local-development-setup)

---

## Architecture Overview

StartUply is architected as a decoupled full-stack application:

* **Frontend Layer (`StartUply_UI`)**: Built with **Next.js 16 (App Router)** and **React 19**, acting as a single-page interactive control center. It connects to the backend via HTTP REST endpoints and persistent WebSockets via **ASP.NET Core SignalR**.
* **Backend Layer (`StartUply`)**: Built with **.NET 8** following **Clean Architecture** principles (`Domain`, `Application`, `Infrastructure`, `Presentation`). Handles repository cloning, AI code processing via OpenRouter API, file parsing, and file compression.
* **AI & Transformation Layer**: Integrates with Google's `gemini-2.0-flash-exp` model via OpenRouter API to process code transformations and format outputs using structured file delimiters (`---FILE: relative/path ---`).

```
┌────────────────────────────────────────────────────────┐
│                     TranspileAI UI                     │
│         Next.js 16 (App Router) + React 19 + TS        │
└──────────────────────────┬─────────────────────────────┘
                           │
           HTTP REST / JSON│SignalR WebSockets (Progress)
                           ▼
┌────────────────────────────────────────────────────────┐
│                   StartUply Backend                    │
│             ASP.NET Core .NET 8 Web API                │
│       (Clean Architecture: Pres/Infra/App/Domain)      │
└──────────────┬──────────────────────────┬──────────────┘
               │                          │
               ▼                          ▼
  ┌────────────────────────┐  ┌────────────────────────┐
  │ OpenRouter AI Engine   │  │   Git Repo Processing  │
  │ (Gemini 2.0 Flash)     │  │   (LibGit2Sharp)       │
  └────────────────────────┘  └────────────────────────┘
```

---

## Tech Stack & Dependencies

### Backend (`StartUply`)

| Component | Technology | Purpose |
| :--- | :--- | :--- |
| **Framework** | .NET 8 / C# | Core Web API server |
| **Architecture** | Clean Architecture | Layer separation (`Presentation`, `Infrastructure`, `Application`, `Domain`) |
| **Git Engine** | `LibGit2Sharp` | In-memory and disk cloning for public/private Git repositories |
| **AI Integration** | `HttpClient` + OpenRouter API | Integration with `google/gemini-2.0-flash-exp:free` model |
| **Real-Time Engine** | ASP.NET Core SignalR | WebSocket streaming hub for task execution progress |
| **API Documentation**| Swagger / OpenAPI | Endpoints exploration in development |
| **Containerization** | Docker (Multi-stage build) | Deployment-ready container targeting Render/Cloud hosts |

### Frontend (`StartUply_UI`)

| Component | Technology | Purpose |
| :--- | :--- | :--- |
| **Framework** | Next.js 16.0.7 (App Router) | Web application framework |
| **UI Library** | React 19.2.0 | Reactive component rendering |
| **Language** | TypeScript 5.x | Type safety |
| **Styling** | Tailwind CSS v4 + Custom CSS | Glassmorphism UI and custom keyframe animations |
| **Real-Time Client** | `@microsoft/signalr` v10.0.0 | Live WebSocket progress streaming |
| **Icons** | `lucide-react` | UI Iconography |
| **Notifications** | `react-hot-toast` | Toast alerts and user feedback |

---

## System Component Diagram

```
StartUply Solution
├── StartUply.Domain
│   └── Entities/BaseEntity.cs
├── StartUply.Application
│   └── Interfaces/
│       ├── IAIService.cs
│       └── IRepository.cs
├── StartUply.Infrastructure
│   └── Persistence/
│       ├── AIService.cs (OpenRouter API client with retries)
│       └── Repository.cs
└── StartUply.Presentation
    ├── Controllers/ProjectController.cs (REST endpoints)
    ├── Hubs/ProgressHub.cs (SignalR hub)
    └── Program.cs (CORS, DI, pipeline configuration)

StartUply_UI Solution
├── src/
│   ├── app/
│   │   ├── globals.css (Design system & animations)
│   │   ├── layout.tsx (HTML wrapper & toasts)
│   │   └── page.tsx (Main application controller)
│   ├── components/
│   │   ├── CredentialsModal.tsx
│   │   ├── DownloadModal.tsx
│   │   ├── ExtractionModal.tsx
│   │   ├── ProgressModal.tsx
│   │   └── StructureModal.tsx
│   └── services/
│       └── projectService.ts (REST API & SignalR service)
```

---

## Backend Deep-Dive (StartUply)

### Clean Architecture Layer Breakdown

1. **Domain (`StartUply.Domain`)**:
   - Contains core business entities (`BaseEntity.cs`) defining foundational properties (`Id`, `CreatedAt`, `UpdatedAt`).

2. **Application (`StartUply.Application`)**:
   - Defines system interfaces (`IAIService.cs`, `IRepository.cs`).
   - `IAIService` outlines method signatures:
     - `ConvertCodeAsync(code, fromDomain, toDomain, progressCallback)`
     - `GenerateBackendAsync(frontendCode, targetDomain, progressCallback)`
     - `GenerateBaseProjectAsync(domain, progressCallback)`

3. **Infrastructure (`StartUply.Infrastructure`)**:
   - Implements `IAIService` via `AIService.cs`.
   - Sends prompt requests to `https://openrouter.ai/api/v1/chat/completions`.
   - Implements exponential backoff retry logic (up to 5 retries) for handling `429 TooManyRequests` rate limits on free AI tiers.

4. **Presentation (`StartUply.Presentation`)**:
   - `ProjectController.cs`: Controller handling project extraction, conversion, generation, and file downloading.
   - `ProgressHub.cs`: SignalR hub mapping clients for progress callbacks (`ReceiveProgress`).
   - `Program.cs`: Sets CORS policies (`AllowAll`), configures dynamic port binding via `PORT` environment variable (default: `8080`), and maps `/progressHub`.

---

### AI Integration & Prompt Parsing (OpenRouter / Gemini)

The application instructs the AI model to return multi-file outputs formatted with standardized delimiters:

```text
---FILE: package.json ---
{
  "name": "generated-app"
}
---FILE: src/index.js ---
console.log("Hello World");
```

`ProjectController.ParseConvertedFiles()` splits response text by `---FILE:`, extracts the target relative file path, creates the corresponding folder hierarchy under temp storage, and writes the contents to disk.

---

### Git Operation Engine (LibGit2Sharp)

- GitHub repositories are cloned locally using `LibGit2Sharp.Repository.Clone`.
- When cloning public repositories, standard anonymous access is attempted.
- If `LibGit2SharpException` indicates an authentication failure (`401 Unauthorized` / missing credentials), `ProjectController` intercepts the exception and throws `AuthenticationRequiredException`, prompting the UI to ask the user for Git credentials (Username & Personal Access Token / Password).
- Cloned files are sanitized to ignore `.git`, `node_modules`, `dist`, `build`, and non-relevant binary file extensions.

---

### SignalR Progress Tracking

During long-running AI code operations:
1. Client generates/receives a SignalR `connectionId`.
2. Request payload includes `connectionId`.
3. `ProjectController.CreateProgressCallback()` broadcasts progress status updates via `_hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", message, percentage)`.
4. Client UI reflects progress in real time within `ProgressModal`.

---

## Frontend Deep-Dive (StartUply UI)

### App Router & State Management

- `src/app/page.tsx` acts as the primary orchestrator component.
- Maintains state for:
  - **Selected Mode**: `conversion` vs `generate`.
  - **Generation Type**: `frontend` vs `backend`.
  - **Framework Choices**: Selected from lists of 50+ frontend and 15+ backend options.
  - **GitHub URL & Credentials**: Username & Personal Access Token state.
  - **Modals State**: Controls open/close states for extraction, repo structure, progress, credentials, and download modals.

---

### Service Layer (ProjectService)

`src/services/projectService.ts` is a singleton client class managing REST communication and SignalR WebSockets:

- `initializeProgressTracking(onProgress)`: Establishes a persistent SignalR WebSocket connection to `${NEXT_PUBLIC_BASE_URL}/progressHub`.
- `extractProjectStructure(githubUrl, username, password)`: POSTs to `/api/project/extract`.
- `processProject(payload)`: POSTs to `/api/project/process` to initiate AI tasks.
- `downloadProjectZip(id)`: Initiates binary zip download from `/api/project/download/{id}`.
- `pushToGithub(params)`: Sends request to `/api/project/pushToGithub` to create a GitHub repo and push code directly.

---

### UI Components & Dialogs

1. **`ExtractionModal.tsx`**: Renders an animated loading screen featuring git branch icons and floating document effects during repository scanning.
2. **`StructureModal.tsx`**: Interactive folder tree rendering repository files (`📂` folders and `📄` files).
3. **`CredentialsModal.tsx`**: Prompts the user for GitHub credentials when targeting private repositories.
4. **`ProgressModal.tsx`**: Real-time progress bar displaying live percentage and step updates received via SignalR callbacks.
5. **`DownloadModal.tsx`**: Presents the tree structure of generated/converted project files and provides a button to download the project `.zip`.

---

### Glassmorphism & Animation Design System

Defined in `src/app/globals.css`:
- **Theme**: Dark modern glassmorphism (`bg-slate-900`, `backdrop-blur-xl`, semi-transparent cards `bg-white/10`, subtle borders `border-white/20`).
- **Keyframe Animations**:
  - `@keyframes extraction-fly`: Flying document transition from git node to target folder.
  - `@keyframes progress-fly`: Smooth file transition between source and destination folders.

---

## Complete Core Workflows

### Workflow 1: Repo Structure Extraction
1. User enters a GitHub URL (e.g., `https://github.com/owner/repo`).
2. User clicks **Extract**.
3. `ExtractionModal` opens with scan animations.
4. `ProjectService.extractProjectStructure()` sends request to `/api/project/extract`.
5. If public: backend scans repo, returns tree data, and opens `StructureModal`.
6. If private without credentials: backend returns `401`, triggering `CredentialsModal`.

### Workflow 2: Tech Stack Conversion
1. User chooses **Conversion** mode.
2. User enters GitHub URL, **Source Tech** (e.g., React), and **Target Tech** (e.g., Vue).
3. User clicks **Go**.
4. SignalR connection is verified and `connectionId` is attached to payload.
5. Backend clones repo, aggregates source files, sends conversion prompt to AI, and streams live progress.
6. `ProgressModal` updates progress bar in real time (`0%` → `100%`).
7. Upon completion, `DownloadModal` opens displaying generated files and enabling `.zip` download.

### Workflow 3: Frontend / Backend Code Generation
1. User chooses **Generate** mode.
2. User selects **Frontend** or **Backend**.
   - **Frontend**: Selects target framework; backend generates a base starter application via AI.
   - **Backend**: Enters frontend GitHub URL and target backend framework; backend analyzes frontend code and generates complementary backend APIs via AI.
3. Live progress is streamed to `ProgressModal` via SignalR until complete.
4. `DownloadModal` opens for file tree preview and download.

### Workflow 4: Private Repository Authentication
1. User attempts extraction or conversion on a private repo without credentials.
2. Backend catches `LibGit2SharpException` and responds with HTTP `401`.
3. UI catches `401` error, saves pending request parameters, and displays `CredentialsModal`.
4. User submits Username and Personal Access Token (PAT).
5. Frontend retries original operation including credentials in payload.

---

## REST API Specifications & Data Contracts

### REST Endpoints

| Endpoint | Method | Payload | Description |
| :--- | :--- | :--- | :--- |
| `/api/project/extract` | `POST` | `CloneRequest` | Scans Git repo and returns tree hierarchy |
| `/api/project/process` | `POST` | `ProcessRequest` | Executes conversion or code generation task |
| `/api/project/clone` | `POST` | `CloneRequest` | Clones repository to temporary disk path |
| `/api/project/createBase` | `POST` | `CreateBaseRequest` | Generates starter base project via AI |
| `/api/project/convert` | `POST` | `ConvertRequest` | Triggers project framework conversion |
| `/api/project/generate` | `POST` | `GenerateRequest` | Generates backend code from frontend structure |
| `/api/project/progress/{taskId}` | `GET` | None | Fallback polling for task progress status |
| `/api/project/download/{id}` | `GET` | None | Downloads generated project as a `.zip` file |
| `/api/project/pushToGithub` | `POST` | `PushToGithubRequest` | Creates a new GitHub repository and pushes code directly |

---

### Data Models & Schemas

#### 1. `CloneRequest`
```json
{
  "url": "https://github.com/username/repo",
  "username": "git-user",
  "password": "personal-access-token"
}
```

#### 2. `ProcessRequest`
```json
{
  "githubUrl": "https://github.com/username/repo",
  "mode": "conversion",
  "type": "backend",
  "fromFramework": "React",
  "targetFramework": "Vue",
  "connectionId": "signalr-connection-id",
  "username": "git-user",
  "password": "personal-access-token"
}
```

#### 3. `DirectoryItem`
```json
{
  "name": "src",
  "type": "directory",
  "path": "src",
  "children": [
    {
      "name": "App.tsx",
      "type": "file",
      "path": "src/App.tsx",
      "children": null
    }
  ]
}
```

#### 4. `ProgressStatus`
```json
{
  "message": "Generating backend code...",
  "percentage": 60,
  "timestamp": "2026-08-13T16:50:00Z"
}
```

#### 5. `PushToGithubRequest`
```json
{
  "id": "project-session-id",
  "repoName": "my-converted-app",
  "isPrivate": true,
  "description": "Generated with TranspileAI",
  "githubToken": "ghp_personal_access_token",
  "connectionId": "signalr-connection-id"
}
```

---

## Environment Configuration & Deployment

### Backend Environment Variables

- `OpenRouter:ApiKey`: Secret key for OpenRouter API access (configured in `appsettings.json` or environment variables).
- `PORT`: Server port binding (default: `8080`, automatically supplied on Render).

### Frontend Environment Variables

- `NEXT_PUBLIC_BASE_URL`: Base HTTP & WebSocket URL pointing to backend service (e.g., `http://localhost:8080` or `https://backend.onrender.com`).

---

## Local Development Setup

### 1. Backend Setup (`StartUply`)

**Prerequisites**: .NET 8.0 SDK

```bash
# Navigate to backend directory
cd StartUply

# Restore dependencies
dotnet restore

# Configure OpenRouter API Key (in appsettings.json or via environment variable)
# "OpenRouter": { "ApiKey": "YOUR_OPENROUTER_API_KEY" }

# Run backend project
dotnet run --project StartUply.Presentation
```

Backend Swagger UI will be available at `http://localhost:5000/swagger` or specified port.

---

### 2. Frontend Setup (`StartUply_UI`)

**Prerequisites**: Node.js v18+ & npm / pnpm / yarn

```bash
# Navigate to frontend directory
cd StartUply_UI

# Install dependencies
npm install

# Create environment file (.env.local)
echo "NEXT_PUBLIC_BASE_URL=http://localhost:8080" > .env.local

# Start development server
npm run dev
```

Frontend application will be accessible at `http://localhost:3000`.
