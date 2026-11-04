# Sestaví overlay (Release) a vytvoří zástupce na ploše.
# Aplikace se spouští přes podepsaný dotnet.exe, protože Smart App Control blokuje nepodepsané .exe.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$project = Join-Path $root 'src\MtgDraftHelper.App\MtgDraftHelper.App.csproj'
$output = Join-Path $root 'src\MtgDraftHelper.App\bin\Release\net10.0-windows'
$dll = Join-Path $output 'MtgDraftHelper.App.dll'

& $dotnet build $project -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Sestavení selhalo.' }

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'MtG Draft Helper.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $dotnet
$shortcut.Arguments = "`"$dll`" --hide-console"
$shortcut.WorkingDirectory = $output
$shortcut.WindowStyle = 7 # minimalizovaně – okno konzole se hned zavře
$shortcut.IconLocation = "$(Join-Path $root 'src\MtgDraftHelper.App\Resources\app.ico'),0"
$shortcut.Description = 'MtG Arena draft helper (data 17Lands)'
$shortcut.Save()

Write-Host "Zástupce vytvořen: $shortcutPath"
