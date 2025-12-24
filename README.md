# ClaudeWin9xNt

[![Build](https://github.com/ryandeering/claude-win9xNt/actions/workflows/build.yml/badge.svg)](https://github.com/ryandeering/claude-win9xNt/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Claude Code CLI access on Windows 95/98/2000/XP via a network proxy.

> “Your scientists were so preoccupied with whether they could, they didn’t stop to think if they should.”
> - Dr. Ian Malcolm, *Jurassic Park* (1993)

```
┌─────────────────────┐         ┌─────────────────────┐         ┌─────────────────────┐
│   Windows 9x/NT     │  HTTP   │       Server        │  stdio  │   Claude Code CLI   │
│      Client         │◄───────►│      Proxy          │◄───────►│                     │
│       (C99)         │  :5000  │     (.NET 10)       │         │                     │
└─────────────────────┘         └─────────────────────┘         └─────────────────────┘
        │                               │
        │ TCP 5001/5002                 │
        ▼                               ▼
   File transfers              Project directory
```

![Claude Code running on Windows 95](misc/win95.jpg)


- Client polls proxy for Claude responses
- Proxy queues file/command operations for client to execute
- All connections initiated by client 

## Prerequisites

**Proxy:** [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code)

**Client:** Win95 needs Winsock 2 + MSVCRT (get Internet Explorer 5, Winsock 2 update), TCP/IP networking set up.

## Quick Start

1. **Proxy:** Run `ClaudeWin9xNt-Server.exe`
2. **Client:** Copy `ClaudeWin9xNt-Client.exe` and `client.ini` to Win9x/NT, then:
   ```
   C:\> ClaudeWin9xNt-Client.exe
   > /server 192.168.1.100:5000
   > /connect C:\MYPROJECT
   ```

Open ports 5000-5002 between machines.

## Support

This is a passion project, not a profit project! If you'd like to buy me a coffee:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/ryandeering)

## Notes

**Security:** All traffic is unencrypted as there is no HTTPS on Win9x (obviously!). Only use on trusted networks.

Command output uses temp files (pipes can be unreliable on 9x depending on how the process is hosted), so it can be a bit of a pain to use on Win9x. Haven't tested ME but should work. Perfect on WinXP.

## Build

### Client

```bash
cd client
make
```

Needs i686 MinGW-w64 with msvcrt runtime.

### Proxy

```bash
cd server/ClaudeWin9xNt
dotnet run                      
dotnet publish -r win-x64       # Or linux-x64, osx-arm64, etc
```

## Commands

| Command | Description |
|--------|-------------|
| `/connect [path]` | Start session |
| `/disconnect` | End session |
| `/server ip:port` | Set proxy address |
| `/download <remote> <local>` | Download file |
| `/upload <local> <remote>` | Upload file |
| `/poll` | Check for output |
| `/status` | Connection status |
| `/log [on\|off\|view]` | Logging |
| `/clear` | Clear screen |
| `/help` | Help |
| `/quit` | Exit |


## Configuration

### Client (`client.ini`)
```ini
[server]
ip=192.168.1.100
port=5000
skip_permissions=false
```

### Proxy (`proxy.ini`)
```ini
[server]
api_port=5000
download_port=5001
upload_port=5002
```

Put these next to their respective executables.

Environment variables (if Claude Code is in non-standard location):
- `CLAUDE_CLI_PATH` - Claude CLI path
- `CLAUDE_NODE_DIR` - Node directory

## License

MIT
