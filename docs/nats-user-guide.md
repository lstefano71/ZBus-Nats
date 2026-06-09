# ZBus.Nats User Guide

A NATS messaging client for Dyalog APL, built on the ZBus event-bus framework.
Loads as a single NativeAOT DLL via `⎕NA` — no .NET runtime installation required.

## Quick Start

```apl
⍝ Path to the published AOT DLL
dll←'D:\path\to\ZBus.Nats.dll'

⍝ Define ⎕NA bindings
'init'    ⎕NA 'I4 ',dll,'|zbus_init <0T1 >Z'
'wait'    ⎕NA 'I4 ',dll,'|zbus_wait& <0T1 I4 >Z >Z >Z'
'close'   ⎕NA 'I4 ',dll,'|zbus_close <0T1'
'connect' ⎕NA 'I4 ',dll,'|zbus_nats_connect <0T1 <0T1'
'pub'     ⎕NA 'I4 ',dll,'|zbus_nats_pub <0T1 <0T1 <Z'
'sub'     ⎕NA 'I4 ',dll,'|zbus_nats_sub <0T1 <0T1 =Z'

⍝ Initialize a root and connect
(rc rootName)←init 'N1' 0
rc←connect 'N1' 'nats://localhost:4222'
(rc obj evt data)←wait 'N1' 5000 0 0 0
⍝ evt should be 'Connected'

⍝ Subscribe to a subject
(rc subName)←sub 'N1' 'prices' 'market.>'

⍝ Publish a message
rc←pub 'N1' 'market.AAPL' 'price=150.25'

⍝ Wait for the message
(rc obj evt data)←wait 'N1' 3000 0 0 0
⍝ evt='Msg', data=(subject payload headers)
```

## Concepts

### Roots

A **root** is a named connection context. You can have multiple roots (e.g., one for
publishing, one for subscribing). Each root maintains its own NATS connection and
object tree.

```apl
(rc _)←init 'PUB' 0    ⍝ publisher root
(rc _)←init 'SUB' 0    ⍝ subscriber root
```

### Events

All asynchronous operations deliver results via **events**. Use `zbus_wait` to
receive the next event on a root:

```apl
(rc obj evt data)←wait 'N1' timeout 0 0 0
```

- `obj` — which object generated the event (e.g., `'N1.prices'`)
- `evt` — event type string (e.g., `'Msg'`, `'Connected'`, `'Error'`)
- `data` — event payload (Z-format nested array, type depends on event)

The `&` in `zbus_wait&` means it runs on a separate OS thread, keeping APL responsive.

### Object Names

Objects are named hierarchically: `root.child`. Examples:
- `N1` — the root
- `N1.prices` — a subscription named "prices"
- `N1.ORDERS` — a JetStream stream
- `N1.settings` — a KV bucket

## Core NATS

### Connect

```apl
'connect' ⎕NA 'I4 ',dll,'|zbus_nats_connect <0T1 <0T1'
rc←connect 'N1' 'nats://localhost:4222'
⍝ Wait for Connected event
(rc obj evt data)←wait 'N1' 5000 0 0 0
```

**Connection lifecycle events:**
- `Connected` — initial connection established
- `Disconnected` — connection lost
- `Reconnected` — reconnected after disconnect

### Publish

```apl
'pub' ⎕NA 'I4 ',dll,'|zbus_nats_pub <0T1 <0T1 <Z'
rc←pub 'N1' 'subject' 'payload text'
```

**With headers** — pass a nested array `(payload headers)` where headers is an
Nx2 char matrix:

```apl
hdrs←2 2⍴'Content-Type' 'application/json' 'X-Trace' 'abc123'
rc←pub 'N1' 'orders.new' ((⊂'{"qty":5}')(⊂hdrs))
```

### Subscribe

```apl
'sub' ⎕NA 'I4 ',dll,'|zbus_nats_sub <0T1 <0T1 =Z'
(rc subName)←sub 'N1' 'localName' 'subject.pattern.>'
```

The `=Z` parameter carries the subject pattern in and returns the full object name out.

**With queue group** — pass a 2-element nested vector as the subject:

```apl
(rc subName)←sub 'N1' 'worker' (('tasks.>')(⊂'workers'))
```

**Msg event data:** `(subject payload headers)`

### Request/Reply

```apl
'request' ⎕NA 'I4 ',dll,'|zbus_nats_request <0T1 <0T1 <0T1 I4 =Z'
(rc mailbox)←request 'N1' 'myReq' 'svc.echo' 5000 'hello'
⍝ Wait for Reply or Timeout
(rc obj evt data)←wait 'N1' 6000 0 0 0
⍝ evt='Reply' → data=(subject payload headers)
⍝ evt='Timeout' → no responders or timed out
```

The mailbox name can be empty `''` for auto-generated names.

**Targeted delivery** — use negative timeout when you have concurrent waits at
different hierarchy levels (e.g., inverse request/respond pattern):

```apl
(rc mailbox)←request 'N1' 'R1' 'svc.echo' ¯5000 'hello'
⍝ Reply will only be visible to waiters on 'N1.R1', won't bubble to root
(rc obj evt data)←wait 'N1.R1' 6000 0 0 0
```

For normal single-event-loop usage (one `wait` on root), positive timeout is correct.

### Close

```apl
rc←close ⊂'N1.prices'    ⍝ close a subscription
rc←close ⊂'N1'           ⍝ close the entire root
```

## JetStream

Persistent messaging with at-least-once delivery guarantees.

### Create Stream

```apl
'stream'       ⎕NA 'I4 ',dll,'|zbus_nats_stream <0T1 <0T1 =Z'
'stream_purge' ⎕NA 'I4 ',dll,'|zbus_nats_stream_purge <0T1'
'stream_delete'⎕NA 'I4 ',dll,'|zbus_nats_stream_delete <0T1'
```

**Simple — single subject:**
```apl
(rc streamName)←stream 'N1' 'ORDERS' 'orders.>'
```

**Multiple subjects:**
```apl
(rc streamName)←stream 'N1' 'ORDERS' ('orders.>' 'orders.new')
```

**With config options** — append an Nx2 options matrix:
```apl
opts←3 2⍴'max_msgs' 100000 'max_age_s' 86400 'storage' 'memory'
(rc streamName)←stream 'N1' 'ORDERS' ('orders.>' opts)
```

| Option | Type | Description |
|--------|------|-------------|
| `max_msgs` | integer | Max messages retained |
| `max_bytes` | integer | Max total bytes retained |
| `max_age_s` | number | Max message age in seconds |
| `retention` | string | `'limits'` (default), `'interest'`, `'workqueue'` |
| `storage` | string | `'file'` (default), `'memory'` |

Streams are idempotent — calling `nats_stream` with the same name and subjects
updates the config. Streams persist on the NATS server across script runs.

### Purge / Delete

```apl
⍝ Remove all messages but keep the stream
rc←stream_purge ⊂streamName

⍝ Delete the stream entirely (and all its consumers)
rc←stream_delete ⊂streamName
```

### Publish with Acknowledgement

```apl
'jspub' ⎕NA 'I4 ',dll,'|zbus_nats_jspub <0T1 <0T1 =Z'
(rc ack)←jspub 'N1' 'orders.new' payload
⍝ ack = nested (stream seqno)
```

### Consumer

```apl
'consumer' ⎕NA 'I4 ',dll,'|zbus_nats_consumer <0T1 <0T1 <0T1 =Z'
(rc consumerName)←consumer streamName 'proc' '' 'all'
```

Deliver policy (last =Z arg): `'all'`, `'last'`, `'new'`, `'last_per_subject'`.
Filter subject (3rd arg): empty string for all, or a subject pattern.

Messages arrive as `JsMsg` events:

```apl
(rc obj evt data)←wait consumerName 5000 0 0 0
⍝ evt='JsMsg', data=(subject payload headers seq)
```

### Ack / Nak

```apl
'ack' ⎕NA 'I4 ',dll,'|zbus_nats_ack <0T1 I4'
'nak' ⎕NA 'I4 ',dll,'|zbus_nats_nak <0T1 I4'
rc←ack consumerName seqno
rc←nak consumerName seqno    ⍝ negative ack → redelivery
```

### Burst Pattern (fire-and-forget then drain)

JetStream never drops messages — unlike Core NATS which has slow-consumer
detection. For high-volume ingestion, blast publishes then drain at your own pace:

```apl
⍝ Purge stale data
rc←stream_purge ⊂streamName

⍝ Blast 100K publishes (no consumer needed yet)
:For i :In ⍳100000
    (rc ack)←jspub 'N1' 'orders.new' payload
:EndFor

⍝ Create consumer after blast — stream held everything
(rc consumerName)←consumer streamName 'drain' '' 'all'

⍝ Drain at leisure
:While drained<100000
    (rc obj evt data)←wait consumerName 10000 0 0 0
    rc←ack consumerName (⊃⌽data)   ⍝ seq is last element
    drained←drained+1
:EndWhile
```

## Key/Value Store

Strongly-consistent key/value built on JetStream.

```apl
'kv'      ⎕NA 'I4 ',dll,'|zbus_nats_kv <0T1 =Z'
'kv_get'  ⎕NA 'I4 ',dll,'|zbus_nats_kv_get <0T1 <0T1 >Z >Z'
'kv_put'  ⎕NA 'I4 ',dll,'|zbus_nats_kv_put <0T1 <0T1 <Z >Z'
'kv_del'  ⎕NA 'I4 ',dll,'|zbus_nats_kv_del <0T1 <0T1'
'kv_watch' ⎕NA 'I4 ',dll,'|zbus_nats_kv_watch <0T1 <0T1 >Z'
```

### Usage

```apl
⍝ Create bucket
(rc bucketName)←kv 'N1' 'settings' 'settings'

⍝ Put (returns revision number)
(rc rev)←kv_put 'N1.settings' 'api.timeout' '30' 0

⍝ Get (returns value + revision)
(rc value rev)←kv_get 'N1.settings' 'api.timeout' 0 0

⍝ Delete
rc←kv_del 'N1.settings' 'api.timeout'

⍝ Watch for changes
(rc watchName)←kv_watch 'N1.settings' '>' 0
⍝ Delivers KeyVal events: data=(key value revision operation)
```

## Object Store

Store large binary objects (files, images, models).

```apl
'obj'       ⎕NA 'I4 ',dll,'|zbus_nats_obj <0T1 =Z'
'obj_get'   ⎕NA 'I4 ',dll,'|zbus_nats_obj_get <0T1 <0T1 >Z'
'obj_put'   ⎕NA 'I4 ',dll,'|zbus_nats_obj_put <0T1 <0T1 <Z'
'obj_del'   ⎕NA 'I4 ',dll,'|zbus_nats_obj_del <0T1 <0T1'
'obj_watch' ⎕NA 'I4 ',dll,'|zbus_nats_obj_watch <0T1 >Z'
```

### Usage

```apl
⍝ Create store
(rc storeName)←obj 'N1' 'files' 'files'

⍝ Put binary data
data←⎕UCS 256⍴⍳256
rc←obj_put 'N1.files' 'model.bin' data

⍝ Get (returns full byte array)
(rc bytes)←obj_get 'N1.files' 'model.bin' 0

⍝ Delete
rc←obj_del 'N1.files' 'model.bin'

⍝ Watch for changes
(rc watchName)←obj_watch 'N1.files' 0
⍝ Delivers ObjChanged events: data=(name size operation)
```

## Services (Micro)

Register NATS micro-services that respond to requests.

```apl
'service'   ⎕NA 'I4 ',dll,'|zbus_nats_service <0T1 <0T1 =Z'
'endpoint'  ⎕NA 'I4 ',dll,'|zbus_nats_endpoint <0T1 <0T1 <0T1'
'discover'  ⎕NA 'I4 ',dll,'|zbus_nats_svc_discover <0T1 <0T1 I4 >Z'
```

### Create & Serve

```apl
⍝ Register service
(rc svcName)←service 'N1' 'calc' ('Math service' '1.0')

⍝ Add endpoint
rc←endpoint 'N1.calc' 'add' 'math.add'

⍝ Incoming requests arrive as events
(rc obj evt data)←wait 'N1' 10000 0 0 0
⍝ evt='Request', data=(subject replyTo payload headers)

⍝ Reply by publishing to replyTo
replyTo←2⊃data
rc←pub 'N1' replyTo 'result=42'
```

### Discover Services

```apl
(rc services)←discover 'N1' 'calc' 500 0
⍝ services = nested array of (name id version) per instance
⍝ Use '' to discover all services
```

## Utility Verbs

### Describe

```apl
'describe' ⎕NA 'I4 ',dll,'|zbus_describe <0T1 >Z'
(rc info)←describe 'N1' 0           ⍝ root: ([ZBus.Nats] version state url)
(rc info)←describe 'N1.prices' 0    ⍝ child: (name type state subject)
```

### GetProperty

```apl
'getprop' ⎕NA 'I4 ',dll,'|zbus_getprop <0T1 <0T1 >Z'
(rc val)←getprop 'N1' 'State' 0       ⍝ → 'Connected'
(rc val)←getprop 'N1' 'Url' 0         ⍝ → 'nats://localhost:4222'
(rc val)←getprop 'N1' 'LastError' 0   ⍝ → most recent swallowed error (or '')
(rc val)←getprop 'N1' 'ErrorCount' 0  ⍝ → total swallowed error count
(rc val)←getprop 'N1' 'Errors' 0      ⍝ → nested vector of last 16 errors
```

**Diagnostics:** Exceptions that cannot cross the ⎕NA boundary (would cause aplcore)
are recorded in an internal ring buffer. Query `LastError` after an unexpected `rc≠0`
to see the actual .NET exception message — invaluable for diagnosing configuration
errors (e.g., "subjects overlap with existing stream").

### Names / Exists

```apl
'names'  ⎕NA 'I4 ',dll,'|zbus_names <0T1 >Z'
'exists' ⎕NA 'I4 ',dll,'|zbus_exists <0T1'
(rc children)←names 'N1' 0          ⍝ list child objects
rc←exists ⊂'N1.prices'              ⍝ 0=exists, 3=not found
```

## Event Reference

| Event | Source | Data |
|-------|--------|------|
| `Connected` | root | (empty) |
| `Disconnected` | root | error message |
| `Reconnected` | root | (empty) |
| `Msg` | subscription | `(subject payload headers)` |
| `Reply` | request mailbox | `(subject payload headers)` |
| `Timeout` | request mailbox | (empty) |
| `JsMsg` | consumer | `(subject payload seq)` |
| `KeyVal` | kv watch | `(key value revision operation)` |
| `ObjChanged` | obj watch | `(name size operation)` |
| `Request` | service | `(subject replyTo payload headers)` |
| `Closed` | any | (empty, posted when object is closed) |
| `Error` | any | error message string |

## High-Precision Timing

For benchmarking, use `16 ⎕DT 'Z'` with `⎕FR←1287` localised in a dfn:

```apl
elapsed_s←{⎕FR←1287 ⋄ (⍵-⍺)÷1e9}

t0←16 ⎕DT 'Z'
⍝ ... timed section ...
t1←16 ⎕DT 'Z'
⎕←'Elapsed: ',(⍕t0 elapsed_s t1),'s'
```

## Building the DLL

```powershell
dotnet publish src\ZBus.Nats\ZBus.Nats.csproj -c Release
# Output: src\ZBus.Nats\bin\Release\net10.0\win-x64\publish\ZBus.Nats.dll
```

The published DLL is a self-contained NativeAOT binary (~7-8 MB). No .NET runtime needed.

## ⎕NA Tips

- DLL names with dots require the full path including `.dll` extension
- `&` (threaded call) only works with `Z` format parameters, not `PP`
- Every `>Z` output needs a placeholder `0` in the call
- Use `=Z` when possible to avoid placeholders
- Enclose simple arguments: `close ⊂'N1'` (single arg must be scalar)
