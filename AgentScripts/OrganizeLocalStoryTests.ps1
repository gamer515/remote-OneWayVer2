param([string]$Root, [string]$UnityCli='unity')
$ErrorActionPreference='Stop'
$taskProject=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskArgs=if([string]::IsNullOrWhiteSpace($Root)){'[]'}else{ConvertTo-Json -InputObject @([IO.Path]::GetFullPath($Root)) -Compress}
# Uses the same archive writer as Unity generation. Requires the editor open, Play stopped.
# The Editor resolves its own persistentDataPath and validates explicit roots against
# that location or the isolated fixture folder. Never infer another user's save root.
$response=& $UnityCli command run_script --file (Join-Path $taskProject 'AgentScripts/LocalStoryArchiveCheck.cs') --entry LocalStoryArchiveCheck.Organize --args $taskArgs --project-path $taskProject --format json
$response
$parsed=($response -join "`n")|ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or !$parsed.success -or !$parsed.data.result.success){throw 'Editor cleanup failed; inspect the returned diagnostics.'}
