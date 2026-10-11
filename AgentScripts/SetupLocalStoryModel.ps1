param([Parameter(Mandatory=$true)][ValidateSet('Runtime','Model')][string]$Part)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskCache = Join-Path $taskRoot 'tmp/LocalStorySetup'
$taskModels = Join-Path $taskRoot 'Assets/StreamingAssets/LocalStory'
New-Item -ItemType Directory -Force -Path $taskCache,$taskModels | Out-Null
function Get-VerifiedDownload($Url,$Path,$Sha) {
    if (!(Test-Path -LiteralPath $Path)) {
        & curl.exe --fail --location --retry 3 --output ($Path + '.part') $Url
        if ($LASTEXITCODE -ne 0) { throw 'Download failed' }
        Move-Item -LiteralPath ($Path + '.part') -Destination $Path
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha) {
        throw ('SHA256 mismatch: ' + $Path)
    }
}
if ($Part -eq 'Model') {
    Get-VerifiedDownload 'https://huggingface.co/bartowski/Qwen_Qwen3-4B-Instruct-2507-GGUF/resolve/ae44f08e1392f39c0e474af10c3ff8355c8b6688/Qwen_Qwen3-4B-Instruct-2507-Q4_K_M.gguf' (Join-Path $taskModels 'Qwen3-4B-Instruct-2507-Q4_K_M.gguf') '2fde00ce69dd4899c70d020845e2638353015bba0fdf161b3eb965f2bca4464e'
    Write-Output 'Model SHA256 verified (2,497,280,736 bytes).'
} else {
    $taskZip = Join-Path $taskCache 'LlamaLib-v2.0.5.zip'
    Get-VerifiedDownload 'https://github.com/undreamai/LlamaLib/releases/download/v2.0.5/LlamaLib-v2.0.5.zip' $taskZip 'f3d85b2d6fbaa12839173f4438a7a2c7124bf6b25708d8be23348de2dec1156b'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZip)
    try {
        $taskEntries = @($taskArchive.Entries | Where-Object { $_.FullName -match 'runtimes/win-x64/native/.*(avx2|tinyblas)\.dll$|/LICENSE$|third_party_licenses/' })
        $taskEntries | Select-Object FullName,Length
        foreach ($taskEntry in $taskEntries) {
            if (!$taskEntry.Name) { continue }
            $taskRelative = $taskEntry.FullName.Substring($taskEntry.FullName.IndexOf('/') + 1)
            $taskTarget = Join-Path $taskModels $taskRelative
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($taskTarget)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry,$taskTarget,$true)
        }
    } finally { $taskArchive.Dispose() }
}
