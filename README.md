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
│       ├── Controllers/     # REST API endpoints (Conversations, Models)
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

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- [Node.js 18+](https://nodejs.org/)
- Azure OpenAI resource with deployed models

## Configuration

### Backend Configuration

Copy `backend/AIChat.Api/config/azure-openai.example.json` to `backend/AIChat.Api/config/azure-openai.json` and fill in your Azure OpenAI credentials:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "ApiKey": "YOUR-API-KEY"
  }
}
```

**Note:** Add your deployed model names to `backend/AIChat.Api/config/models.json`. The `DeploymentName` should match your Azure OpenAI deployment name.

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
| GET | `/api/conversations` | List all conversations |
| POST | `/api/conversations` | Create new conversation |
| GET | `/api/conversations/{id}` | Get conversation with messages |
| DELETE | `/api/conversations/{id}` | Delete conversation |
| GET | `/api/models` | Get available AI models |
| SignalR | `/chathub` | Real-time streaming (on-demand) |

## Architecture Highlights

### On-Demand SignalR Connections
The application creates SignalR connections only when streaming messages, and disconnects immediately after completion. This saves server resources by not maintaining persistent connections.

### Multi-Model Support
Users can switch between different Azure OpenAI models from the header dropdown. The selected model is passed with each message request.

## Technology Stack

### Frontend
- React 18 with TypeScript
- Vite for build tooling
- Tailwind CSS for styling
- SignalR client for real-time communication
- Lucide React for icons
- React Markdown for message rendering

### Backend
- ASP.NET Core 8
- SignalR for WebSocket connections
- Azure.AI.OpenAI SDK
- In-memory conversation storage

## Design System

| Element | Value |
|---------|-------|
| Primary Color | `#2563EB` |
| Font | Inter |
| Style | AI-Native UI |

## License

MIT
