# TileCLI 인스톨러 빌드: Release publish -> Inno Setup 컴파일 -> installer\Output\TileCLI-Setup-x.y.z.exe
# 사용: powershell -ExecutionPolicy Bypass -File build-installer.ps1
# 사전: Inno Setup 6 설치 필요 (winget install JRSoftware.InnoSetup)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root 'src\TileCLI\TileCLI.csproj'
$iss = Join-Path $root 'installer\TileCLI.iss'

# 이 저장소 bin\ 에서 실행 중인 TileCLI 종료(파일 잠금 방지)
$binPath = Join-Path $root 'src\TileCLI\bin'
Get-Process TileCLI -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($binPath, [System.StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

Write-Host 'publish 중...'
dotnet publish $csproj -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패(exit=$LASTEXITCODE)" }

# Inno Setup 컴파일러(ISCC) 탐지
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw "Inno Setup(ISCC.exe)를 찾지 못함 — 'winget install --id JRSoftware.InnoSetup -e' 후 재시도" }

Write-Host "ISCC: $iscc"
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC 컴파일 실패(exit=$LASTEXITCODE)" }

$out = Get-ChildItem (Join-Path $root 'installer\Output') -Filter 'TileCLI-Setup-*.exe' |
    Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host "인스톨러 생성: $($out.FullName) ($([math]::Round($out.Length/1MB,1)) MB)"
