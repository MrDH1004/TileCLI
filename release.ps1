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

Write-Host 'publish 중...'
dotnet publish $csproj -c Release | Out-Null
$exe = Join-Path $root 'src\TileCLI\bin\Release\net9.0-windows\win-x64\publish\TileCLI.exe'
if (-not (Test-Path $exe)) { throw "publish 산출물 없음: $exe" }

# 버전 커밋(소스만) + GitHub Release에 exe만 업로드
git -C $root add src/TileCLI/TileCLI.csproj
git -C $root commit -m "Release v$ver" | Out-Null
git -C $root push origin main | Out-Null
gh release create "v$ver" $exe --repo MrDH1004/TileCLI --title "TileCLI v$ver" --notes "TileCLI v$ver" | Out-Null
Write-Host "릴리스 완료: v$ver (exe 업로드됨)"
