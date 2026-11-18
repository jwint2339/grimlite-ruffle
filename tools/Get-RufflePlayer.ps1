param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$playerDirectory = Join-Path $repository 'Player'
$version = '1.6.0-skua.5'
$expectedHash = 'A4D32E17CCA60DFAE24DCA27CFC0C15F2FFBAB8DB7AC6137CBACEB31EE76F676'
$downloaded = $false
if (-not $ArchivePath) {
    $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) ('grimlite-ruffle-' + [guid]::NewGuid().ToString('N') + '.zip')
    Invoke-WebRequest -Uri "https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v$version/windows.zip" -OutFile $ArchivePath
    $downloaded = $true
}
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The Ruffle archive did not match the pinned release checksum.'
}
$extraction = Join-Path ([IO.Path]::GetTempPath()) ('grimlite-ruffle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extraction -Force | Out-Null
try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extraction
    $executable = Join-Path $extraction 'Mugetsu\Mugetsu.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'The archive did not contain the expected Ruffle player.' }
    New-Item -ItemType Directory -Path $playerDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath (Split-Path -Parent $executable) -Force | Copy-Item -Destination $playerDirectory -Recurse -Force
    Write-Output "Prepared Mugetsu $version, including its licenses, in $playerDirectory"
}
finally {
    # These are explicitly created scratch paths; validate them before recursive cleanup.
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedExtraction = [IO.Path]::GetFullPath($extraction)
    if ($resolvedExtraction.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedExtraction -Recurse -Force
    }
    if ($downloaded) { Remove-Item -LiteralPath $ArchivePath -Force }
}
