# SkyLight 自動テスト：ビルド → DLL を入れる → fpfc で起動 → 曲を自動で始めて撮影 → 終了を待つ。
# 結果は <インスタンス>\UserData\SkyLight\AutoTest\ の shot-N.png と status.txt。
#
# 例: powershell -ExecutionPolicy Bypass -File scripts\autotest.ps1
#     powershell -ExecutionPolicy Bypass -File scripts\autotest.ps1 -Version 1.40.8 -Shots 2 -Level 100Bills
param(
    [string]$Version = "1.44.1",
    [string]$InstancesDir = "D:\BSManager\BSInstances",
    [string]$Level = "",
    [string]$Diff = "Expert",
    [double]$Delay = 5,
    [int]$Shots = 3,
    [double]$Interval = 2,
    [int]$TimeoutSec = 240,
    [string]$Env = "",      # 環境の serializedName（例 DefaultEnvironment）。空なら譜面の既定
    [switch]$ListEnvs,      # 曲を始めず、環境の一覧（envs.txt）だけ書いて終了する
    [switch]$MenuShot,      # 曲を始めず、設定画面を浮かべて撮る（menu-1/2/3.png）
    [string]$MenuShotObject = "",  # MenuShot で -Env の環境のこの候補を Hide した状態も撮る（撮影後に元へ戻す）
    [string[]]$Set = @(),   # 設定を一時的に変える（例 -Set "DumpScene=true","ShowRing=true"）。終了後に元へ戻す
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$inst = Join-Path $InstancesDir $Version
$exe = Join-Path $inst "Beat Saber.exe"
$outDir = Join-Path $inst "UserData\SkyLight\AutoTest"
$status = Join-Path $outDir "status.txt"

if (Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue) {
    Write-Host "Beat Saber is already running. Close it first."
    exit 2
}

if (-not $NoBuild) {
    dotnet build (Join-Path $repo "SkyLight\SkyLight.csproj") -c Debug -nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Copy-Item (Join-Path $repo "SkyLight\bin\Debug\net48\net48\SkyLight.dll") (Join-Path $inst "Plugins\") -Force

# -Set：SkyLight.json を退避してから書き換え、最後に必ず戻す（途中で止まっても finally で戻す）。
$cfg = Join-Path $inst "UserData\SkyLight.json"
$cfgBackup = "$cfg.autotest-backup"
if (Test-Path $cfgBackup) {
    # 前回が途中で止まって退避ファイルが残っている＝こちらが本当の元の設定。先に戻す。
    Move-Item $cfgBackup $cfg -Force
    Write-Host "Restored config left over from a previous run."
}
$result = "TIMEOUT"
try {
    if ($Set.Count -gt 0) {
        $json = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
        $names = $json.PSObject.Properties.Name
        foreach ($kv in (($Set -join ",").Split(",") | Where-Object { $_ -ne "" })) {
            $k, $v = $kv.Split("=", 2)
            if ($names -notcontains $k) { throw "Unknown setting '$k' (not in SkyLight.json)" }
            if ($v -eq "true") { $v = $true } elseif ($v -eq "false") { $v = $false }
            elseif ($v -match '^-?[0-9]+(\.[0-9]+)?$') { $v = [double]$v }
            $json.$k = $v
            Write-Host "Set: $k = $v"
        }
        Copy-Item $cfg $cfgBackup -Force
        [IO.File]::WriteAllText($cfg, ($json | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
    }

    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force $outDir | Out-Null

    # BSManager と同じ起動（--no-yeet fpfc）。Steam 版なので AppId を環境変数で渡す。
    $env:SteamAppId = "620980"
    $env:SteamGameId = "620980"
    $env:SteamOverlayGameId = "620980"
    $argList = @("--no-yeet", "fpfc", "--skylight-autotest", "--skylight-diff=$Diff",
        "--skylight-delay=$Delay", "--skylight-shots=$Shots", "--skylight-interval=$Interval")
    if ($Level -ne "") { $argList += "--skylight-level=$Level" }
    if ($Env -ne "") { $argList += "--skylight-env=$Env" }
    if ($ListEnvs) { $argList += "--skylight-listenvs" }
    if ($MenuShot) { $argList += "--skylight-menushot" }
    if ($MenuShotObject -ne "") { $argList += "--skylight-menushot-object=$MenuShotObject" }
    Write-Host "Launch: `"$exe`" $($argList -join ' ')"
    $proc = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $inst -PassThru

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if (Test-Path $status) {
            $last = (Get-Content $status -Encoding UTF8 | Select-Object -Last 1)
            if ($last -match " DONE$") { $result = "DONE"; break }
            if ($last -match " FAIL ") { $result = "FAIL"; break }
        }
        if ($proc.HasExited) { $result = "EXITED"; break }
    }
}
finally {
    # ゲームの終了を最大30秒待つ（DONE なら自分で終了する）。残っていたら止める。
    $waitUntil = (Get-Date).AddSeconds(30)
    while ((Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue) -and (Get-Date) -lt $waitUntil) { Start-Sleep -Seconds 1 }
    $bs = Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue
    if ($bs) { $bs | Stop-Process -Force; Write-Host "Beat Saber was stopped by the script."; Start-Sleep -Seconds 2 }
    if (Test-Path $cfgBackup) { Move-Item $cfgBackup $cfg -Force; Write-Host "Config restored." }
}

Write-Host "RESULT: $result"
if (Test-Path $status) { Get-Content $status -Encoding UTF8 }
Get-ChildItem $outDir -Filter "*.png" | ForEach-Object { Write-Host "PNG: $($_.FullName)" }

# SkyLight のログ（ゲーム終了後はロックが外れている）。
$logFile = Join-Path $inst "Logs\_latest.log"
if (Test-Path $logFile) {
    Select-String -Path $logFile -Pattern "\[SkyLight\]\[|SkyLight\] .*(Exception|Error)|autotest" |
        ForEach-Object { $_.Line.Substring(0, [Math]::Min(300, $_.Line.Length)) }
}
if ($result -eq "DONE") { exit 0 } else { exit 1 }
