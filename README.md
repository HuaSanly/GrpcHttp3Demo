# Robot-VR Streaming Server

This project implements a low-latency video streaming server for Robot-to-VR teleoperation.

## Architecture

- **Control Plane**: gRPC over HTTP/2 (TCP) or HTTP/3 (QUIC).
  - Handles device registration, session management, and control commands.
  - Defined in `Protos/signaling.proto`.
- **Data Plane**: Pure UDP.
  - Handles high-bandwidth video data.
  - Implements a custom SFU (Selective Forwarding Unit) in `UdpMediaServer.cs`.
- **Flow Control**: SCReAM (Self-Clocked Rate Adaptation for Multimedia).
  - Implemented in `Services/Scream/`.
  - Regulates video bitrate based on network congestion.

## Prerequisites

- .NET 10.0 SDK
- Visual Studio 2022 or VS Code

## Running the Server

```bash
dotnet run
```

The server listens on:
- **TCP 7776**: gRPC (HTTP/2, cleartext)
- **TCP 7777**: gRPC (HTTPS, HTTP/1.1 + HTTP/2)

### Deploying to two servers with two certificates

This repo includes two example environment overrides:
- `appsettings.ServerA.json` -> `Configs/Certificate/grpc-dev.eifuture.com.pfx`
- `appsettings.ServerB.json` -> `Configs/Certificate/grpc-pro.eifuture.com.pfx`

On each server, set `ASPNETCORE_ENVIRONMENT` to pick which certificate is used (this controls `appsettings.{Environment}.json` loading):

```bash
# Server A
set ASPNETCORE_ENVIRONMENT=ServerA
dotnet run --no-launch-profile

# Server B
set ASPNETCORE_ENVIRONMENT=ServerB
dotnet run --no-launch-profile
```

Alternatively, you can use the included launch profiles:

```bash
dotnet run --launch-profile ServerA
dotnet run --launch-profile ServerB
```

Both servers can listen on `7777` as long as they are on different machines (or different network namespaces/containers). On the same Windows machine, you cannot bind two processes to the same `7777`.

### Broadcast mode

`DevSettings:BroadcastToAll=true` enables a debug broadcast mode that forwards to all UDP-registered sessions.

- In `Development` environment: `BroadcastToAll` can be enabled directly.
- In non-`Development` environments: you must also set `DevSettings:AllowBroadcastInNonDevelopment=true`.

## Protocol

### 1. Signaling (gRPC)
- **Robot** calls `Register(ClientInfo)` with `type = ROBOT`.
- **VR** calls `Register(ClientInfo)` with `type = VR`.
- **WPF / Website companion client** calls `Register(ClientInfo)` with `type = CLIENT`.
- **VR** calls `StartStream(StreamRequest)` to request video from a Robot.
- **VR** calls `RobotCmd(Command)` to send control commands to the Robot.

#### gRPC Session Header (Required)

Full gRPC signaling details: `Readme/PROTOCOL_GRPC_SIGNALING.md`.

After `Register`, the server returns a unique `SessionId`.

For **all subsequent gRPC calls** (including `Ping`, `Pair`, `Subscribe`, `ListUnpaired`, and `EventStream`), the client must attach this value as a gRPC metadata/header:

- Header key: `session-id`
- Header value: the `SessionId` returned by `Register`

If the header is missing, the server cannot associate the call with a session, heartbeats will not refresh `LastHeartbeatUtc`, and the session may be cleaned up as timed out.

**grpc-dotnet C# example:**

```csharp
using Grpc.Core;

// 1) Register
var reg = await client.RegisterAsync(new RegisterRequest { /* ... */ });
var sessionId = reg.SessionId;

// 2) Prepare metadata once
var headers = new Metadata { { "session-id", sessionId } };

// 3) Ping (gRPC heartbeat)
await client.PingAsync(new Heartbeat { ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, headers);

// 4) Other unary calls
await client.PairAsync(new PairRequest { /* ... */ }, headers);
await client.SubscribeAsync(new SubscribeRequest { /* ... */ }, headers);

// 5) Server-streaming
using var call = client.EventStream(new EventSubscribe { SessionId = sessionId }, headers);
await foreach (var evt in call.ResponseStream.ReadAllAsync()) { /* ... */ }
```

### 2. Media (UDP)
- **Handshake**: Send `HELLO|<session_id>` to UDP port 5002.
- **Video Data**: Send RTP packets.
- **Feedback**: Server sends RTCP-like feedback for SCReAM.

### 3. Client and Monitoring

Client setup and monitoring subscription guide: `Readme/WPF_GRPC_CLIENT_GUIDE.md`.

- UDP polling: `/api/monitor/udp/stats`
- UDP push: `POST /api/client/monitor/subscriptions` + UDP datagrams with prefix `0x07`
- System: `/api/monitor/system/stats`
- Config: `/api/system/config`

## Project Structure

- `Protos/`: gRPC definitions.
- `Services/`:
  - `SignalingService.cs`: gRPC implementation.
  - `ConnectionManager.cs`: State management.
  - `Media/UdpMediaServer.cs`: UDP listener and packet forwarder.
  - `Scream/`: Congestion control logic.
