# Development runbook

## Run the APL test script

The repo currently has one test script:

- `probe\utf_probe_test.apls`
- `samples\ZProbeManaged\run-e2e.apls`

Run it with Dyalog Script:

```powershell
& "d:\devel\dyalog\20.0\scriptbin\dyalogscript.ps1" "D:\devel\Z-Format\probe\utf_probe_test.apls"
```

The script writes its log to `%TEMP%\utf_probe_log.txt`.

To run the sample DLL end-to-end, use:

```powershell
cd D:\devel\Z-Format\samples\ZProbeManaged
.\run-e2e.ps1
```

## Build the C probe DLLs

Use the Visual C++ environment from `vcvars64.bat` before invoking `cl.exe`.

Known working location in this setup:

```powershell
D:\Program Files\Microsoft Visual Studio\2026\VC\Auxiliary\Build\vcvars64.bat
```

If that path is not present, find it with `vswhere.exe`:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
  -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
  -find **\VC\Auxiliary\Build\vcvars64.bat
```

Build the probes from a Developer Command Prompt shell:

```powershell
& $env:ComSpec /c "call `\"D:\Program Files\Microsoft Visual Studio\2026\VC\Auxiliary\Build\vcvars64.bat`\" >nul && cd /d D:\devel\Z-Format\probe && cl /LD /O2 z_probe.c /Fe:z_probe.dll"
& $env:ComSpec /c "call `\"D:\Program Files\Microsoft Visual Studio\2026\VC\Auxiliary\Build\vcvars64.bat`\" >nul && cd /d D:\devel\Z-Format\probe && cl /LD /O2 utf_probe.c /Fe:utf_probe.dll"
```

## Notes

- `vcvars64.bat` must run in the same command session as `cl.exe`.
- The generated DLLs live in `probe\`.
- Re-run the APL script after rebuilding a probe DLL to refresh the log.
