# ClankYankers – PROMPT.md

## Purpose

This prompt is the **single source of truth** for bootstrapping, validating, and proving the ClankYankers MVP works end-to-end.

It is designed for an autonomous agent (or human) to:

* Build the system from scratch
* Validate all components locally
* Ensure architecture integrity
* Verify terminal fidelity
* Avoid paid API usage

---

## Required Reference Documents

The agent MUST load and respect the following documents before execution. They define requirements, architecture, process, and standards.

* **BRD.md** → Business requirements, scope, and success criteria
* **DESIGN.md** → Architecture, contracts, and system design
* **PLAN.md** → Milestones, sequencing, and execution strategy
* **AGENTS.md** → Development philosophy, roles, TDD/BDD/SDD, and coding standards

### Usage Rules

* If PROMPT.md conflicts with another document:

  * DESIGN.md overrides implementation details
  * BRD.md overrides scope
  * AGENTS.md overrides development behavior
* PLAN.md defines execution order unless explicitly overridden here

---

This prompt is the **single source of truth** for bootstrapping, validating, and proving the ClankYankers MVP works end-to-end.

It is designed for an autonomous agent (or human) to:

* Build the system from scratch
* Validate all components locally
* Ensure architecture integrity
* Verify terminal fidelity
* Avoid paid API usage

---

## Core Objective

Build a browser-based agentic CLI orchestration tool that:

* Embeds a fully interactive terminal in the browser
* Connects to local and Docker execution environments
* Runs agentic CLIs via connectors
* Streams input/output in real-time
* Supports multiple concurrent sessions

---

## Hard Constraints

### MUST

* Terminal must behave identically to native CLI
* Must support PTY (not pseudo streaming)
* Must support real-time bidirectional streaming
* Must support multiple sessions
* Must run entirely locally

### MUST NOT

* Use any paid APIs
* Require Claude, Gemini, Codex, or Copilot
* Require external cloud dependencies

### ALLOWED

* Ollama (local)
* Model: `qwen3.5:9b`
* Local shell commands
* Docker (optional but required for validation phase)

---

## System Architecture (Condensed)

### Frontend

* React
* xterm.js
* WebSocket client

### Backend

* .NET 10
* ASP.NET Core
* PTY handling
* WebSocket server

### Core Components

* Session Orchestrator
* Backplanes (Local, Docker)
* Connectors (Ollama first)
* Event Bus

---

## Implementation Order (Strict)

### Phase 1 – Terminal Foundation

Goal: Prove browser terminal == real shell

#### Tasks

1. Spin up backend server
2. Implement WebSocket endpoint `/ws/session/{id}`
3. Create PTY process:

   * Windows: ConPTY
   * Unix: forkpty
4. Pipe:

   * STDIN ← WebSocket
   * STDOUT → WebSocket
5. Integrate xterm.js
6. Handle resize events

#### Validation

Run in browser:

```
pwd
ls
echo hello
```

Expected:

* No lag
* Correct formatting
* Full interactivity

---

### Phase 2 – Session Management

Goal: Multiple independent terminals

#### Tasks

* Create Session model
* Map WebSocket → session
* Track lifecycle
* Support multiple sessions

#### Validation

* Open 2+ terminals
* Run different commands
* Ensure isolation

---

### Phase 3 – Local Backplane

Goal: Abstract execution layer

#### Tasks

* Implement `IBackplane`
* Create LocalBackplane
* Move PTY logic behind backplane

#### Validation

* All previous terminal tests still pass

---

### Phase 4 – Connector (Ollama Only)

Goal: First real agent CLI integration

#### Tasks

* Implement `IAgentConnector`
* Create Ollama connector
* Command:

```
ollama run qwen3.5:9b
```

* Ensure interactive prompt works

#### Validation

* Launch session
* Run ollama
* Send prompt
* Receive response

---

### Phase 5 – Docker Backplane

Goal: Execute inside container

#### Tasks

* Implement DockerBackplane
* Create container with TTY
* Attach STDIN/STDOUT
* Execute shell

#### Validation

* Run same commands inside Docker
* Run Ollama if container supports it OR fallback shell validation

---

### Phase 6 – Config System

Goal: Persist user configuration

#### Tasks

* JSON config
* Store:

  * Backplanes
  * Hosts
  * Connectors

#### Validation

* Restart app
* Config persists

---

## Testing Strategy

---

### Unit Tests

* Session lifecycle
* Connector command generation
* Backplane execution contracts

---

### Integration Tests

* Start session → execute command → receive output
* Backplane switching
* Connector execution

---

### End-to-End Tests

#### Test 1 – Terminal Fidelity

```
echo test
```

Expected: exact output

---

#### Test 2 – Interactive Input

```
read var
```

Enter input → verify response

---

#### Test 3 – Ollama Chat

```
ollama run qwen3.5:9b
```

Prompt:

```
Hello, respond with one sentence.
```

Expected: valid response

---

#### Test 4 – Multi-session

* Open 3 sessions
* Run different commands
* No cross-talk

---

#### Test 5 – Resize Handling

* Resize browser
* Verify layout adjusts correctly

---

## Local Validation Checklist

* [ ] WebSocket connects
* [ ] PTY works
* [ ] Terminal renders correctly
* [ ] Commands execute
* [ ] Multiple sessions work
* [ ] Ollama responds
* [ ] Docker execution works
* [ ] No crashes under rapid input

---

## Performance Validation

* Input latency < 100ms
* Large output does not freeze UI
* No memory leaks across sessions

---

## Failure Handling

System must:

* Gracefully terminate sessions
* Recover from WebSocket disconnect
* Clean up processes
* Kill orphaned PTY processes

---

## Observability (Minimal)

Log:

* Session start/stop
* Command execution
* Errors

---

## Anti-Goals (Do NOT Build Yet)

* Experiment orchestration
* Multi-agent coordination
* OAuth handling
* Plugin marketplace

---

## Definition of Success

The system is complete when:

1. A user opens the browser
2. Launches a session
3. Runs:

```
ollama run qwen3.5:9b
```

4. Interacts with the model in real time
5. Can open multiple sessions
6. Can run inside Docker
7. Experiences zero difference from native CLI

---

## Final Instruction to Agent

Build the system incrementally.

After each phase:

* Run all validation steps
* Fix issues immediately
* Do not proceed with broken fundamentals

If terminal fidelity is not perfect:

* STOP
* FIX
* RE-VALIDATE

---

## Guiding Principle

> If it doesn’t feel like a real terminal, it is wrong.

---

End of PROMPT.md
