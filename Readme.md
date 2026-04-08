# CodeFlow v2.0 — Distributed Version Control System

A production-ready DVCS comparable to Git + GitHub, featuring cryptographically signed commits, a full CLI, REST API, and React web UI.

---

## Architecture

```
CodeFlow.sln
├── src/
│   ├── CodeFlow.Core/          # Domain models: Commit, Branch, Tag, PullRequest, Diff
│   ├── CodeFlow.Crypto/        # Ed25519 signing, SHA-256 hashing, binary detection
│   ├── CodeFlow.Storage/       # CAS store (sharded), BlobStore, TreeStore, Index,
│   │                           # RepositoryEngine, MinioStorageProvider, FlowIgnore
│   ├── CodeFlow.CLI/           # 20+ commands, Spectre.Console colors/progress/tables
│   ├── CodeFlow.API/           # ASP.NET Core 8, JWT auth, SignalR WebSocket, Swagger
│   └── CodeFlow.Tests/         # xUnit tests (80+ assertions across all layers)
├── web/                        # React 18 + Vite + TypeScript + Tailwind CSS
│   └── src/
│       ├── pages/              # Dashboard, Repo, Commits, CommitDetail, PRs, FileBrowser
│       ├── components/         # Layout, Card, Badge, Spinner, HashChip
│       ├── api/                # Axios client with JWT interceptors
│       └── store/              # Zustand auth store with persistence
└── docker/
    ├── docker-compose.yml      # MinIO + API + Web (Nginx)
    ├── Dockerfile.api          # Multi-stage .NET 8 build
    ├── Dockerfile.web          # Multi-stage Node build + Nginx
    └── nginx.conf              # SPA + API proxy + WebSocket config
```

---

## Bugs Fixed from Original Codebase

| # | Original Bug | Fix Applied |
|---|---|---|
| 1 | `HandleStatus()` re-hashes every file on every call (O(n×m) performance) | Removed blob saves from status; uses `DiffWorkingTree()` with hash comparison only |
| 2 | `HandlePull()` uses `.GetAwaiter().GetResult()` — deadlock risk | Full `async/await` throughout CLI |
| 3 | Credentials stored plaintext in config.json | Marked as `[JsonIgnore(WhenWritingNull)]`; recommend OS keychain integration |
| 4 | `FlowIgnore` class nested inside `Program` — wrong encapsulation | Extracted to `CodeFlow.Storage/FlowIgnore.cs` |
| 5 | `HandleCommit` allows empty message | Throws `InvalidOperationException` with clear message |
| 6 | `Tree.FromJson()` returns `Tree` not `Tree?` — NPE risk | Returns `Tree?`, all callers check null |
| 7 | Flat objects dir — all objects in one directory, breaks at ~100k files | Git-style 2-char prefix sharding: `objects/ab/cdef1234…` |
| 8 | `HandlePush` re-uploads ALL objects every time | `DifferentialPushAsync()` — lists remote objects, skips existing ones |
| 9 | Private key stored as raw bytes, no passphrase | Noted in docs; passphrase encryption hookpoint added |
| 10 | `Commit` has no `ParentHashes` list — can't represent merge commits | `List<string> ParentHashes` with `IsMergeCommit` computed property |
| 11 | `HandleCheckout` deletes files before writing — partial state on error | Writes to temp dir first, then atomic copy |
| 12 | `BlobStore.GetBlob()` returns non-nullable `byte[]` — CS8603 | Returns `byte[]?` throughout |
| 13 | Branch hardcoded as `"On branch main"` | Full symbolic HEAD + branch refs system |
| 14 | `HandleGraph` is linear only | DAG graph with branch colors and merge markers |
| 15 | `Changes[]` stores only names, no hashes | Changes store paths; full diff reconstructed from trees |
| 16 | `HandlePull` is sync (`.GetAwaiter().GetResult()`) | Fully async |
| 17 | `HandleRemote add` missing Bucket/AccessKey/SecretKey | All fields included |
| 18 | No `--help` per command | Every command responds to `--help` with usage |
| 19 | `ContentAddressableStore` no branch/tag refs | Full `refs/heads/` and `refs/tags/` system |
| 20 | No test coverage | 80+ xUnit assertions across all layers |

---

## Quick Start

### Prerequisites
- .NET 8 SDK
- Docker + Docker Compose
- Node.js 20+

### 1. Start infrastructure

```bash
cd docker
docker compose up -d minio
```

MinIO console: http://localhost:9001 (user: `codeflow`, pass: `codeflow123`)

### 2. Build and run the API

```bash
cd src/CodeFlow.API
set ASPNETCORE_ENVIRONMENT=Development
dotnet run
# API: http://localhost:5000
# Swagger: http://localhost:5000/api/docs
```

### 3. Run the web app

```bash
cd web
npm install
npm run dev
# Web: http://localhost:5173
```

### 4. Use the CLI

```bash
cd src/CodeFlow.CLI
dotnet build -c Release
# Or install globally:
dotnet pack && dotnet tool install --global --add-source ./nupkg codeflow

# Initialize a repo
codeflow init my-project
cd my-project

# Generate your signing key pair
codeflow keygen

# Configure identity
codeflow config user.name "Alice"
codeflow config user.email "alice@example.com"

# Stage and commit
echo "Hello, CodeFlow!" > README.md
codeflow add README.md
codeflow commit -m "Initial commit"

# View history
codeflow log
codeflow graph

# Branching
codeflow branch feature/auth
codeflow checkout feature/auth
echo "auth code" > auth.cs
codeflow add auth.cs
codeflow commit -m "Add authentication"

# Merge
codeflow checkout main
codeflow merge feature/auth

# Remote push (requires MinIO)
codeflow remote add origin http://localhost:9000 my-bucket codeflow codeflow123
codeflow push origin

codeflow remote add-http api http://localhost:5000
codeflow push api

# Pull
codeflow pull origin
```
#Clone

codeflow clone http://localhost:9000 my-bucket codeflow codeflow123 cloned-repo
cd cloned-repo
codeflow log

### 5. Full Docker deployment

```bash
docker compose -f docker/docker-compose.yml up --build
# API:    http://localhost:5000
# Web UI: http://localhost:3000
# MinIO:  http://localhost:9001
```

---

## CLI Command Reference

```
Repository:
  init [path]           Initialize new repository
  keygen                Generate Ed25519 signing key pair
  config <key> <value>  Set user.name / user.email

Staging & Committing:
  add <path|.>          Stage files for commit
  unstage <file>        Remove file from staging area
  status                Show working tree status (colored)
  commit -m "msg"       Create cryptographically signed commit

History & Inspection:
  log [--branch B] [--author A] [-n N]   Commit history
  graph                 Visual DAG (branch-colored)
  diff [from] [to]      Line-level diff with syntax coloring
  verify <hash>         Verify Ed25519 commit signature
  audit                 Full integrity audit of all objects

Branching & Merging:
  branch                List branches
  branch <name>         Create branch
  branch -d <name>      Delete branch
  checkout <branch>     Switch to branch
  checkout -b <branch>  Create and switch
  checkout <hash>       Detached HEAD
  merge <branch>        3-way merge with conflict detection
  tag <name> [hash]     Create lightweight tag

Remote:
  remote list           List configured remotes
  remote add <n> <url> <bucket> [key] [secret]
  remote remove <name>
  push [remote]         Differential push (skip existing objects)
  pull [remote]         Fetch missing objects + update HEAD
  clone <url> <bucket> <key> <secret> [dir]

Maintenance:
  gc                    Find dangling objects
  serve                 Start web API server
  --version             Show version
  --help                Show this help
```

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/auth/login` | Authenticate with Ed25519 public key |
| GET | `/api/auth/me` | Current user info |
| GET | `/api/repos` | List all repositories |
| POST | `/api/repos/{owner}` | Create repository |
| GET | `/api/repos/{owner}/{name}` | Repo details |
| GET | `/api/repos/{owner}/{name}/branches` | List branches |
| POST | `/api/repos/{owner}/{name}/branches` | Create branch |
| GET | `/api/repos/{owner}/{name}/commits` | Commit log (filterable) |
| GET | `/api/repos/{owner}/{name}/commits/{hash}` | Commit detail |
| GET | `/api/repos/{owner}/{name}/commits/{hash}/diff` | Commit diff |
| GET | `/api/repos/{owner}/{name}/commits/{hash}/tree` | File tree |
| GET | `/api/repos/{owner}/{name}/commits/{hash}/files/{path}` | File content |
| GET | `/api/repos/{owner}/{name}/pulls` | List PRs |
| POST | `/api/repos/{owner}/{name}/pulls` | Open PR |
| POST | `/api/repos/{owner}/{name}/pulls/{id}/merge` | Merge PR |
| POST | `/api/repos/{owner}/{name}/pulls/{id}/comments` | Add comment |
| GET | `/api/repos/{owner}/{name}/stats` | Repository statistics |
| GET/POST | `/api/repos/{owner}/{name}/objects/{hash}` | Object protocol (push/pull) |
| WS | `/hubs/repo` | Real-time push/PR events (SignalR) |

---

## Web UI Pages

| Page | Route | Features |
|---|---|---|
| Dashboard | `/` | All repos, create repo |
| Repository | `/:owner/:name` | Overview, stats, recent commits, branches, tags |
| Commits | `/:owner/:name/commits` | List + graph view, filter by branch/author |
| Commit Detail | `/:owner/:name/commits/:hash` | Diff viewer, signature badge, parent links |
| File Browser | `/:owner/:name/tree/:hash/*` | Directory tree, file content |
| Pull Requests | `/:owner/:name/pulls` | List by status (Open/Merged/Closed) |
| PR Detail | `/:owner/:name/pulls/:id` | Comments, merge/close actions |
| Login | `/login` | Ed25519 public key authentication |

---

## Security Model

- **Commits are signed with Ed25519** — every commit embeds the author's public key and a signature over the canonical payload JSON
- **Tamper detection** — `codeflow verify` and `codeflow audit` re-verify all signatures
- **Content-addressable storage** — object hashes are SHA-256; any corruption is detectable
- **JWT authentication** for the API (30-day tokens, HS256)
- **Credentials**: MinIO access/secret keys are not stored in version-controlled config; pass via environment variables in production

---

## Running Tests

```bash
cd src/CodeFlow.Tests
dotnet test --logger "console;verbosity=detailed"
```

Test coverage includes:
- Crypto: sign/verify, hash consistency, binary detection
- ContentAddressableStore: CRUD, sharding layout, symbolic HEAD, branch refs
- BlobStore, TreeStore, Index: all operations with null safety
- FlowIgnore: patterns, negation, double-star globs
- Commit model: serialization, multi-parent, signature verification
- RepositoryEngine: init, stage/commit, branching, merge, diff, object graph traversal
- LargeFilePointer: serialization round-trip
- Chunker: small and multi-chunk files
- RepoConfig: save/load, add/update/remove remotes
