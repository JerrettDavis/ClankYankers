# **ClankYankers**

## **DESIGN.md (Technical Design Document)**

---

## **1. Overview**

ClankYankers is a browser-based orchestration platform that provides a unified terminal interface for interacting with agentic CLI tools across multiple execution environments (backplanes).

The system is designed around:

* **Terminal-first interaction model**
* **Backplane abstraction for execution environments**
* **Connector abstraction for agent CLIs**
* **Plugin-driven extensibility**
* **Event-driven lifecycle orchestration**

The architecture prioritizes:

* Fidelity to native CLI behavior
* Isolation of execution environments
* Extensibility without core rewrites

---

## **2. High-Level Architecture**

```
+--------------------------------------------------+
|                  Web Client (UI)                 |
|  - Terminal UI (xterm.js or equivalent)          |
|  - Session Manager UI                            |
|  - Config UI (Backplanes, Connectors)            |
+------------------------+-------------------------+
                         |
                         | WebSocket (bi-directional)
                         |
+------------------------v-------------------------+
|                 Application Server              |
|                                                |
|  Core Framework                                |
|   - Session Orchestrator                       |
|   - Plugin Host                                |
|   - Event Bus                                  |
|   - Contract Registry                          |
|                                                |
|  Backplane Layer                               |
|   - Local Backplane                            |
|   - Docker Backplane                           |
|                                                |
|  Connector Layer                               |
|   - Claude Connector                           |
|   - OpenClaw Connector                         |
|   - Codex Connector                            |
|   - Gemini Connector                           |
|   - Ollama Connector                           |
|                                                |
+------------------------+-------------------------+
                         |
                         |
               +---------v---------+
               | Execution Targets |
               | Local / Docker    |
               | VM / SSH (future) |
               +-------------------+
```

---

## **3. Technology Stack**

### Option A (Recommended Hybrid)

**Frontend**

* TypeScript
* React (or lightweight alternative)
* xterm.js (terminal emulation)

**Backend**

* .NET 10
* ASP.NET Core (minimal APIs)
* System.IO.Pipelines (streaming)
* PTY integration (ConPTY on Windows, pty on Unix)

**Why Hybrid**

* Terminal UX is significantly better in TS ecosystem
* .NET excels at:

  * Process control
  * Streaming
  * Plugin frameworks
  * Strong contracts

---

### Option B (Single Stack TS)

* Node.js backend
* node-pty
* WebSocket server
* Shared types

**Tradeoff**

* Faster iteration
* Weaker long-term structure for enterprise-grade plugin system

---

## **4. Core Concepts**

---

### 4.1 Session

A **Session** represents a live interactive CLI execution.

#### Properties

```
Session
- Id
- BackplaneId
- HostId
- ConnectorId
- State (Starting, Running, Stopped, Failed)
- CreatedAt
- Metadata
```

#### Responsibilities

* Manage lifecycle
* Stream terminal IO
* Execute hooks
* Emit events

---

### 4.2 Backplane

A **Backplane** defines *where execution occurs*.

#### Interface

```
IBackplane
- StartSessionAsync(SessionContext)
- StopSessionAsync(SessionId)
- ExecuteCommandAsync(...)
- StreamAsync(...)
```

#### Implementations (MVP)

* LocalBackplane
* DockerBackplane

---

### 4.3 Host

Represents a specific execution target within a backplane.

```
Host
- Id
- BackplaneId
- Configuration (JSON)
```

Examples:

* Local machine
* Docker daemon
* Remote Docker host

---

### 4.4 Connector

A **Connector** defines *how to run an agent CLI*.

#### Interface

```
IAgentConnector
- Name
- BuildCommand(SessionContext)
- GetEnvironmentVariables(...)
- GetWorkingDirectory(...)
- Hooks (optional)
```

#### Responsibilities

* CLI invocation
* Environment setup
* Optional lifecycle scripts

#### Non-Responsibilities

* No orchestration logic
* No behavior enforcement

---

### 4.5 Plugin

Plugins extend the system.

#### Capabilities

* Register backplanes
* Register connectors
* Subscribe to events
* Inject hooks

#### Structure

```
Plugin
- Name
- Version
- Register(IPluginContext)
```

---

## **5. Terminal Architecture**

---

### 5.1 Core Requirements

* Full duplex communication
* Raw input passthrough
* ANSI rendering
* Resize support
* Low latency

---

### 5.2 Flow

```
User Input (Browser)
   ↓
WebSocket
   ↓
Server PTY
   ↓
Process STDIN

Process STDOUT/ERR
   ↓
PTY
   ↓
WebSocket
   ↓
Terminal Renderer (xterm.js)
```

---

### 5.3 PTY Layer

#### Windows

* ConPTY

#### Linux/macOS

* pty / forkpty

---

### 5.4 Multiplexing

Each session gets:

* Dedicated PTY
* Dedicated WebSocket channel

---

## **6. Backplane Implementations**

---

### 6.1 Local Backplane

#### Responsibilities

* Spawn local processes
* Attach PTY
* Stream IO

#### Implementation Notes

* Use native process APIs
* Direct PTY binding

---

### 6.2 Docker Backplane

#### Responsibilities

* Create/manage containers
* Attach interactive shell
* Maintain container lifecycle

#### Flow

```
Create container
→ Attach TTY
→ Execute connector command
→ Stream IO
```

#### Key Features

* Optional Dockerfile provisioning
* Watchdog scripts
* Log streaming

---

## **7. Connector Implementations (MVP)**

---

### 7.1 Claude Connector

* Command: `claude`
* Supports:

  * Browser login flow
  * Token fallback

---

### 7.2 OpenClaw Connector

* Command: `openclaw`
* May include:

  * Session config overrides

---

### 7.3 Ollama Connector

* Command: `ollama run ...`
* Local model execution

---

### 7.4 Codex / Gemini / Copilot

* Wrapper commands
* Environment-driven configuration

---

## **8. Event System**

---

### 8.1 Purpose

* Decouple lifecycle logic
* Enable plugins and hooks

---

### 8.2 Event Types

```
SessionStarting
SessionStarted
SessionStopping
SessionStopped
SessionFailed

CommandExecuting
CommandCompleted

OutputReceived
ErrorReceived
```

---

### 8.3 Event Bus

```
IEventBus
- Publish(event)
- Subscribe(handler)
```

---

## **9. Hook System**

---

### 9.1 Hook Types

* Setup
* Teardown
* PreRun
* PostRun
* Watchdog (long-running)

---

### 9.2 Execution Model

```
Session Start
 → Setup Hooks
 → PreRun Hooks
 → Command Execution
 → PostRun Hooks
 → Teardown Hooks
```

---

## **10. Configuration Model**

---

### 10.1 Structure

```
Config
- Backplanes[]
- Hosts[]
- Connectors[]
- Defaults
```

---

### 10.2 Storage

MVP Options:

* JSON file
* Local storage (browser)
* Lightweight DB (SQLite)

---

## **11. WebSocket API**

---

### 11.1 Endpoints

```
/ws/session/{sessionId}
```

---

### 11.2 Messages

#### Client → Server

```
{
  type: "input",
  data: "ls -la\n"
}
```

```
{
  type: "resize",
  cols: 120,
  rows: 40
}
```

---

#### Server → Client

```
{
  type: "output",
  data: "..."
}
```

```
{
  type: "exit",
  code: 0
}
```

---

## **12. Security Model (MVP)**

* No credential persistence
* Explicit execution only
* Docker socket must be opt-in
* No sandboxing guarantees (user responsibility)

---

## **13. Deployment Architecture**

---

### 13.1 Modes

1. Local Dev
2. Docker Container
3. Remote Server

---

### 13.2 Docker Considerations

* Mount Docker socket (optional)
* Volume for config persistence

---

## **14. Observability (MVP-Light)**

* Session logs
* Basic metrics:

  * Start time
  * Duration
* Future:

  * Token usage
  * Agent metrics

---

## **15. Extensibility Strategy**

---

### 15.1 Add New Backplane

* Implement `IBackplane`
* Register via plugin

---

### 15.2 Add New Connector

* Implement `IAgentConnector`
* Define command + env

---

### 15.3 Add New Hooks

* Subscribe to events
* Inject lifecycle logic

---

## **16. Future Design Hooks (Important)**

These must exist in MVP even if unused:

* Multi-session orchestration hooks
* Experiment pipeline hooks
* Agent-to-agent messaging channel
* Session replay capture hooks

---

## **17. Tradeoffs and Decisions**

---

### Decision: Terminal-first design

* ✅ Maximum compatibility
* ❌ Less structured than API-driven agents

---

### Decision: No OAuth in MVP

* ✅ Simpler
* ❌ Some UX friction

---

### Decision: Plugin system early

* ✅ Avoid rewrites later
* ❌ More upfront complexity

---

## **18. Open Questions**

* Do we persist session history in MVP?
* Do we allow session replay immediately or defer?
* Should Docker containers be ephemeral or reusable?

---

## **19. Summary**

ClankYankers is architected as:

* A **terminal-native orchestration layer**
* With **pluggable execution environments**
* And **connector-driven agent integrations**

The system deliberately avoids:

* Over-opinionated orchestration
* Lock-in to any agent ecosystem

Instead, it builds the foundation for:

* Unified agent interaction
* Experimentation
* Distributed execution


