# Dyalog's ⎕NA Z Format — Library & Specification

## References

- `docs/z-wire-format.md` — Full specification (empirically verified against Dyalog 20.0)
- `src/ZFormat/` — C# library: ZWriter + ZReader
- `tests/ZFormat.Tests/` — Unit tests with golden bytes from real interpreter dumps
- `samples/ZProbeManaged/` — AOT-compiled DLL loadable via ⎕NA (integration test)
- `probe/z_probe.c` — C probe DLL used for initial reverse-engineering

## Skills

- /dyalog-apl-runner        Run Dyalog APL scripts and inline expressions
- /dyalog-ride              Connect to a persistent Dyalog APL session via RIDE

## Upstream References

- C:\Users\stf\Source\Repos\Conga-Sharp\docs\z-wire-format.md
- C:\Users\stf\Source\Repos\Conga-Sharp\src\CongaSharp\ZWriter.cs
- D:\devel\dwa\v20
- C:\Users\stf\Source\Repos\bridge-dwa


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

