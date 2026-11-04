# Sestaví overlay a vytvoří zástupce na ploše.
# Aplikace se spouští přes podepsaný dotnet.exe, protože Smart App Control blokuje nepodepsané .exe.
# Smart App Control ale posuzuje i jednotlivé .dll (podle cloudové reputace), takže sestavení může
# být zablokované – skript to po sestavení ověří zkušebním spuštěním.

param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$project = Join-Path $root 'src\MtgDraftHelper.App\MtgDraftHelper.App.csproj'
$output = Join-Path $root "src\MtgDraftHelper.App\bin\$Configuration\net10.0-windows"
$dll = Join-Path $output 'MtgDraftHelper.App.dll'

& $dotnet build $project -c $Configuration -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Sestavení selhalo.' }

# Zkušební spuštění: vykreslí okno do dočasného obrázku a skončí.
$probe = Join-Path $env:TEMP 'mtg-draft-helper-probe.png'
$ErrorActionPreference = 'Continue'
$proc = Start-Process -FilePath $dotnet -ArgumentList "`"$dll`" --screenshot `"$probe`" --screenshot-delay 2" -Wait -PassThru -WindowStyle Hidden
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $probe)) {
    throw "Aplikaci se nepodařilo spustit – pravděpodobně ji zablokoval Smart App Control (viz Prohlížeč událostí, CodeIntegrity)."
}
Remove-Item $probe

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
