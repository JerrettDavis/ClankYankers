# **ClankYankers (Working Name)**

## **Business Requirements Document (BRD)**

---

## **1. Executive Summary**

ClankYankers is a browser-based orchestration platform for interacting with agentic CLI tools (e.g., Claude Code, OpenClaw, Codex, Gemini, Ollama) through a unified terminal interface.

The system enables users to:

* Run agent CLIs from local or remote environments
* Interact with them via a browser-native terminal
* Configure execution environments (backplanes)
* Standardize agent behavior (personality, skills)
* Monitor and orchestrate multiple sessions

The MVP focuses on **execution, connectivity, and observability**, not autonomy or orchestration intelligence.

---

## **2. Problem Statement**

### Current State

Developers using agentic tools face:

* Fragmented CLI environments across tools
* Manual orchestration across terminals, containers, and machines
* No unified way to monitor or compare agent behavior
* Difficulty running identical workloads across agents
* Poor visibility into execution and session lifecycle

### Core Problem

There is no unified system for:

* Hosting multiple agent CLIs
* Interacting with them consistently
* Managing execution environments
* Observing and controlling sessions

---

## **3. Goals and Objectives**

### Primary Goals (MVP)

1. Provide a **browser-based terminal** that mirrors native CLI behavior
2. Support **multiple execution backplanes**:

   * Local machine
   * Docker containers
3. Enable **agent CLI integration via connectors**
4. Allow **session-based interaction and monitoring**
5. Establish a **plugin-based architecture** for extensibility

### Secondary Goals (Post-MVP Direction)

* Multi-agent orchestration (worker, reviewer, watchdog)
* Experimentation framework (A/B agent testing)
* Shared agent profiles (skills, personalities)
* Remote distributed execution (VMs, clusters)

---

## **4. Target Users**

### Primary Users

* Solo developers using multiple AI CLIs
* Power users running agent workflows locally

### Secondary Users (Future)

* Teams running experiments across models
* Platform engineers building internal agent infrastructure
* AI researchers comparing agent performance

---

## **5. Key Features (MVP)**

### 5.1 Browser-Based Terminal

* Fully interactive terminal embedded in the web UI
* Behavior identical to local CLI execution
* Supports:

  * STDIN / STDOUT streaming
  * ANSI rendering
  * Keyboard input passthrough
* Multiple concurrent terminal sessions

---

### 5.2 Backplane System

Backplanes define **where execution happens**.

#### MVP Backplanes:

* **Local Backplane**

  * Executes commands on host machine
* **Docker Backplane**

  * Executes inside containers
  * Supports:

    * Image pulling
    * Dockerfile provisioning
    * Container lifecycle management

#### Future Backplanes:

* SSH / VM
* Kubernetes
* Remote agent hosts

---

### 5.3 Host Configuration

Each backplane supports multiple hosts:

* Local host (default)
* Docker hosts (local or remote)

Users can:

* Add/remove hosts
* Configure connection details
* Select host per session

---

### 5.4 Agent Connectors

Agent connectors define **how to run a specific CLI tool**.

#### MVP Connectors:

* Claude Code
* OpenClaw
* Codex
* Gemini CLI
* Ollama

#### Responsibilities:

* Define CLI command invocation
* Handle environment setup
* Provide optional scripts:

  * Setup
  * Teardown
  * Pre-run
  * Post-run

#### Non-Responsibilities:

* No enforcement of agent behavior
* No opinionated workflows

---

### 5.5 Authentication Model (MVP Constraint)

* No OAuth handling in platform
* Authentication delegated to CLI tools

Examples:

* `claude login` via browser callback
* Manual token input support

System must:

* Allow interactive login flows
* Support manual credential injection

---

### 5.6 Session Management

* Users can:

  * Start sessions
  * Stop sessions
  * Reconnect to sessions
* Sessions are tied to:

  * Backplane
  * Host
  * Agent connector

Session metadata includes:

* Start time
* Execution logs
* Environment details

---

### 5.7 Plugin Architecture

Core framework defines:

* Contracts
* Events
* Hooks
* Extension points

Plugins:

* Bundled with application (MVP)
* Provide:

  * Backplanes
  * Connectors
  * Scripts
  * Extensions

No external plugin registry in MVP.

---

### 5.8 Script Hooks

Backplanes and connectors can define:

* Setup scripts
* Teardown scripts
* Pre-run scripts
* Post-run scripts
* Watchdog processes

Example:

* Docker connector keeps container alive and streams logs

---

### 5.9 Deployment Modes

The application must run as:

1. Local web app
2. Docker container
3. Remote hosted service

---

## **6. User Flows (High-Level)**

### Flow 1: Start a Session

1. User opens web app
2. Selects:

   * Backplane (e.g., Docker)
   * Host
   * Agent connector (e.g., Claude Code)
3. Configures session overrides (optional)
4. Launches session
5. Terminal opens and streams output

---

### Flow 2: Configure Backplane

1. Navigate to settings
2. Add backplane
3. Add host under backplane
4. Save configuration

---

### Flow 3: Run CLI Login

1. Launch session
2. CLI prompts login
3. User:

   * Completes browser-based login OR
   * Inputs token manually
4. Session continues

---

## **7. Non-Functional Requirements**

### Performance

* Terminal latency must feel real-time (<100ms perceived delay)
* Support multiple concurrent sessions

### Reliability

* Sessions should recover from UI disconnects
* Backplane failures should be isolated

### Security (MVP Scope)

* No credential storage beyond session scope (initially)
* Clear separation between hosts
* No execution without explicit user action

### Extensibility

* Plugin system must support:

  * New backplanes
  * New connectors
  * Custom scripts

---

## **8. Constraints**

* No OAuth management in MVP
* No external plugin marketplace
* No orchestration intelligence (agents managing agents)
* No experiment framework in MVP

---

## **9. Risks**

### Technical Risks

* Terminal emulation inconsistencies across browsers
* Handling interactive CLI login flows
* Docker socket security concerns

### Product Risks

* Becoming “just a terminal wrapper” without differentiation
* Connector fragmentation across ecosystems

---

## **10. Success Criteria**

MVP is successful if users can:

* Launch and interact with:

  * Claude Code
  * OpenClaw
  * Ollama
* Run them via:

  * Local machine
  * Docker container
* Do so entirely from the browser
* Without losing functionality compared to native CLI usage

---

## **11. Future Vision (Post-MVP)**

* Experiment orchestration system
* Multi-agent workflows:

  * Worker
  * Watchdog
  * Reviewer
* Centralized agent definitions (skills, personalities)
* Distributed execution across infrastructure
* Observability dashboards and metrics
* Replayable workflows and session capture

