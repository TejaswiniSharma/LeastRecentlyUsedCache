# LRU Cache — C# / ASP.NET Core

A **Least Recently Used (LRU) Cache** exposed as a localhost REST API, with a
dedicated HTTP client, a test runner, and a full NUnit test suite.

---

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
  - [LRU Cache Internals](#lru-cache-internals)
  - [Concurrency](#concurrency)
  - [Authentication — API Key](#authentication--api-key)
  - [Rate Limiting — Token Bucket](#rate-limiting--token-bucket)
  - [Observability — Health Check & Metrics](#observability--health-check--metrics)
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
| Concurrency | `SemaphoreSlim(1,1)` — async-safe exclusive lock |
| Transport | HTTP/1.1 on localhost |
| Authentication | API Key (`X-Api-Key` header) |
| Rate limiting | Token bucket per API key — 10 req/s, queue of 5 |
| Health check | `GET /health` — liveness + cache metrics (no auth) |
| Test framework | NUnit 3 |

---

## Project Structure

```
Round2.sln
├── Round2/                          # ASP.NET Core Web API server
│   ├── LRUCache.cs                  # Core cache logic (doubly linked list + dict + metrics)
│   ├── ApiKeyMiddleware.cs          # Auth middleware — validates X-Api-Key header
│   ├── LRUCacheHealthCheck.cs       # Health check — fill rate, hit/miss/eviction counters
│   ├── Program.cs                   # Server startup, port/key discovery, middleware pipeline
│   └── Controllers/
│       └── LRUCacheController.cs    # REST endpoints (add, get, stats)
│
├── Round2.Client/                   # Console client
│   ├── LRUCacheClient.cs            # Typed HTTP client wrapper
│   ├── TestRunner.cs                # Automated scenario runner
│   └── Program.cs                   # Entry point — discovers server, runs tests
│
└── Round2.Tests/                    # NUnit unit tests
    ├── LRUCacheTests.cs             # 33 tests — cache logic + metrics
    ├── ApiKeyMiddlewareTests.cs     #  6 tests — auth middleware
    └── RateLimiterTests.cs          #  8 tests — token bucket behaviour
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

### Concurrency

Because `GetAsync()` mutates the linked list (MoveToFront), both reads and writes
modify shared state. A **`SemaphoreSlim(1,1)`** guards every public operation:

```csharp
await _sem.WaitAsync();
try   { /* AddItemCore / GetCore */ }
finally { _sem.Release(); }
```

Unlike `lock`, `SemaphoreSlim` is awaitable — calling threads yield back to the
thread pool while waiting instead of blocking, which suits ASP.NET Core's async
request pipeline.

---

### Authentication — API Key

Every request (except `/health`) must carry a shared secret in the `X-Api-Key` header.

```
Client                         ApiKeyMiddleware          Controller
  │── POST /api/cache/add ──▶ │                          │
  │   X-Api-Key: <key>        │── key valid? ──── Yes ──▶│  Add()
  │                           │              └── No  ──▶ 401
  │◀── 200 / 401 ─────────────────────────────────────── │
```

- The server generates a fresh **256-bit random key** (`RandomNumberGenerator`) on every startup.
- The key is written to `{TempDir}/lrucache_server.apikey`.
- The client reads the key from that file at startup — no manual copy-paste needed.
- The key is set once on `HttpClient.DefaultRequestHeaders` so every request carries it automatically.

---

### Rate Limiting — Token Bucket

Authenticated requests are rate-limited **per API key** using the built-in
`System.Threading.RateLimiting` token bucket algorithm:

| Parameter | Value | Meaning |
|---|---|---|
| `TokenLimit` | 10 | Bucket capacity — max burst |
| `TokensPerPeriod` | 10 | Tokens refilled per second |
| `QueueLimit` | 5 | Requests queued when bucket is empty |
| `QueueProcessingOrder` | OldestFirst | FIFO queue drain |

```
Bucket full (10) → burst of 10 requests passes through immediately
Bucket empty     → next requests queue (up to 5) until tokens replenish
Queue full       → 429 Too Many Requests + Retry-After: 1
```

The `/health` endpoint is **exempt** from both auth and rate limiting so load
balancers can probe it freely.

---

### Observability — Health Check & Metrics

#### `GET /health`

No authentication required. Returns JSON with server status and live cache counters:

```json
{
  "status": "Healthy",
  "checks": [{
    "name": "lrucache",
    "status": "Healthy",
    "description": "Cache is operational.",
    "data": {
      "count": 42,
      "capacity": 100,
      "fillPct": 42.0,
      "hitCount": 1200,
      "missCount": 300,
      "evictionCount": 15,
      "hitRatePct": 80.0
    }
  }]
}
```

Status is `Degraded` (not `Unhealthy`) when fill rate ≥ 90%, allowing load
balancers to keep routing traffic while alerting operators.

#### `GET /api/cache/stats`

Requires auth. Returns the same counters alongside count and capacity:

```json
{
  "currentCount": 42,
  "capacity": 100,
  "hitCount": 1200,
  "missCount": 300,
  "evictionCount": 15,
  "hitRatePct": 80.0
}
```

All values are snapshot reads — eventually consistent under high concurrency.

---

### Dynamic Port Discovery

Port `5000` may already be in use. The server binds to **port 0**, which lets
the OS assign the next available port, then writes the actual port to
`{TempDir}/lrucache_server.port`. The client reads this file before making any
requests.

```
Server startup
  └─ Binds to 0 → OS assigns e.g. 61184
  └─ Writes 61184   → /tmp/lrucache_server.port
  └─ Writes <key>   → /tmp/lrucache_server.apikey

Client startup
  └─ Reads port + key from /tmp/
  └─ BaseAddress = http://localhost:61184
  └─ DefaultRequestHeaders["X-Api-Key"] = <key>
```

---

## API Endpoints

| Method | Path | Auth | Rate limited | Description |
|---|---|---|---|---|
| `GET` | `/health` | No | No | Liveness + cache metrics |
| `POST` | `/api/cache/add` | Yes | Yes | Add / refresh a key |
| `GET` | `/api/cache/{key}` | Yes | Yes | Retrieve a key (promotes to MRU) |
| `GET` | `/api/cache/stats` | Yes | Yes | Count, capacity, hit/miss/eviction metrics |

---

### `POST /api/cache/add`

Adds a key to the cache. If the key already exists, its timestamp is refreshed
and it is moved to the MRU position. Evicts the LRU item if at capacity.

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

**Response `429 Too Many Requests`** (queue full)
```json
{ "message": "Rate limit exceeded. Request queue is full — retry after 1 second." }
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

Returns current count, capacity, and lifetime hit/miss/eviction counters.

**Response `200 OK`**
```json
{
  "currentCount": 42,
  "capacity": 100,
  "hitCount": 1200,
  "missCount": 300,
  "evictionCount": 15,
  "hitRatePct": 80.0
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
  [stats]  count=100  capacity=100  hitRatePct=75.0
✓ Test run complete.
```

---

### Run the Tests

```bash
# All tests
dotnet test Round2.Tests

# Verbose output (see each test name)
dotnet test Round2.Tests --logger "console;verbosity=normal"

# Filter by test class
dotnet test Round2.Tests --filter "ClassName=Round2.Tests.LRUCacheTests"
dotnet test Round2.Tests --filter "ClassName=Round2.Tests.ApiKeyMiddlewareTests"
dotnet test Round2.Tests --filter "ClassName=Round2.Tests.RateLimiterTests"
```

Expected output:
```
Total tests: 41
     Passed: 41
 Total time: 0.8 Seconds
```

---

## Testing

Tests are split across three files, each covering a different layer.

### `LRUCacheTests.cs` — 33 tests

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
| Metrics | 8 | Initial zeros, hit/miss counting, eviction counting, hit rate formula, zero-division guard |
| Concurrency | 2 | Count never exceeds capacity under 200 parallel adds; no exception under mixed add+get |

---

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

---

### `RateLimiterTests.cs` — 8 tests

Tests `TokenBucketRateLimiter` directly (no HTTP stack). `AutoReplenishment` is
set to `false` so tests call `TryReplenish()` manually — fully deterministic,
no real-time waits.

| Test | What is verified |
|---|---|
| `Request_WithinTokenLimit_IsGrantedImmediately` | First request within budget is granted |
| `MultipleRequests_WithinTokenLimit_AllGrantedImmediately` | All requests within bucket are granted |
| `Request_WhenTokensExhausted_IsQueuedNotRejected` | Over-budget request enters queue (pending), not rejected |
| `QueuedRequest_IsGrantedAfterTokenReplenishment` | Queued request granted after `TryReplenish()` |
| `MultipleQueuedRequests_AllGrantedAfterReplenishments` | Each replenishment serves exactly one queued request |
| `Request_WhenQueueFull_IsRejectedImmediately` | Request beyond queue limit rejected (`IsAcquired = false`) |
| `MultipleRequests_BeyondQueueLimit_AllRejected` | All overflow requests rejected |
| `QueuedRequests_AreProcessedInOldestFirstOrder` | FIFO drain — oldest queued request served first |
