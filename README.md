# NetProbe

A network diagnostic tool for measuring packet loss, latency, jitter, reordering, and MTU across network segments. Built for debugging issues in homelab and production networks.

## Features

- **UDP mode**: Raw numbered datagrams for detecting packet loss, reordering, and jitter
- **TCP mode**: Connection-per-probe for surfacing firewall/NAT/session-table issues and slow handshakes
- **MTU probing**: Binary search on payload size to find the maximum application payload that transits the path (UDP only)
- **Real-time dashboard**: Live stats via Spectre.Console
- **JSON output**: Machine-readable results for scripting
- **Docker support**: Run server and client in containers

## Quick Start

### Direct

```bash
# Start server
netprobe server --port 5555 --protocol udp

# Run client (from another terminal or machine)
netprobe client --host 192.168.1.1 --port 5555 --count 1000 --interval 10
```

### Docker Compose

```bash
docker compose up
```

This starts a server and client on a shared network for quick testing.

## CLI Reference

### Server

```
netprobe server [OPTIONS]

Options:
  --port <PORT>          Port to listen on [default: 5555]
  --bind <ADDRESS>       Address to bind to [default: 0.0.0.0]
  --protocol <PROTOCOL>  Protocol: udp or tcp [default: udp]
```

### Client

```
netprobe client --host <HOST> [OPTIONS]

Options:
  --host <HOST>            Server address (required)
  --port <PORT>            Server port [default: 5555]
  --protocol <PROTOCOL>    Protocol: udp or tcp [default: udp]
  --count <COUNT>          Number of packets [default: 1000]
  --interval <MS>          Delay between packets in ms [default: 10]
  --payload-size <BYTES>   Payload size in bytes [default: 64]
  --mtu-probe              Enable MTU discovery (UDP only)
  --timeout <SECONDS>      Max wait for responses [default: 30]
  --json                   Output as JSON
```

## What Each Mode Measures

### UDP Mode

Sends numbered UDP datagrams and measures:
- **Packet loss**: Probes that never echo back
- **Round-trip latency**: Time from send to echo receipt (monotonic clock)
- **Jitter**: RFC 3550 interarrival jitter
- **Reordering**: Out-of-sequence echo arrivals
- **Throughput**: Bytes/sec over the test duration

### TCP Mode

Opens a new TCP connection per probe. Measures:
- **Connection setup + round-trip time**: Includes the TCP handshake (SYN/SYN-ACK/ACK)
- Useful for detecting: slow handshakes, firewall/NAT state table issues, connection timeouts

> **Note:** TCP mode does not directly detect TCP retransmissions. Retransmissions occur below the socket API and are invisible to the application. Elevated RTT variance may suggest retransmits, but cannot confirm them.

### MTU Probing

Binary search on application payload size (UDP only):
- Reports **max successful application payload** as the primary result
- Also reports **estimated max UDP payload** and **estimated path MTU** as derived values
- These estimates assume no IP options, tunneling overhead (VPN, VXLAN), or additional encapsulation

> **Platform note:** MTU probing attempts to set `Socket.DontFragment = true` to prevent OS-level fragmentation. If this is not supported on the current platform, the tool prints a warning. Without DontFragment, results may reflect fragmentation success rather than true path MTU behavior.

## Building from Source

```bash
dotnet build
dotnet test
dotnet run --project src/NetProbe -- client --host localhost --port 5555
```

## Docker

The image is published to GHCR: `ghcr.io/carbonneuron/netprobe`

### UDP Server & Client

```bash
# Start server (UDP, background, expose port)
docker run -d --name netprobe-server -p 5555:5555/udp ghcr.io/carbonneuron/netprobe server --port 5555 --protocol udp

# Run client against the server
docker run --rm ghcr.io/carbonneuron/netprobe client --host <server-ip> --port 5555 --protocol udp --count 1000 --interval 10
```

### TCP Server & Client

```bash
# Start server (TCP, background, expose port)
docker run -d --name netprobe-server -p 5555:5555/tcp ghcr.io/carbonneuron/netprobe server --port 5555 --protocol tcp

# Run client against the server
docker run --rm ghcr.io/carbonneuron/netprobe client --host <server-ip> --port 5555 --protocol tcp --count 100 --interval 50
```

### MTU Probing

```bash
# Run MTU probe against a running UDP server
docker run --rm ghcr.io/carbonneuron/netprobe client --host <server-ip> --port 5555 --mtu-probe
```

### JSON Output

```bash
# Get machine-readable results for scripting
docker run --rm ghcr.io/carbonneuron/netprobe client --host <server-ip> --port 5555 --count 100 --json
```

### Docker Compose

```bash
docker compose up
```

### Build Locally

```bash
docker build -t netprobe .
docker run --rm netprobe server --port 5555
```

## License

MIT
