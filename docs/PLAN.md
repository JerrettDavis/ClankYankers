# **ClankYankers**

## **PLAN.md (Implementation Plan)**

---

## **1. Execution Strategy**

### Guiding Principles

1. **Vertical slices over layers**

   * Every milestone produces a working system
   * Avoid building abstractions without execution

2. **Terminal-first validation**

   * If the terminal doesn’t feel native, nothing else matters

3. **Fake it before generalizing**

   * Hardcode before abstracting
   * Then extract contracts

4. **Backplanes before connectors**

   * Execution environment must be solid first

5. **Plugins evolve, not block**

   * Start inline, extract later

---

## **2. Milestone Overview**

| Milestone | Goal                                     |
| --------- | ---------------------------------------- |
| M0        | Project scaffold + hello terminal        |
| M1        | Local terminal execution (PTY working)   |
| M2        | Session management + WebSocket stability |
| M3        | Connector abstraction (Claude working)   |
| M4        | Docker backplane                         |
| M5        | Multi-connector support                  |
| M6        | Config system + UI                       |
| M7        | Plugin system extraction                 |
| M8        | Hardening + packaging                    |

---

## **3. Repository Structure**

### Option A (Recommended Hybrid)

```
/clankyankers
  /apps
    /web            (React + xterm)
    /server         (.NET backend)
  /libs
    /core           (contracts, events, abstractions)
    /backplanes
      /local
      /docker
    /connectors
      /claude
      /openclaw
      /ollama
      /codex
      /gemini
  /plugins          (bundled plugins)
  /infra
    /docker
```

---

## **4. Milestone Breakdown**

---

## **M0 – Project Scaffold**

### Goal

Get a running app with WebSocket connection and placeholder terminal.

### Deliverables

* Web app bootstrapped
* Backend server running
* WebSocket connection established
* Basic UI shell

### Tasks

* [ ] Create repo structure
* [ ] Setup frontend (React + xterm placeholder)
* [ ] Setup backend (.NET minimal API)
* [ ] Add WebSocket endpoint
* [ ] Connect client to server

### Exit Criteria

* Browser connects to backend
* Messages can be sent/received over WebSocket

---

## **M1 – Local Terminal Execution (Critical Path)**

### Goal

Run a real shell from the browser.

### Deliverables

* PTY integration
* Real command execution
* Streaming IO

### Tasks

* [ ] Implement PTY abstraction

  * Windows: ConPTY
  * Unix: forkpty
* [ ] Spawn shell (`cmd`, `powershell`, `bash`)
* [ ] Pipe STDIN/STDOUT over WebSocket
* [ ] Integrate xterm.js
* [ ] Handle terminal resize

### Exit Criteria

* User can run:

  ```
  ls
  pwd
  echo hello
  ```
* Feels indistinguishable from local terminal

---

## **M2 – Session Management**

### Goal

Introduce session lifecycle and isolation.

### Deliverables

* Session manager
* Multiple sessions
* Reconnect support

### Tasks

* [ ] Create Session model
* [ ] Implement SessionOrchestrator
* [ ] Map WebSocket → Session
* [ ] Support multiple concurrent sessions
* [ ] Add session state tracking

### Exit Criteria

* Multiple terminals open simultaneously
* Sessions persist across UI refresh

---

## **M3 – Connector Abstraction (Claude First)**

### Goal

Run a real agent CLI via connector.

### Deliverables

* Connector interface
* Claude connector implementation

### Tasks

* [ ] Define `IAgentConnector`
* [ ] Refactor local execution to use connector
* [ ] Implement Claude connector:

  * Command: `claude`
  * Pass-through args
* [ ] Validate login flows:

  * Browser-based login
  * Token fallback

### Exit Criteria

* User can run `claude` from browser terminal
* Login works

---

## **M4 – Docker Backplane**

### Goal

Run sessions inside Docker containers.

### Deliverables

* Docker backplane
* Container lifecycle management

### Tasks

* [ ] Define `IBackplane`
* [ ] Implement LocalBackplane (refactor existing)
* [ ] Implement DockerBackplane:

  * Create container
  * Attach TTY
  * Execute commands
* [ ] Add Docker config UI inputs
* [ ] Implement watchdog script support

### Exit Criteria

* User can select Docker
* Session runs inside container
* Terminal behaves correctly

---

## **M5 – Multi-Connector Support**

### Goal

Support multiple agent CLIs.

### Deliverables

* Additional connectors:

  * OpenClaw
  * Ollama
  * Codex
  * Gemini
  * Copilot

### Tasks

* [ ] Implement connectors incrementally
* [ ] Normalize environment handling
* [ ] Add connector selection UI

### Exit Criteria

* User can switch connectors per session
* All connectors execute successfully

---

## **M6 – Configuration System**

### Goal

Persist and manage system configuration.

### Deliverables

* Config model
* UI for managing:

  * Backplanes
  * Hosts
  * Connectors

### Tasks

* [ ] Define config schema
* [ ] Implement persistence (JSON or SQLite)
* [ ] Build config UI
* [ ] Add validation

### Exit Criteria

* User can configure:

  * Local + Docker hosts
  * Connectors
* Config persists across restarts

---

## **M7 – Plugin System Extraction**

### Goal

Make system extensible without modifying core.

### Deliverables

* Plugin contracts
* Plugin loader
* Built-in plugins migrated

### Tasks

* [ ] Define plugin interface
* [ ] Extract connectors into plugins
* [ ] Extract backplanes into plugins
* [ ] Implement plugin registration system

### Exit Criteria

* Core app loads plugins dynamically
* No hardcoded connectors/backplanes

---

## **M8 – Hardening + Packaging**

### Goal

Make the system usable and distributable.

### Deliverables

* Docker image
* Stability improvements
* Basic observability

### Tasks

* [ ] Add logging
* [ ] Add session error handling
* [ ] Improve reconnection logic
* [ ] Build Docker image
* [ ] Add environment config support

### Exit Criteria

* App runs via:

  * `docker run`
  * Local dev
* Stable multi-session usage

---

## **5. Parallel Work Streams**

---

### Stream A – Terminal Fidelity (Highest Priority)

* PTY correctness
* Resize handling
* Input latency
* ANSI rendering

---

### Stream B – Backplane Stability

* Process lifecycle
* Docker reliability
* Resource cleanup

---

### Stream C – Connector Compatibility

* CLI quirks
* Login flows
* Environment variables

---

### Stream D – UX Layer

* Session tabs
* Config UI
* Status indicators

---

## **6. Technical Spikes (Do Early)**

These are high-risk unknowns. Resolve ASAP.

### Spike 1: PTY + WebSocket throughput

* Validate no input lag
* Validate large output streams

### Spike 2: Claude login flow

* Browser callback handling
* Manual token fallback

### Spike 3: Docker TTY attach

* Interactive shell correctness
* Signal handling (CTRL+C)

---

## **7. Definition of Done (MVP)**

MVP is complete when:

* [ ] User opens browser terminal
* [ ] Runs:

  * Claude Code
  * OpenClaw
  * Ollama
* [ ] Selects:

  * Local OR Docker execution
* [ ] Interacts with CLI identically to native
* [ ] Runs multiple sessions concurrently
* [ ] Configuration persists

---

## **8. Post-MVP Roadmap (Preview)**

---

### Phase 2 – Experiment System

* Multi-agent orchestration
* Worker / Watchdog / Reviewer roles
* Experiment definitions

---

### Phase 3 – Observability

* Token tracking
* Execution metrics
* Replay system

---

### Phase 4 – Distributed Execution

* SSH backplane
* Cluster support
* Remote orchestration

---

## **9. Suggested First Week Plan**

### Day 1–2

* M0 scaffold
* WebSocket working

### Day 3–4

* PTY integration spike (M1 core)

### Day 5–6

* Full terminal working end-to-end

### Day 7

* Session abstraction (start M2)

---

## **10. Anti-Patterns to Avoid**

* ❌ Building plugin system too early
* ❌ Abstracting connectors before one works fully
* ❌ Ignoring terminal edge cases
* ❌ Treating this like REST instead of streaming system

---

## **11. Final Notes**

This system succeeds or fails on one thing:

> **Does the terminal feel real?**

If yes:

* Everything else becomes layering

If no:

* Nothing else matters

