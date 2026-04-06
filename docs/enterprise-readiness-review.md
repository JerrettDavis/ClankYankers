# ClankYankers enterprise readiness review

## Verdict

ClankYankers is **not enterprise-ready yet**.

The product has a solid terminal-first MVP spine, but the runtime is still optimized for a trusted local operator. The biggest gaps are:

1. **Control-plane security**: no auth, no RBAC, no session ownership, and no protected WebSocket attach path.
2. **Execution isolation**: local execution runs under the server identity, and Docker sessions are not hardened for shared or regulated environments.
3. **Runtime model depth**: the Lab, environment abstraction, and experiment comparison model were previously scaffolded rather than operational.
4. **Extensibility posture**: provider and backplane growth was still gated by hard-coded validation and startup registration patterns.

## Review summary

| Area | Current state before this slice | Enterprise target |
| --- | --- | --- |
| Control plane | Open local API/WebSocket surface with no identity or policy boundary | Authenticated, authorized, auditable control plane |
| Execution fabrics | Local + Docker only, with remote/distributed fabrics absent | Named environments across local, container, SSH, remote Docker, VM, and cluster fabrics |
| Provider breadth | Shell, Claude, Ollama | Full provider catalog with server-enforced policy metadata |
| Experiment model | Blueprint page only | First-class experiment definitions, run groups, variants, artifacts, and comparisons |
| Extensibility | Startup registries plus hard-coded config validator kind allowlists | Registry-driven metadata that can expand without core rewrites |
| Governance | NDJSON diagnostics only | Immutable audit, approval, retention, and policy posture |

## Release gates that still block enterprise rollout

These are the items that must be treated as deployment blockers, not backlog nice-to-haves:

1. **Authentication and authorization**
   - Protect every API and WebSocket.
   - Introduce at least `admin`, `operator`, and `viewer` roles.
   - Prevent session hijack via raw session ID attach.
2. **Execution isolation**
   - Disable or strongly gate direct local execution in shared deployments.
   - Harden container execution with allowlists, non-root users, read-only filesystems, capability drops, and resource limits.
3. **Secrets and secure config**
   - Replace plaintext config-as-control-plane with a secure configuration and secret reference model.
   - Remove runtime state from tracked repo paths for hosted/shared modes.
4. **Governance and audit**
   - Record actor, source, action, target, config diff, and outcome.
   - Add attach/detach audit for terminals and session control.
5. **Plugin and MCP trust boundaries**
   - Do not enable broad plugin/MCP extensibility without capability manifests, approval, and isolation.

## Chosen foundation slice

The first enterprise-readiness slice is:

> **Registry-driven runtime metadata plus first-class experiment runs**

This was selected because it is the smallest vertical slice that unlocks the next major tracks without forcing a rewrite:

- new providers/connectors
- new backplanes/fabrics
- named experiment definitions
- run grouping and comparison
- future environment profiles

Security remains the primary release gate, but this slice changes the platform shape underneath the product so the next enterprise features have a stable place to land.

## Implemented in this slice

### 1. Registry-driven runtime validation

`ConfigValidator` no longer depends purely on hard-coded kind lists. It now accepts the live backplane and connector registries so new runtime kinds can be introduced without first rewriting validation logic.

### 2. First-class experiment definitions in config

`AppConfig` now carries `experiments[]`, making the Lab a persisted runtime surface instead of a roadmap-only placeholder.

Each experiment definition now captures:

- host matrix
- connector matrix
- optional model matrix
- terminal dimensions
- enabled state

### 3. Experiment run groups

The server now supports launching a saved experiment definition as a batch.

New behavior:

- `POST /api/experiments/{experimentId}/runs`
- expands the saved matrix into variants
- creates sessions through the existing orchestrator
- records an in-memory run group summary
- exposes run summaries back to the UI
- is currently **development-gated** until control-plane auth and authorization exist

### 4. Session-to-experiment correlation

Sessions now carry `experimentId`, which makes experiment-launched work distinguishable from ad hoc manual sessions and gives future comparison, replay, and audit features a clean correlation key.

### 5. Event bus failure isolation

Observability subscribers no longer fail the full lifecycle publish path. This reduces coupling between diagnostics and session creation.

### 6. Lab UI promoted from blueprint to working surface

The Lab now supports:

- editing saved experiment definitions
- launching an experiment batch
- viewing recent run groups
- jumping from a run back into the workspace

The initial shipped defaults are intentionally conservative:

- `local-shell-smoke` is runnable everywhere the current local shell path works
- `connector-sweep` remains a disabled draft until provider availability is confirmed

## What this slice does not claim

This slice does **not** make ClankYankers enterprise-safe by itself.

It does not add:

- auth or RBAC
- remote execution fabrics
- environment profiles
- artifact capture
- durable run history
- hardened plugin trust boundaries

## Next implementation phases

### Phase 1: Secure control plane

- OIDC-backed authentication
- RBAC and session ownership
- protected WebSocket attach
- admin-only config mutation

### Phase 2: Environment profiles and richer run ledger

- `EnvironmentProfile`
- `ResolvedLaunchSpec`
- durable `RunGroup` and `RunVariant` storage
- execution timeline and artifact capture

### Phase 3: Remote execution fabrics

- SSH-backed environment model
- remote Docker with secure transport metadata
- cluster-oriented fabric abstraction
- host health and capability discovery

### Phase 4: Governance and plugin trust

- allowlists and policy engine
- audit-grade event model
- signed plugins and capability manifests
- MCP trust and outbound policy

## Current recommendation

Treat the product as:

- **local power-user ready**
- **enterprise architecture in progress**
- **enterprise deployment blocked pending control-plane security and execution hardening**

That is the honest posture after this review and update.
