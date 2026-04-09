# Remote daemon and SSH backplanes design

## Problem

ClankYankers already supports `local` and `docker` execution, but it cannot yet launch sessions over SSH or delegate execution to a dedicated remote node. We need two new backplanes that preserve terminal fidelity across the existing slice:

`UI -> session API -> backplane -> interactive runtime -> output -> UI`

The new capabilities are:

1. **SSH backplane** for direct interactive terminal sessions over SSH, including password, key, passphrase, keyboard-interactive, and OpenSSH user-certificate authentication.
2. **Remote backplane** for node-based execution through a dedicated cross-platform `.NET tool` daemon named `clank-daemon`.

The implementation must remain config-driven, testable in CI, and observable through the existing session orchestration model.

## Recommended approach

### SSH backplane

Implement SSH with **SSH.NET** instead of shelling out to the native `ssh` executable. That gives the server:

- a fully managed, cross-platform transport
- direct access to authentication methods
- a shell stream we can adapt to `IInteractiveSession`
- resize support through `ShellStream.ChangeWindowSize(...)`

The SSH backplane will:

1. Resolve SSH settings from `HostConfig`.
2. Open an authenticated `SshClient`.
3. Create a PTY-backed `ShellStream`.
4. Start the connector launch command inside that shell.
5. Stream shell output into `Channel<TerminalOutputChunk>`.

### Remote daemon backplane

Create a new `clank-daemon` tool as a small ASP.NET Core host that exposes:

- HTTP control endpoints for session lifecycle and daemon metadata
- a WebSocket session stream for terminal output and state
- an out-of-process self-update endpoint that schedules a detached updater process

Use a shared contracts project so both the server and daemon speak the same request/response schema. The first release will support daemon-side **process execution** and **docker execution** so a remote node can run either the host shell or containerized connectors.

## Data model changes

Keep the existing `BackplaneDefinition` shape, but extend `HostConfig` with optional per-backplane fields:

- Docker: endpoint, image
- SSH: address, port, username, password, private key path, private key passphrase, certificate path, host fingerprint, CA fingerprint, allow-any-host-key, keyboard-interactive
- Remote: daemon URL, access token, executor kind, docker endpoint, docker image, allow-insecure TLS

This keeps config persistence straightforward while still allowing conditional validation and UI rendering.

## Runtime design

### SSH

- `SshBackplane : IBackplane`
- `SshConnectionFactory` builds `ConnectionInfo` and auth methods
- `SshInteractiveSession : IInteractiveSession`
- `ShellStream` pump reads bytes and forwards them to the output channel
- input writes to the stream
- resize calls `ChangeWindowSize`
- stop closes the shell/client cleanly

Connector launch stays unchanged: connectors still emit `LaunchSpec`, and the SSH runtime turns that into a remote shell command using the resolved working directory and executable/arguments.

### Remote

- `RemoteBackplane : IBackplane`
- `RemoteDaemonClient` calls daemon HTTP APIs and opens the WebSocket session channel
- `RemoteDaemonInteractiveSession : IInteractiveSession`
- `clank-daemon` hosts a session registry plus local/docker executors
- daemon-side process execution uses a cross-platform PTY abstraction
- daemon-side docker execution reuses the current Docker session model

## Testing strategy

### Unit

- config validation for SSH and remote hosts
- SSH auth selection and host verification rules
- remote daemon request mapping and self-update scheduling
- session request validation updates

### Integration

- `SshBackplaneTests` against a dockerized `sshd` fixture
- `RemoteBackplaneTests` against a real daemon test host on an ephemeral port
- daemon-side process/docker executor tests

### End-to-end

- Studio acceptance scenarios for configuring and launching SSH and remote sessions
- responsive-launch coverage updated for any added launch controls

### CI

- add a dockerized SSH fixture
- keep daemon integration tests self-contained
- continue running server and web suites on `ubuntu-latest`

## Tradeoffs

- Extending the flat `HostConfig` is a bigger schema surface, but it is the lowest-risk path for a complete implementation in the current codebase.
- SSH.NET provides the auth coverage we need without a native `ssh` dependency.
- HTTP + WebSocket keeps the daemon protocol simple and close to the existing server session model.
