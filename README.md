# AIChat - AI Chat Application

A modern AI chat application built with React TypeScript frontend and ASP.NET Core backend, integrating with Azure OpenAI for conversational AI capabilities.

## Features

- 🤖 **Multi-Model Support** - Switch between different Azure OpenAI models (GPT-4o, GPT-4o Mini, GPT-4, etc.)
- 💬 **Multi-Conversation Support** - Create and manage multiple chat conversations
- ⚡ **Real-time Streaming** - Live streaming responses via SignalR (on-demand connections)
- 📱 **Responsive Design** - Works on desktop and mobile
- 🎨 **Modern UI** - Clean, minimal AI-native interface

## Project Structure

```
AIChat/
├── backend/
│   └── AIChat.Api/          # ASP.NET Core Web API
│       ├── Controllers/     # Models, memory, prompt profiles, media
│       ├── Hubs/           # SignalR hub for streaming
│       ├── Models/         # Data models
│       └── Services/       # Business logic & Azure OpenAI
│
├── frontend/                # React + TypeScript + Vite
│   └── src/
│       ├── components/     # UI components
│       ├── hooks/          # Custom React hooks
│       ├── services/       # API client
│       └── types/          # TypeScript interfaces
│
└── README.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22.12+](https://nodejs.org/)
- Azure OpenAI resource with deployed models

## Configuration

### Backend Configuration

Supply credentials through process environment variables only:

| Setting | Standard .NET name | Alternative name |
|---------|--------------------|------------------|
| Endpoint | `AzureOpenAI__Endpoint` | `AZURE_OPENAI_ENDPOINT` |
| API key | `AzureOpenAI__ApiKey` | `AZURE_OPENAI_API_KEY` |

The standard name takes precedence when both are set. JSON credentials are not loaded as a fallback. Direct `dotnet run` does not automatically load a `.env` file; configure its process environment first. Docker Compose maps the alternative variables into the standard names.

Set model IDs and Azure deployment names in [backend/AIChat.Api/config/models.json](backend/AIChat.Api/config/models.json). The application writes only this model catalog, with atomic replacement. Keys and endpoints are absent from the persisted catalog type. Other required JSON configuration files remain read-only application inputs. Authentication requires a configured access code; do not use production credentials in automated tests.

Model settings refresh after catalog changes. User/authentication mappings and other startup-only options require a process restart when edited.

## Getting Started

### 1. Start the Backend

```bash
cd backend/AIChat.Api
dotnet run
```

Backend will start at `http://localhost:5000`

### 2. Start the Frontend

```bash
cd frontend
npm install
npm run dev
```

Frontend will start at `http://localhost:5173`

### 3. Open the Application

Navigate to `http://localhost:5173` in your browser.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/models` | Get available AI models |
| PUT / DELETE | `/api/models`, `/api/models/{id}` | Admin model catalog changes |
| GET / POST | `/api/memory` | List or create user memories |
| PUT | `/api/memory/conversations/{id}/mode` | Enable/disable memory use and collection |
| GET | `/api/promptprofiles` | Built-in prompt profiles |
| SignalR | `/chathub` | Real-time streaming (on-demand) |

Conversation CRUD and message persistence are local IndexedDB operations, not backend endpoints.

## Architecture Highlights

### On-Demand SignalR Connections
The application creates SignalR connections only when streaming messages, and disconnects immediately after completion. This saves server resources by not maintaining persistent connections.

### Multi-Model Support
Users can switch between available chat models from the input toolbar. The selected model is passed with each message request.

### Memory Controls

Turning memory off cancels pending/queued extraction and excludes disabled-period history from later collection, including after re-enabling. Existing memories are not deleted. Requests already sent to a model provider cannot be recalled; cancellation is cooperative. If a required exclusion boundary is missing from supplied history, extraction is skipped rather than guessing.

Both Compose configurations persist memory, exclusion checkpoints, and pending snapshots. Before replacing existing containers, back up and migrate any old `data/extraction` or `data/pending` files into their new named volumes; adding a volume does not import the previous container's files. Do not remove volumes during routine upgrades.

## Technology Stack

### Frontend
- React 19 with TypeScript
- Vite for build tooling
- Tailwind CSS for styling
- SignalR client for real-time communication
- Lucide React for icons
- React Markdown for message rendering

### Backend
- ASP.NET Core 10
- SignalR for WebSocket connections
- Azure.AI.OpenAI SDK
- Browser-owned conversation history and per-user JSON memory storage

## Design System

| Element | Value |
|---------|-------|
| Primary Color | `#BF4435` |
| Fonts | Space Grotesk / DM Sans |
| Style | Graphite and paper-white workspace |

## Development Checks

```sh
dotnet test backend/AIChat.Api.Tests/AIChat.Api.Tests.csproj
npm --prefix frontend run build
npm --prefix frontend run lint
```

See [frontend/TESTING.md](frontend/TESTING.md) for isolated browser regression, [AGENTS.md](AGENTS.md) for shared agent instructions, and [REVIEW.md](REVIEW.md) for review findings and resolution status.

## License

MIT
