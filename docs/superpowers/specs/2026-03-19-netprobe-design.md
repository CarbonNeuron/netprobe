# NetProbe Design Specification

A C# network diagnostic tool for identifying packet loss, retransmissions, latency, jitter, and reordering across network segments. Built for debugging issues in a homelab cluster behind pfSense.

## Decisions

- **Single executable** with `server` and `client` subcommands (not two separate apps)
- **Three projects**: `NetProbe` (CLI), `NetProbe.Shared` (library), `NetProbe.Tests` (xUnit)
- **TCP mode uses connection-per-probe** — measures connection setup time (SYN/SYN-ACK/ACK), good for detecting firewall/NAT state table issues
- **MTU probing uses binary search** — converges on max MTU in ~10 steps instead of linear scan
- **Humanizer** for formatting times, bytes/sec, and other human-readable output
- **Docker**: flexible entrypoint (`ENTRYPOINT ["netprobe"]`) + compose with preconfigured server/client services

## Solution Structure

```
NetProbe.sln
├── src/
│   ├── NetProbe/                    # Single console app (server + client commands)
│   │   ├── NetProbe.csproj          # net10.0, refs Shared + Spectre.Console.Cli + Humanizer.Core
│   │   ├── Program.cs               # Spectre.Console.Cli CommandApp setup
│   │   ├── Commands/
│   │   │   ├── ServerCommand.cs     # `server` subcommand + settings
│   │   │   └── ClientCommand.cs     # `client` subcommand + settings
│   │   └── UI/
│   │       ├── LiveDashboard.cs     # AnsiConsole.Live() real-time stats display
│   │       └── ReportRenderer.cs    # Final summary tables + bar charts
│   └── NetProbe.Shared/
│       ├── NetProbe.Shared.csproj   # net10.0 class library, no Spectre dependency
│       ├── Protocol/
│       │   ├── Packet.cs            # Binary packet struct + serialize/deserialize
│       │   ├── PacketType.cs        # Enum: Probe, Echo (Report/Control reserved)
│       │   └── Checksum.cs          # CRC32 implementation
│       ├── Net/
│       │   ├── UdpProbeServer.cs    # UDP socket listener + echo logic
│       │   ├── UdpProbeClient.cs    # UDP socket sender + response collector
│       │   ├── TcpProbeServer.cs    # TCP listener, accepts connection-per-probe
│       │   └── TcpProbeClient.cs    # TCP connect-per-probe sender
│       └── Stats/
│           ├── ProbeResult.cs       # Per-packet result record
│           ├── StatsCollector.cs    # Collects results, tracks reordering/jitter
│           └── TestReport.cs        # Summary statistics
├── tests/
│   └── NetProbe.Tests/
│       ├── NetProbe.Tests.csproj    # xUnit, refs NetProbe.Shared
│       ├── Protocol/
│       │   ├── PacketTests.cs       # Roundtrip serialize/deserialize, checksum validation
│       │   └── ChecksumTests.cs     # CRC32 known-value tests
│       └── Stats/
│           ├── StatsCollectorTests.cs  # Jitter, reordering, loss detection
│           └── TestReportTests.cs      # Percentile calculations, edge cases
├── Dockerfile
├── docker-compose.yml
├── .github/workflows/ci.yml
├── .gitignore
├── README.md
└── LICENSE
```

## Binary Packet Protocol

```
Offset  Size    Field
0       2       Magic bytes (0x4E50 = "NP")
2       1       Version (0x01)
3       1       Packet type: 0=Probe, 1=Echo, 2=Reserved, 3=Reserved
4       4       Sequence number (uint32, big-endian)
8       8       Timestamp (int64, UTC ticks, big-endian)
16      4       Payload length (uint32, big-endian)
20      N       Payload (variable)
20+N    4       CRC32 checksum (over bytes 0 through 20+N-1)
```

Header is fixed 20 bytes. Total packet size = 24 + payload length.

### Serialization

- `Packet` struct with serialize/deserialize using `BinaryPrimitives` — direct span-based for zero-alloc on the hot path.
- CRC32 computed over everything except the checksum field itself using `System.IO.Hashing.Crc32`.
- Big-endian wire format for network standard consistency.

### Probe/Echo Flow

Server receives a Probe, flips type to Echo, preserves the sequence number and original timestamp, sends it back. Client computes RTT from the difference between current time and the echoed timestamp.

### TCP Framing

TCP is a stream protocol, so packets must be length-prefixed for framing:
- **4-byte big-endian length prefix** followed by the raw packet bytes
- UDP sends raw packets with no framing prefix
- Both sides must consistently apply/strip the framing layer

### MTU Probing (UDP Only)

Binary search on payload size within a range (64–1500 bytes):
- **Probes per step**: 3 packets at each candidate size
- **Success threshold**: at least 2 of 3 must echo back
- **Per-step timeout**: 2 seconds (independent of `--timeout`)
- **DontFragment**: `Socket.DontFragment = true` must be set, otherwise the OS fragments packets and every size "succeeds"
- Converges in ~10 steps
- MTU probing is UDP-only; `--mtu-probe` with `--protocol tcp` is an error

## Networking Layer

### UDP Mode

- **Server**: Binds a `Socket` (UDP/dgram), loops on `ReceiveFromAsync`. For each valid Probe, flips type to Echo, preserves sequence + timestamp, sends back to the source address.
- **Client**: Sends numbered Probe packets at the configured interval. Concurrently listens for Echo responses on the same socket. After all packets sent + timeout elapsed, computes stats from what came back.

### TCP Mode (Connection-Per-Probe)

- **Server**: Binds a `Socket` (TCP/stream), calls `AcceptAsync` in a loop. For each connection: read one framed Probe (4-byte big-endian length prefix + packet bytes), send back Echo, close connection. Each accept handled concurrently via `Task`.
- **Client**: For each probe: open new TCP connection, send length-prefixed Probe, read Echo, close, record RTT (which includes connection setup time). Sequential by default since connection setup is the thing being measured.

### Shared Patterns

- All socket operations use `async/await` with `CancellationToken`.
- `IAsyncDisposable` on server/client classes for socket cleanup.
- Receive timeout on client to avoid hanging on lost packets.
- Server supports multiple concurrent clients (each UDP source address or TCP connection is independent).
- Server logs a startup banner and one line per client session to stdout via Spectre.Console markup.

### Error Handling

- Unreachable server (DNS failure, connection refused): print clear error message and exit with non-zero code.
- Bind address/port already in use: print error and exit.
- Invalid argument combinations (e.g., `--mtu-probe` with `--protocol tcp`): print validation error and exit.
- `--version` flag configured in `CommandApp` setup.

## Statistics & Reporting

### StatsCollector

Accumulates results during the test run:

- Tracks every `ProbeResult` (sequence number, sent timestamp, received timestamp, RTT, payload size).
- Detects reordering: maintains highest-seen sequence number; any arrival below that is out-of-order.
- Computes rolling jitter using RFC 3550 formula: `J(i) = J(i-1) + (|D(i-1,i)| - J(i-1)) / 16`.
- Tracks throughput: total bytes / elapsed time.

### TestReport

Final summary computed from collected results:

- Total sent / received / lost / loss percentage
- RTT: min, avg, max, p95, p99 (sorted array, index-based percentile)
- Jitter: mean deviation of inter-packet arrival times
- Reordered packet count and percentage
- Throughput in bytes/sec (formatted with Humanizer)
- MTU probe result: max successful payload size (if MTU mode)

### Display

- **Live dashboard**: `AnsiConsole.Live()` with a `Table` showing packets sent, received, current loss %, rolling RTT avg, throughput. Updated every ~100ms.
- **Config panel**: `Panel` at start showing test parameters.
- **Final report**: `Table` with summary stats, color-coded (green < 1% loss, yellow 1-5%, red > 5%). `BarChart` for latency distribution.
- **JSON mode**: `--json` flag skips Spectre output, emits `TestReport` as JSON via `System.Text.Json`.

## CLI

```
netprobe server [--port 5555] [--bind 0.0.0.0] [--protocol udp|tcp]
netprobe client --host <addr> [--port 5555] [--protocol udp|tcp]
                [--count 1000] [--interval 10ms] [--payload-size 64]
                [--mtu-probe] [--timeout 30s] [--json]
```

## Docker

### Dockerfile

Multi-stage build:
- Build stage: `mcr.microsoft.com/dotnet/sdk:10.0`
- Runtime stage: `mcr.microsoft.com/dotnet/runtime:10.0`
- `ENTRYPOINT ["netprobe"]` — pass `server` or `client` as args

### docker-compose.yml

Two services on a shared bridge network:
- `server`: `command: server --port 5555 --protocol udp`, with a healthcheck (`netprobe client --host localhost --port 5555 --count 1 --timeout 2`)
- `client`: `command: client --host server --port 5555 --protocol udp --count 100`, `depends_on: server` with `condition: service_healthy`
- Environment variable overrides for all CLI args.

## CI

`.github/workflows/ci.yml`:
- Triggers on push to `main` and PRs
- `actions/setup-dotnet@v4` with .NET 10
- Steps: restore → build → test → publish
- Docker image build (verify only, no push)

## Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| Spectre.Console | NetProbe | Terminal UI, tables, charts, progress |
| Spectre.Console.Cli | NetProbe | CLI argument parsing |
| Humanizer.Core | NetProbe | Human-readable formatting |
| System.IO.Hashing | NetProbe.Shared | CRC32 (part of .NET 10 runtime, no NuGet ref needed) |
| xunit | NetProbe.Tests | Test framework |
| xunit.runner.visualstudio | NetProbe.Tests | Test runner |
| Microsoft.NET.Test.Sdk | NetProbe.Tests | Test infrastructure |

## Target Framework & Language

- .NET 10 (`net10.0` TFM)
- C# 13
- Nullable reference types enabled
- Implicit usings enabled
