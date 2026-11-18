param([string]$SourceDirectory)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'legacy-dependencies.json') -Raw | ConvertFrom-Json
$allowedPaths = @(
    'Dependency/AxInterop.ShockwaveFlashObjects.dll',
    'Dependency/Interop.ShockwaveFlashObjects.dll',
    'Dependency/DarkUI.dll',
    'Dependency/FlatTabControl.dll',
    'Dependency/Unity3.Eyedropper.dll',
    'Loader/grimoire.swf'
)
if ($manifest.repository -ne 'jib-akbr/Grimlite-Li' -or $manifest.commit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unexpected upstream dependency manifest.'
}

# Windows PowerShell 5.1 may otherwise negotiate an obsolete TLS version.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
foreach ($entry in $manifest.files) {
    if ($entry.path -cnotin $allowedPaths -or $entry.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Unexpected dependency path or checksum in manifest.'
    }
    $destination = Join-Path $repositoryRoot $entry.path
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Existing dependency has an unexpected checksum; left untouched: $($entry.path)"
        }
        Write-Output "Verified $($entry.path)"
        continue
    }

    $scratchFile = [IO.Path]::GetTempFileName()
    try {
        if ($SourceDirectory) {
            Copy-Item -LiteralPath (Join-Path $SourceDirectory $entry.path) -Destination $scratchFile -Force
        }
        else {
            $uri = "https://raw.githubusercontent.com/$($manifest.repository)/$($manifest.commit)/$($entry.path)"
            Invoke-WebRequest -Uri $uri -OutFile $scratchFile -UseBasicParsing
        }
        if ((Get-FileHash -LiteralPath $scratchFile -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Upstream dependency failed checksum verification: $($entry.path)"
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Move-Item -LiteralPath $scratchFile -Destination $destination
        Write-Output "Prepared $($entry.path)"
    }
    finally {
        if (Test-Path -LiteralPath $scratchFile) { Remove-Item -LiteralPath $scratchFile -Force }
    }
}
