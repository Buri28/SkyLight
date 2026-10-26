# 全環境の「非表示にできるオブジェクト」候補を集める。
# 環境ごとに autotest.ps1 で曲を始め、候補一覧（candidates.txt）とスクリーンショットを
# <インスタンス>\UserData\SkyLight\EnvCandidates\<環境名>.txt / .png に保存する。
#
# 例: powershell -ExecutionPolicy Bypass -File scripts\collect-env-candidates.ps1
#     powershell -ExecutionPolicy Bypass -File scripts\collect-env-candidates.ps1 -Only "DefaultEnvironment","BTSEnvironment"
param(
    [string]$Version = "1.44.1",
    [string]$InstancesDir = "D:\BSManager\BSInstances",
    [string[]]$Only = @(),
    [double]$Delay = 3
)

$ErrorActionPreference = "Stop"
$autotest = Join-Path $PSScriptRoot "autotest.ps1"
$inst = Join-Path $InstancesDir $Version
$outDir = Join-Path $inst "UserData\SkyLight\AutoTest"
$collectDir = Join-Path $inst "UserData\SkyLight\EnvCandidates"
New-Item -ItemType Directory -Force $collectDir | Out-Null

# 1) 環境の一覧（最初の1回だけビルドする）
& powershell -ExecutionPolicy Bypass -File $autotest -Version $Version -ListEnvs | Select-String "RESULT"
$envs = Get-Content (Join-Path $outDir "envs.txt") -Encoding UTF8 | ForEach-Object { $_.Split("`t")[0] } | Where-Object { $_ -ne "" }
$onlyList = ($Only -join ",").Split(",") | Where-Object { $_ -ne "" }
if ($onlyList.Count -gt 0) { $envs = $envs | Where-Object { $onlyList -contains $_ } }
Copy-Item (Join-Path $outDir "envs.txt") (Join-Path $collectDir "envs.txt") -Force
Write-Host "Environments: $($envs.Count)"

# 2) 環境ごとに1曲
$summary = @()
foreach ($e in $envs) {
    $r = & powershell -ExecutionPolicy Bypass -File $autotest -Version $Version -NoBuild -Env $e -Delay $Delay -Shots 1 2>&1 | Select-String "RESULT"
    $cand = Join-Path $outDir "candidates.txt"
    $shot = Join-Path $outDir "shot-1.png"
    if (Test-Path $cand) { Copy-Item $cand (Join-Path $collectDir "$e.txt") -Force }
    if (Test-Path $shot) { Copy-Item $shot (Join-Path $collectDir "$e.png") -Force }
    $line = "$e : $r : candidates=$(Test-Path $cand)"
    Write-Host $line
    $summary += $line
}
$summary | Set-Content (Join-Path $collectDir "summary.txt") -Encoding UTF8
Write-Host "Saved to $collectDir"
