# ZBus.Nats Test Runner
# Starts nats-server with JetStream, runs all APL test scripts, reports results.
# Usage: .\run_tests.ps1 [-NoBuild]

param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$natsServer = 'D:\devel\nats\2.14.2\nats-server.exe'
$dyalogScript = 'd:\devel\dyalog\20.0\scriptbin\dyalogscript.ps1'
$testDir = "$PSScriptRoot"
$projectDir = Split-Path $testDir -Parent

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
Write-Host "`nStarting nats-server (JetStream)..." -ForegroundColor Cyan
$natsProc = Start-Process -FilePath $natsServer -ArgumentList "-js" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1

# Verify it's running
if ($natsProc.HasExited) {
    Write-Host "nats-server failed to start!" -ForegroundColor Red
    exit 1
}
Write-Host "nats-server PID: $($natsProc.Id)" -ForegroundColor DarkGray

# ── Run tests ────────────────────────────────────────────────────────
$tests = @(
    'test_nats_smoke.apls'
    'test_nats_core.apls'
    'test_nats_request.apls'
    'test_nats_jetstream.apls'
    'test_nats_kv.apls'
    'test_nats_objsvc.apls'
)

$totalPassed = 0
$totalFailed = 0

Write-Host "`n════════════════════════════════════════════" -ForegroundColor White
Write-Host " Running $($tests.Count) test suites" -ForegroundColor White
Write-Host "════════════════════════════════════════════`n" -ForegroundColor White

foreach ($test in $tests) {
    $testPath = Join-Path $testDir $test
    if (-not (Test-Path $testPath)) {
        Write-Host "  SKIP $test (not found)" -ForegroundColor Yellow
        continue
    }

    Write-Host "  Running $test..." -NoNewline
    $output = & $dyalogScript $testPath 2>&1 | Out-String
    
    if ($output -match 'SOME TESTS FAILED|FAILED') {
        if ($output -notmatch 'ALL.*PASSED') {
            Write-Host " FAIL" -ForegroundColor Red
            $totalFailed++
            $output -split "`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
        } else {
            # Edge case: output contains both "FAILED" keyword and "ALL PASSED"
            Write-Host " PASS" -ForegroundColor Green
            $totalPassed++
        }
    } else {
        Write-Host " PASS" -ForegroundColor Green
        $totalPassed++
    }
}

# ── Stop NATS server ─────────────────────────────────────────────────
Write-Host "`nStopping nats-server..." -ForegroundColor DarkGray
Stop-Process -Id $natsProc.Id -ErrorAction SilentlyContinue

# ── Summary ──────────────────────────────────────────────────────────
Write-Host "`n════════════════════════════════════════════" -ForegroundColor White
if ($totalFailed -eq 0) {
    Write-Host " ALL $totalPassed TEST SUITES PASSED" -ForegroundColor Green
} else {
    Write-Host " $totalPassed passed, $totalFailed FAILED" -ForegroundColor Red
}
Write-Host "════════════════════════════════════════════" -ForegroundColor White

exit $totalFailed
