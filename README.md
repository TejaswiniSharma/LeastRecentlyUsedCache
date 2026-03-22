# LRU Cache — C# / ASP.NET Core

A **Least Recently Used (LRU) Cache** exposed as a localhost REST API, with a
dedicated HTTP client, a test runner, and a full NUnit test suite.

---

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
  - [LRU Cache Internals](#lru-cache-internals)
  - [Authentication — API Key](#authentication--api-key)
  - [Dynamic Port Discovery](#dynamic-port-discovery)
- [API Endpoints](#api-endpoints)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Run the Server](#run-the-server)
  - [Run the Client](#run-the-client)
  - [Run the Tests](#run-the-tests)
- [Testing](#testing)

---

## Overview

| Capability | Detail |
|---|---|
| Cache algorithm | LRU — O(1) add, O(1) get |
| Data structures | Doubly linked list + dictionary |
| Default capacity | 100 items |
| Transport | HTTP/1.1 on localhost |
| Authentication | API Key (`X-Api-Key` header) |
| Test framework | NUnit 3 |

---

## Project Structure

```
Round2.sln
├── Round2/                     # ASP.NET Core Web API server
│   ├── LRUCache.cs             # Core cache logic (doubly linked list + dict)
│   ├── ApiKeyMiddleware.cs     # Auth middleware — validates X-Api-Key header
│   ├── Program.cs              # Server startup, key + port discovery files
│   └── Controllers/
│       └── LRUCacheController.cs   # REST endpoints (add, get, stats)
│
├── Round2.Client/              # Console client
│   ├── LRUCacheClient.cs       # Typed HTTP client wrapper
│   ├── TestRunner.cs           # Automated scenario runner
│   └── Program.cs              # Entry point — discovers server, runs tests
│
└── Round2.Tests/               # NUnit unit tests
    ├── LRUCacheTests.cs        # 17 tests — cache logic
    └── ApiKeyMiddlewareTests.cs # 6 tests — auth middleware
```

---

## Architecture

### LRU Cache Internals

The cache maintains a **doubly linked list** alongside a **dictionary** to
achieve O(1) time complexity for both reads and writes.

```
 MRU side                               LRU side
  HEAD ←→ [most recent] ←→ ... ←→ [oldest] ←→ TAIL
   ↑                                              ↑
sentinel                                       sentinel
```

- **Add** a new key → insert at HEAD (MRU position); evict node before TAIL if at capacity.
- **Add** an existing key → refresh its timestamp and move it to HEAD.
- **Get** a key → look up in O(1) via dictionary, move node to HEAD, return timestamp.
- **Evict** → remove node just before TAIL (the LRU item).

The `Node` class stores:

| Field | Type | Purpose |
|---|---|---|
| `Key` | `int` | Cache key |
| `Value` | `DateTime` | Timestamp of last access/insert |
| `Prev` | `Node?` | Previous node in list |
| `Next` | `Node?` | Next node in list |

The optional `Func<DateTime> clock` constructor parameter lets unit tests inject
a deterministic fake clock so timestamp assertions never flicker.

---

### Authentication — API Key

Every request must carry a shared secret in the `X-Api-Key` header.

```
Client                         ApiKeyMiddleware          Controller
  │── POST /api/cache/add ──▶ │                          │
  │   X-Api-Key: <key>        │── key valid? ──── Yes ──▶│  Add()
  │                           │              └── No  ──▶ 401
  │◀── 200 / 401 ─────────────────────────────────────── │
```

- The server **generates a fresh 256-bit random key** (`RandomNumberGenerator`) on every startup.
- The key is written to `{TempDir}/lrucache_server.apikey`.
- The client reads the key from that file at startup — no manual copy-paste needed.
- The key is set once on `HttpClient.DefaultRequestHeaders`, so every request
  carries it automatically.

---

### Dynamic Port Discovery

Port `5000` may already be in use. The server binds to **port 0**, which lets
the OS assign the next available port, then writes the actual port to
`{TempDir}/lrucache_server.port`. The client reads this file before making any
requests.

```
Server startup
  └─ Binds to 0 → OS assigns e.g. 61184
  └─ Writes 61184 → /tmp/lrucache_server.port
  └─ Writes <key>  → /tmp/lrucache_server.apikey

Client startup
  └─ Reads port + key from /tmp/
  └─ BaseAddress = http://localhost:61184
  └─ DefaultRequestHeaders["X-Api-Key"] = <key>
```

---

## API Endpoints

All endpoints require the `X-Api-Key` header. Missing or invalid keys receive
**HTTP 401**.

### `POST /api/cache/add`

Adds a key to the cache. If the key already exists, its timestamp is refreshed
and it is moved to the MRU position. Evicts the LRU item if the cache is at
capacity.

**Request body**
```json
{ "key": 42 }
```

**Response `200 OK`**
```json
{
  "key": 42,
  "timestamp": "2024-01-01T00:00:00Z",
  "currentCount": 1
}
```

---

### `GET /api/cache/{key}`

Retrieves the timestamp for a key and promotes it to the MRU position.
Returns `404` if the key is not in the cache.

**Response `200 OK`**
```json
{
  "key": 42,
  "timestamp": "2024-01-01T00:00:00Z"
}
```

**Response `404 Not Found`**
```json
{ "message": "Key 42 not found in cache." }
```

---

### `GET /api/cache/stats`

Returns the current item count and configured capacity.

**Response `200 OK`**
```json
{
  "currentCount": 10,
  "capacity": 100
}
```

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)

Verify with:
```bash
dotnet --version   # should print 8.x.x
```

---

### Run the Server

```bash
dotnet run --project Round2
```

Expected output:
```
[Server] API key written to  /tmp/lrucache_server.apikey
[Server] Listening on        http://127.0.0.1:61184
[Server] Port written to     /tmp/lrucache_server.port
```

---

### Run the Client

Open a **second terminal** (server must already be running):

```bash
dotnet run --project Round2.Client
```

Expected output:
```
Connecting to LRU Cache server at http://localhost:61184
Using API key: 90DAF7FE...98D3  (64 chars)

  ADD  key=1    ts=19:55:23  count=1
  ADD  key=2    ts=19:55:23  count=2
  ...
  [stats]  count=100  capacity=100
✓ Test run complete.
```

---

### Run the Tests

```bash
# All tests
dotnet test Round2.Tests

# Verbose output
dotnet test Round2.Tests --logger "console;verbosity=normal"

# Only cache logic tests
dotnet test Round2.Tests --filter "ClassName=Round2.Tests.LRUCacheTests"

# Only auth middleware tests
dotnet test Round2.Tests --filter "ClassName=Round2.Tests.ApiKeyMiddlewareTests"
```

Expected output:
```
Total tests: 23
     Passed: 23
 Total time: 0.33 Seconds
```

---

## Testing

Tests are split into two files covering different concerns:

### `LRUCacheTests.cs` — 17 tests

Tests the cache in complete isolation. A **fake clock** (`Func<DateTime>`) is
injected so every timestamp increments by exactly one second per tick —
deterministic, no `Thread.Sleep` needed.

| Group | # | What is verified |
|---|---|---|
| Constructor | 3 | Valid init, zero/negative capacity throws |
| AddItem | 6 | New key, duplicate (count + timestamp), eviction, LRU order after Get, re-add protection |
| Get | 3 | Returns timestamp, null on miss, promotes to MRU |
| Eviction ordering | 2 | Strict insertion-order eviction, multiple sequential evictions |
| Capacity boundary | 3 | No eviction at capacity, exactly one eviction at capacity+1, capacity-of-1 edge case |

### `ApiKeyMiddlewareTests.cs` — 6 tests

Tests `ApiKeyMiddleware` in isolation using `DefaultHttpContext` — no server or
HTTP involved. Both the **status code** and whether the **next middleware was
called** are asserted.

| Test | Scenario | Expected |
|---|---|---|
| `Request_WithNoApiKeyHeader_Returns401` | Header absent | `401`, next blocked |
| `Request_WithWrongApiKey_Returns401` | Wrong key | `401`, next blocked |
| `Request_WithEmptyApiKey_Returns401` | Empty string | `401`, next blocked |
| `Request_WithWhiteSpaceApiKey_Returns401` | Whitespace only | `401`, next blocked |
| `Request_WithPartiallyCorrectApiKey_Returns401` | First half of key | `401`, next blocked |
| `Request_WithCorrectApiKey_CallsNextMiddleware` | Exact valid key | next called, not `401` |
