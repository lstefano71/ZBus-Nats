$ErrorActionPreference = 'Stop'

$sampleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $sampleRoot
try {
    $vsInstaller = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
    $env:PATH = "$vsInstaller;$env:PATH"

    & dotnet publish .\ZProbeManaged\ZProbeManaged.csproj -c Release | Out-Host
    & 'd:\devel\dyalog\20.0\scriptbin\dyalogscript.ps1' .\run-e2e.apls
}
finally {
    Pop-Location
}
