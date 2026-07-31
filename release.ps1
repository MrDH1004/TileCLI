# TileCLI 릴리스: 버전 자동 증가 → 단일 exe publish → GitHub Release(v{ver})에 exe 업로드.
# 인스톨러는 '최초 1회'만 별도 'setup' 릴리스에 올리고(안정 링크), 이후 업데이트는 앱이 exe로 자동 처리.
# 사용: powershell -ExecutionPolicy Bypass -File release.ps1 [patch|minor|major] [-ForceInstaller]
param(
    [ValidateSet('patch', 'minor', 'major')][string]$bump = 'patch',
    [switch]$ForceInstaller   # 인스톨러를 강제로 다시 빌드·업로드(기본은 최초 1회만)
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

# 빌드 산출물(bin\)에서 실행 중인 TileCLI가 있으면 종료한다.
# 실행 중이면 publish가 파일 잠금(GenerateBundle: being used by another process)으로 실패하므로.
# 이 저장소 bin\ 경로 프로세스만 겨냥 — 설치본 등 다른 경로의 TileCLI는 건드리지 않는다.
$binPath = Join-Path $root 'src\TileCLI\bin'
$locking = Get-Process TileCLI -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($binPath, [System.StringComparison]::OrdinalIgnoreCase) }
if ($locking) {
    Write-Host "빌드 산출물 실행 중 프로세스 종료: PID $($locking.Id -join ', ')"
    $locking | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

$csproj = Join-Path $root 'src\TileCLI\TileCLI.csproj'
$text = [System.IO.File]::ReadAllText($csproj, [System.Text.Encoding]::UTF8)

if ($text -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') { throw '<Version>x.y.z</Version>를 csproj에서 찾지 못함' }
$maj = [int]$Matches[1]; $min = [int]$Matches[2]; $pat = [int]$Matches[3]
switch ($bump) {
    'major' { $maj++; $min = 0; $pat = 0 }
    'minor' { $min++; $pat = 0 }
    default { $pat++ }
}
$ver = "$maj.$min.$pat"
$text = $text -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$ver</Version>"
[System.IO.File]::WriteAllText($csproj, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "버전 -> $ver"

# 네이티브 명령 실패를 즉시 중단시키는 헬퍼(PS는 exit code로 자동 throw 안 함)
function Invoke-Checked([string]$desc, [scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$desc 실패(exit=$LASTEXITCODE)" }
}

Write-Host 'publish 중...'
Invoke-Checked 'dotnet publish' { dotnet publish $csproj -c Release | Out-Null }
$exe = Join-Path $root 'src\TileCLI\bin\Release\net9.0-windows\win-x64\publish\TileCLI.exe'
if (-not (Test-Path $exe)) { throw "publish 산출물 없음: $exe" }

# 버전 커밋(소스만) → 푸시 → GitHub Release에 exe만 업로드.
# 커밋이 실제로 생성됐는지 HEAD 이동으로 검증(빈 커밋/누락 방지).
$before = (git -C $root rev-parse HEAD).Trim()
Invoke-Checked 'git add' { git -C $root add src/TileCLI/TileCLI.csproj }
Invoke-Checked 'git commit' { git -C $root commit -m "Release v$ver" | Out-Null }
$after = (git -C $root rev-parse HEAD).Trim()
if ($before -eq $after) { throw "버전 커밋이 생성되지 않음(스테이징된 변경 없음?)" }
Invoke-Checked 'git push' { git -C $root push origin main | Out-Null }
Invoke-Checked 'gh release create' { gh release create "v$ver" $exe --repo MrDH1004/TileCLI --title "TileCLI v$ver" --notes "TileCLI v$ver" | Out-Null }
Write-Host "릴리스 완료: v$ver (exe 업로드됨, 커밋 $($after.Substring(0,7)))"

# ── 인스톨러: 최초 1회만 'setup' 릴리스에 업로드(안정 링크). 이후엔 exe만 배포되고 앱이 자동 업데이트.
#   setup 릴리스는 --latest=false 로 만들어, auto-update의 /releases/latest 가 항상 버전 릴리스를 가리키게 한다.
$setupTag = 'setup'
# gh 존재 확인(없으면 stderr+비정상 종료) — Stop 정책에서 중단되지 않게 EAP 완화 + stderr 무시.
$eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$names = gh release view $setupTag --repo MrDH1004/TileCLI --json assets --jq '.assets[].name' 2>$null
$installerExists = ($LASTEXITCODE -eq 0) -and ($names -match 'TileCLI-Setup')
$ErrorActionPreference = $eap

if ($installerExists -and -not $ForceInstaller) {
    Write-Host "인스톨러 이미 게시됨(setup) — 이번엔 exe만 배포(앱 자동 업데이트)."
}
else {
    # Inno Setup 컴파일러 탐지(퍼-유저 설치 경로 우선)
    $iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
    if (-not $iscc) { throw "인스톨러 업로드 필요하나 Inno Setup(ISCC.exe) 없음 — 'winget install --id JRSoftware.InnoSetup -e' 후 재실행" }

    Write-Host '인스톨러 컴파일 중...'
    Invoke-Checked 'ISCC 컴파일' { & $iscc (Join-Path $root 'installer\TileCLI.iss') | Out-Null }
    $setupExe = Get-ChildItem (Join-Path $root 'installer\Output') -Filter 'TileCLI-Setup-*.exe' |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $setupExe) { throw '인스톨러 산출물 없음(installer\Output)' }

    # 고정 파일명으로 복사 → 다운로드 링크가 버전과 무관하게 안정적
    $stable = Join-Path $setupExe.DirectoryName 'TileCLI-Setup.exe'
    Copy-Item $setupExe.FullName $stable -Force

    # setup 릴리스가 없으면 생성(latest 아님), 있으면 자산만 교체 업로드
    $eap2 = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    gh release view $setupTag --repo MrDH1004/TileCLI 1>$null 2>$null
    $setupExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $eap2
    if (-not $setupExists) {
        Invoke-Checked 'gh setup 릴리스 생성' { gh release create $setupTag $stable --repo MrDH1004/TileCLI --target main --latest=false --title 'TileCLI 설치 관리자' --notes '한 번만 이걸로 설치하세요. 이후 업데이트는 앱이 자동으로 받습니다(퍼-유저 설치, 관리자 불필요).' | Out-Null }
    }
    else {
        Invoke-Checked 'gh 인스톨러 업로드' { gh release upload $setupTag $stable --repo MrDH1004/TileCLI --clobber | Out-Null }
    }
    Write-Host "인스톨러 업로드됨(1회) → https://github.com/MrDH1004/TileCLI/releases/download/$setupTag/TileCLI-Setup.exe"
}
