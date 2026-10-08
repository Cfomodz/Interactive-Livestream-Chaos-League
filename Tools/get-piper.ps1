# Downloads Piper (offline neural text to speech) and the game's default voices into
# Assets/StreamingAssets/Piper, where the game looks for them. They're large, so they're
# gitignored; run this once per clone. Builds copy the folder in.
#
#   powershell -ExecutionPolicy Bypass -File Tools/get-piper.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/get-piper.ps1 -Voices en_US-joe-medium,en_GB-vctk-medium
#
# Voices are listed at https://huggingface.co/rhasspy/piper-voices (voices.json). Check each voice's
# MODEL_CARD for its license before using it on a monetized stream: some are non-commercial only.
# The defaults are CC0 (joe) and CC BY 4.0 (libritts_r, which needs credit; see the README).

param(
    [string[]]$Voices = @('en_US-joe-medium', 'en_US-libritts_r-medium')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Join-Path $PSScriptRoot '..\Assets\StreamingAssets\Piper'
$voicesDir = Join-Path $root 'voices'
New-Item -ItemType Directory -Force $root, $voicesDir | Out-Null

# The last standalone Windows build. Piper's development moved to OHF-Voice/piper1-gpl, which ships as
# a Python package; this build runs today's voice models.
$piperExe = Join-Path $root 'piper\piper.exe'
if (-not (Test-Path $piperExe)) {
    Write-Host 'Downloading Piper...'
    $zip = Join-Path $env:TEMP 'piper_windows_amd64.zip'
    Invoke-WebRequest 'https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip' -OutFile $zip
    Expand-Archive $zip -DestinationPath $root -Force
    Remove-Item $zip
}
$license = Join-Path $root 'piper\LICENSE.md'
if (-not (Test-Path $license)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/rhasspy/piper/master/LICENSE.md' -OutFile $license
}

$catalog = Invoke-RestMethod 'https://huggingface.co/rhasspy/piper-voices/resolve/main/voices.json'
foreach ($voice in $Voices) {
    $entry = $catalog.$voice
    if (-not $entry) { Write-Warning "There's no Piper voice called $voice."; continue }
    foreach ($file in $entry.files.PSObject.Properties.Name) {
        $name = Split-Path $file -Leaf
        if ($name -eq 'MODEL_CARD') { $name = "$voice.MODEL_CARD.md" }
        $target = Join-Path $voicesDir $name
        if (Test-Path $target) { continue }
        Write-Host "Downloading $name..."
        Invoke-WebRequest "https://huggingface.co/rhasspy/piper-voices/resolve/main/$file" -OutFile $target
    }
}

Write-Host "Piper is ready in $((Resolve-Path $root).Path)"
