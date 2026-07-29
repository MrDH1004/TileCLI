# TileCLI 릴리스: 버전 자동 증가 → 단일 exe publish → GitHub Release에 exe만 업로드.
# 사용: powershell -ExecutionPolicy Bypass -File release.ps1 [patch|minor|major]  (기본 patch)
param([ValidateSet('patch', 'minor', 'major')][string]$bump = 'patch')
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
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
