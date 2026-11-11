# Folder History をビルドして、このユーザーにインストールし、起動します。管理者権限は要りません。
#
#   powershell -ExecutionPolicy Bypass -File tools\FolderHistory\deploy.ps1
#
#   -SkipTests  テストを実行しない
#   -NoStart    インストール後に起動しない
param(
    [switch]$SkipTests,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FolderHistory'
$exe = Join-Path $installDir 'FolderHistory.exe'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Folder History.lnk'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

function Check($text) {
    if ($LASTEXITCODE -ne 0) { throw "$text に失敗しました（終了コード $LASTEXITCODE）。上のメッセージを確認してください。" }
}

if (-not $SkipTests) {
    Step 'テストを実行'
    dotnet test (Join-Path $repo 'tests\Files.History.Tests')
    Check 'テスト'
}

Step '動いている Folder History を終了'
Get-Process FolderHistory -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Step "配布用の exe を作成: $installDir"
dotnet publish (Join-Path $repo 'tools\FolderHistory') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $installDir
Check '配布用の exe の作成'

Step 'スタートメニューにショートカットを作成'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $installDir
$link.Description = 'Folder History'
$link.Save()

if (-not $NoStart) {
    Step '起動'
    Start-Process $exe
}

Write-Host "`n完了しました。" -ForegroundColor Green
Write-Host "exe: $exe"
Write-Host 'スタートメニューで「Folder History」と入力すると起動できます。'
Write-Host '初回は、ツールバーの「自動起動」を一度オフにして、もう一度オンにすると、この exe の場所が登録されます。'
