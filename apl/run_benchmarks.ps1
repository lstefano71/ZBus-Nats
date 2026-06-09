# ZBus.Nats Benchmark Runner
# Starts nats-server, runs APL benchmark scripts, reports results.
# Usage: .\run_benchmarks.ps1 [-NoBuild]

param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$natsServer = 'D:\devel\nats\2.14.2\nats-server.exe'
$dyalogScript = 'd:\devel\dyalog\20.0\scriptbin\dyalogscript.ps1'
$benchDir = "$PSScriptRoot"
$projectDir = Split-Path $benchDir -Parent

# ── Build ────────────────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Host "Building ZBus.Nats (AOT)..." -ForegroundColor Cyan
    $env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
    $buildResult = dotnet publish "$projectDir\src\ZBus.Nats\ZBus.Nats.csproj" -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED" -ForegroundColor Red
        $buildResult | Write-Host
        exit 1
    }
    Write-Host "Build OK" -ForegroundColor Green
}

# ── Start NATS server ────────────────────────────────────────────────
$existingNats = Get-Process -Name nats-server -ErrorAction SilentlyContinue
if ($existingNats) {
    Write-Host "Using existing nats-server (PID: $($existingNats.Id))" -ForegroundColor DarkGray
    $natsProc = $null
} else {
    Write-Host "`nStarting nats-server (JetStream)..." -ForegroundColor Cyan
    $natsProc = Start-Process -FilePath $natsServer -ArgumentList "-js" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1

    if ($natsProc.HasExited) {
        Write-Host "nats-server failed to start!" -ForegroundColor Red
        exit 1
    }
    Write-Host "nats-server PID: $($natsProc.Id)" -ForegroundColor DarkGray
}

# ── Run benchmarks ───────────────────────────────────────────────────
$benchmarks = @(
    'bench_nats_pubsub.apls'
)

$totalPassed = 0
$totalFailed = 0

Write-Host "`n════════════════════════════════════════════" -ForegroundColor White
Write-Host " Running $($benchmarks.Count) benchmark(s)" -ForegroundColor White
Write-Host "════════════════════════════════════════════`n" -ForegroundColor White

foreach ($bench in $benchmarks) {
    $benchPath = Join-Path $benchDir $bench
    if (-not (Test-Path $benchPath)) {
        Write-Host "  SKIP $bench (not found)" -ForegroundColor Yellow
        continue
    }

    Write-Host "  Running $bench..." -ForegroundColor Cyan
    $output = & $dyalogScript $benchPath 2>&1 | Out-String

    if ($output -match 'FAIL|FATAL') {
        Write-Host "  FAIL" -ForegroundColor Red
        $totalFailed++
        $output -split "`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
    } else {
        $totalPassed++
        # Print the benchmark output (it's the point)
        $output -split "`n" | ForEach-Object { Write-Host "  $_" }
    }
}

# ── Stop NATS server (only if we started it) ─────────────────────────
if ($natsProc) {
    Write-Host "`nStopping nats-server..." -ForegroundColor DarkGray
    Stop-Process -Id $natsProc.Id -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────
Write-Host "`n════════════════════════════════════════════" -ForegroundColor White
if ($totalFailed -eq 0) {
    Write-Host " ALL $totalPassed BENCHMARK(S) PASSED" -ForegroundColor Green
} else {
    Write-Host " $totalPassed passed, $totalFailed FAILED" -ForegroundColor Red
}
Write-Host "════════════════════════════════════════════" -ForegroundColor White

exit $totalFailed
