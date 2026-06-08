# ZBus — Generic Event Bus for Dyalog APL via ⎕NA

## References

- `docs/z-wire-format.md` — Full Z format specification (empirically verified against Dyalog 20.0)
- `docs/adr/` — Architecture Decision Records
- `src/ZFormat/` — C# library: ZWriter + ZReader (standalone, no bus dependency)
- `src/ZBus/` — Bus kernel: object registry, event queue, waitpoints, adapter contract
- `tests/ZFormat.Tests/` — Unit tests with golden bytes from real interpreter dumps
- `samples/ZProbeManaged/` — AOT-compiled DLL loadable via ⎕NA (Z format integration test)
- `probe/z_probe.c` — C probe DLL used for initial reverse-engineering

## Skills

- /dyalog-apl-runner        Run Dyalog APL scripts and inline expressions
- /dyalog-ride              Connect to a persistent Dyalog APL session via RIDE

## Upstream References

- C:\Users\stf\Source\Repos\Conga-Sharp\docs\z-wire-format.md
- C:\Users\stf\Source\Repos\Conga-Sharp\src\CongaSharp\ZWriter.cs
- D:\devel\dwa\v20
- C:\Users\stf\Source\Repos\bridge-dwa
- D:\devel\nats\2.14.2\nats-server.exe  (local NATS server)


## Data types still needing support:

1 bit packed booleans
8 bit integers (-127..127)
16 bit integers (-32767..32767)
64 bit floats (IEEE 754 double precision)
128 bit decimal (DECF)

There are no 64 bit integers in Dyalog APL. They are exchanged as DECFs.

## Notes

- each argument of a []NA assigned name is an element in an array. if there's only one argument (like in =Z) then that argument MUST be a       scalar. foo 'argument' calls foo with an 8 element array. but you said you wanted one argument. So you must enclose it. remember that in APL  strings don't exist. there are just character arrays which have their structure unlike strings in other languages with are "scalars"
- enclosing a simple scalar has no effect: ⊂'x' is the same as 'x'. Different if you enclose a character array: ⊂,'x'

## ⎕NA Gotchas

### DLL names containing dots

⎕NA normally lets you omit the `.dll` extension:
```apl
⎕NA 'I4 MyLib|fn <0T1'         ⍝ loads MyLib.dll — works
```

If the DLL name itself contains a dot (e.g. `ZBus.Echo.dll`), Dyalog's ⎕NA parser
sees the first dot and treats everything after it as the extension. Result: `FILE ERROR`.

**Fix**: include the `.dll` extension explicitly in the path:
```apl
dll←'D:\path\to\ZBus.Echo.dll'
⎕NA 'I4 ',dll,'|zbus_init <0T1 >Z'   ⍝ works
```

### & (threaded) requires Z format

`PP` parameters do not support `&` (OS-threaded calls). Only `Z` works with `&`:
```apl
⎕NA 'I4 dll|fn& <0T1 >Z'    ⍝ OK
⎕NA 'I4 dll|fn& <PP >PP'     ⍝ NOPE — PP doesn't support &
```

### >Z output placeholders

Every `>Z` output parameter needs a placeholder argument (typically `0`) in the call:
```apl
'fn' ⎕NA 'I4 dll|fn <0T1 >Z >Z >Z'
(rc a b c) ← fn 'arg' 0 0 0   ⍝ three >Z → three 0 placeholders
```

### =Z to avoid placeholders

When a verb has both Z input and Z output, use `=Z` to reuse one slot for both.
The DLL reads input first, then overwrites the pointer with output. Saves an arg:
```apl
⍝ BAD: separate <Z input + >Z output = needs placeholder 0
'sub' ⎕NA 'I4 dll|fn <0T1 <0T1 <Z >Z'
(rc name) ← sub 'N1' 'leaf' 'subject' 0   ⍝ 0 = dummy for >Z

⍝ GOOD: =Z serves double duty (input consumed, then output written)
'sub' ⎕NA 'I4 dll|fn <0T1 <0T1 =Z'
(rc name) ← sub 'N1' 'leaf' 'subject'     ⍝ no placeholder needed
```
Only use `>Z` when there is no Z input to reuse (e.g., `zbus_init`, `zbus_wait`).

