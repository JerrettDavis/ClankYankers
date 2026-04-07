# ClankYankers

> **A browser-based orchestration platform for agentic CLI tools**
> Run, manage, and monitor AI agent CLIs (Claude Code, Ollama, Codex, Gemini, and more) from a unified browser terminal.

[![CI](https://github.com/JerrettDavis/ClankYankers/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/ClankYankers/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JerrettDavis/ClankYankers/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/JerrettDavis/ClankYankers/security/code-scanning)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

---

## Overview

ClankYankers is a browser-based orchestration platform that provides a unified terminal interface for interacting with agentic CLI tools across multiple execution environments. Whether you're running Claude Code locally, spinning up Ollama inside a Docker container, or experimenting with multiple agents side-by-side, ClankYankers gives you a single, consistent interface.

### Key Features

- **Browser-native terminal** — Full interactive terminal powered by xterm.js; ANSI rendering, keyboard passthrough, and resize support
- **Multiple execution backplanes** — Run sessions on your local machine or inside Docker containers
- **Agent connectors** — Built-in support for Claude Code, Ollama, OpenClaw, Codex, and Gemini CLI
- **Session management** — Start, stop, reconnect to, and run multiple sessions concurrently
- **Plugin-driven extensibility** — Add new backplanes, connectors, and lifecycle hooks without modifying the core
- **Configuration UI** — Manage backplanes, hosts, and connectors from the browser
- **Event-driven lifecycle** — Hook into session start/stop, command execution, and output events

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/) (for the web frontend)
- Docker (optional, for Docker backplane support)

### Run the app

```bash
git clone https://github.com/JerrettDavis/ClankYankers.git
cd ClankYankers

# Start the backend server and SPA together in development
dotnet run --project apps/server/ClankYankers.Server
```

`dotnet run` now launches the Vite SPA automatically in development and opens the browser through the ASP.NET Core host. The web UI is served through the SPA proxy at `http://127.0.0.1:5173` and the API server remains available from the ASP.NET Core app at `http://localhost:5023`.

If you want to work on the frontend separately, you can still run it directly:

```bash
cd apps/web
npm install
npm run dev
```

### Run the tests

```bash
# Unit and integration tests
dotnet test ClankYankers.slnx

# Frontend tests
cd apps/web
npm run test

# E2E tests
cd apps/web
npm run test:e2e
```

---

## Architecture

ClankYankers is a hybrid stack — a .NET 10 backend for process control and streaming, paired with a React + xterm.js frontend for the terminal UI.

```
Browser (React + xterm.js)
    ↕ WebSocket (bi-directional)
Application Server (.NET 10 / ASP.NET Core)
    ├── Session Orchestrator
    ├── Backplane Layer (Local / Docker)
    └── Connector Layer (Claude / Ollama / ...)
         ↓
Execution Targets (Local machine / Docker containers)
```

### Solution Structure

| Path | Purpose |
|------|---------|
| `apps/server/ClankYankers.Server` | ASP.NET Core backend — session orchestration, PTY management, WebSocket API |
| `apps/web` | React + TypeScript frontend — terminal UI, session management |
| `tests/ClankYankers.Server.UnitTests` | Unit tests for the server |
| `tests/ClankYankers.Server.IntegrationTests` | Integration tests (local & Docker backplanes) |

### WebSocket API

Sessions communicate over WebSocket at `/ws/session/{sessionId}`.

**Client → Server**
```json
{ "type": "input", "data": "ls -la\n" }
{ "type": "resize", "cols": 120, "rows": 40 }
```

**Server → Client**
```json
{ "type": "output", "data": "..." }
{ "type": "exit", "code": 0 }
```

---

## Milestones

- [x] **M0** — Project scaffold, WebSocket connection
- [x] **M1** — Local terminal execution (PTY, STDIN/STDOUT streaming)
- [x] **M2** — Session management, reconnect support
- [x] **M3** — Connector abstraction (Claude Code)
- [x] **M4** — Docker backplane
- [ ] **M5** — Multi-connector support (OpenClaw, Ollama, Codex, Gemini)
- [ ] **M6** — Configuration system + UI
- [ ] **M7** — Plugin system extraction
- [ ] **M8** — Hardening + packaging (Docker image, release artifacts)

---

## Documentation

- [Business Requirements (BRD)](docs/BRD.md)
- [Technical Design (DESIGN.md)](docs/DESIGN.md)
- [Implementation Plan (PLAN.md)](docs/PLAN.md)
- [API Documentation](https://jerrettdavis.github.io/ClankYankers)

---

## Contributing

Contributions are welcome! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

1. Fork the repository
2. Create a feature branch (`git checkout -b feat/amazing-feature`)
3. Commit your changes using [Conventional Commits](https://www.conventionalcommits.org/)
4. Push to the branch (`git push origin feat/amazing-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
