# ZBus.Nats — ⎕NA API Reference

## Z Direction Convention

- **`<Z`** — input only. Use for data the DLL consumes without returning anything (e.g., publish payload). APL call site: `rc←verb args`.
- **`>Z`** — output only. Requires a placeholder `0` in the APL call. Use only when there's no input Z to reuse.
- **`=Z`** — in/out. **Preferred for verbs that both consume input AND produce output.** The DLL reads the input first, then overwrites the same pointer slot with the output. Saves one argument and eliminates dummy placeholders.

**Rule of thumb:** if a verb has a Z input AND needs to return a Z result, use `=Z` to reuse the slot. Only fall back to separate `<Z` + `>Z` when the input must survive past the output write.

---

## Kernel Exports

Present in every ZBus AOT DLL.

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_init` | `I4 dll\|zbus_init <0T1 >Z` | `(rc rootName)←init 'N1' 0` |
| `zbus_wait` | `I4 dll\|zbus_wait& <0T1 I4 >Z >Z >Z` | `(rc obj evt data)←wait 'N1' 5000 0 0 0` |
| `zbus_close` | `I4 dll\|zbus_close <0T1` | `rc←close ⊂'N1.prices'` |
| `zbus_names` | `I4 dll\|zbus_names <0T1 >Z` | `(rc children)←names 'N1' 0` |
| `zbus_exists` | `I4 dll\|zbus_exists <0T1` | `rc←exists ⊂'N1.prices'` |
| `zbus_getprop` | `I4 dll\|zbus_getprop <0T1 <0T1 >Z` | `(rc value)←getprop 'N1' 'State' 0` |
| `zbus_setprop` | `I4 dll\|zbus_setprop <0T1 <0T1 <Z` | `rc←setprop 'N1' 'TlsMode' 'require'` |
| `zbus_describe` | `I4 dll\|zbus_describe <0T1 >Z` | `(rc desc)←describe 'N1.prices' 0` |

---

## NATS Adapter Exports

### Core

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_nats_connect` | `I4 dll\|zbus_nats_connect <0T1 <0T1` | `rc←nats_connect 'N1' 'nats://localhost:4222'` |
| `zbus_nats_pub` | `I4 dll\|zbus_nats_pub <0T1 <0T1 <Z` | `rc←nats_pub 'N1' 'subject' 'payload'` |
| `zbus_nats_sub` | `I4 dll\|zbus_nats_sub <0T1 <0T1 =Z` | `(rc name)←nats_sub 'N1' 'prices' 'market.>'` |
| `zbus_nats_request` | `I4 dll\|zbus_nats_request <0T1 <0T1 <0T1 =Z` | `(rc reqName)←nats_request 'N1' 'R1' 'svc.add' payload` |

### JetStream

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_nats_jspub` | `I4 dll\|zbus_nats_jspub <0T1 <0T1 =Z` | `(rc ack)←nats_jspub 'N1' 'orders.new' payload` |
| `zbus_nats_stream` | `I4 dll\|zbus_nats_stream <0T1 <0T1 =Z` | `(rc name)←nats_stream 'N1' 'ORDERS' 'orders.>'` |
| `zbus_nats_consumer` | `I4 dll\|zbus_nats_consumer <0T1 <0T1 <0T1 =Z` | `(rc name)←nats_consumer 'N1' 'ORDERS' 'proc' 'all'` |
| `zbus_nats_ack` | `I4 dll\|zbus_nats_ack <0T1 I4` | `rc←nats_ack 'N1.ORDERS.proc' seqNo` |

### Key/Value Store

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_nats_kv` | `I4 dll\|zbus_nats_kv <0T1 =Z` | `(rc name)←nats_kv 'N1' 'config'` |
| `zbus_nats_kv_get` | `I4 dll\|zbus_nats_kv_get <0T1 <0T1 >Z` | `(rc val)←nats_kv_get 'N1.config' 'api.timeout' 0` |
| `zbus_nats_kv_put` | `I4 dll\|zbus_nats_kv_put <0T1 <0T1 <Z` | `rc←nats_kv_put 'N1.config' 'api.timeout' payload` |
| `zbus_nats_kv_del` | `I4 dll\|zbus_nats_kv_del <0T1 <0T1` | `rc←nats_kv_del 'N1.config' 'api.timeout'` |
| `zbus_nats_kv_watch` | `I4 dll\|zbus_nats_kv_watch <0T1 <0T1` | `rc←nats_kv_watch 'N1.config' 'api.>'` |

### Object Store

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_nats_obj` | `I4 dll\|zbus_nats_obj <0T1 =Z` | `(rc name)←nats_obj 'N1' 'files'` |
| `zbus_nats_obj_get` | `I4 dll\|zbus_nats_obj_get <0T1 <0T1 >Z` | `(rc data)←nats_obj_get 'N1.files' 'report.pdf' 0` |
| `zbus_nats_obj_put` | `I4 dll\|zbus_nats_obj_put <0T1 <0T1 <Z` | `rc←nats_obj_put 'N1.files' 'report.pdf' payload` |
| `zbus_nats_obj_del` | `I4 dll\|zbus_nats_obj_del <0T1 <0T1` | `rc←nats_obj_del 'N1.files' 'report.pdf'` |
| `zbus_nats_obj_watch` | `I4 dll\|zbus_nats_obj_watch <0T1` | `rc←nats_obj_watch 'N1.files'` |

### Services

| Export | ⎕NA signature | APL call |
|--------|--------------|----------|
| `zbus_nats_service` | `I4 dll\|zbus_nats_service <0T1 <0T1 =Z` | `(rc name)←nats_service 'N1' 'calc' ('Math svc' '1.0')` |
| `zbus_nats_endpoint` | `I4 dll\|zbus_nats_endpoint <0T1 <0T1 <0T1` | `rc←nats_endpoint 'N1.calc' 'add' 'math.add'` |
| `zbus_nats_svc_discover` | `I4 dll\|zbus_nats_svc_discover <0T1 =Z` | `(rc name)←nats_svc_discover 'N1' ('D1' '' 2000)` |

---

## =Z Reuse Pattern

Verbs that consume structured input AND return a result reuse the same `=Z` slot:

| Verb | Input (read first) | Output (written after) |
|------|-------------------|----------------------|
| `nats_sub` | subject (or subject+queue) | dotted name of subscription object |
| `nats_request` | payload | request mailbox object name |
| `nats_jspub` | payload (or payload+headers) | ack info (stream, seq) |
| `nats_stream` | subjects (or subjects+options) | stream object name |
| `nats_consumer` | delivery policy / options | consumer object name |
| `nats_kv` | bucket name (future: +options) | bucket object name |
| `nats_obj` | store name | store object name |
| `nats_service` | description+version | service object name |
| `nats_svc_discover` | filter+leafname+timeout | discovery object name |

---

## Event Types

| Event | Object | Data shape |
|-------|--------|-----------|
| `Connected` | root | empty |
| `Disconnected` | root | empty |
| `Reconnected` | root | empty |
| `Error` | any | char vector (message) |
| `Timeout` | waited-on | empty |
| `Closed` | any | empty (synthetic, on close) |
| `Msg` | subscription | `(subject replyTo payload headers)` |
| `Reply` | request mailbox | `(subject replyTo payload headers)` |
| `JsMsg` | consumer | `(subject payload headers seq)` |
| `KeyVal` | KV bucket | `(key value revision operation)` |
| `ObjChanged` | object store | `(name size operation)` |
| `Request` | service | `(subject replyTo payload headers)` |
| `SvcInfo` | discovery | `(name id version metadata endpoints)` |

---

## Shape-Driven Overloading

A single `=Z` or `<Z` parameter whose meaning depends on the Z value's structure:

| Shape | Meaning |
|-------|---------|
| Simple char vector | Common case (e.g., subject, single option) |
| 2-element nested vector | Extra dimension (e.g., `(subject queueGroup)`, `(payload headers)`) |
| Nx2 nested matrix | Full options bag (e.g., consumer config, stream config) |

Headers always use the **Nx2 nested matrix** format (Conga convention):
```apl
hdrs←2 2⍴'Nats-Msg-Id' 'abc123' 'X-Custom' 'foo'
rc←nats_pub 'N1' 'subject' ('hello' hdrs)
```
