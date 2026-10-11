param([string]$Root='C:/Users/유영선/AppData/LocalLow/DefaultCompany/OneWayInVc/Saves/LocalStoryTests')
$ErrorActionPreference='Stop'
$testRoot=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
$knownRoot=[IO.Path]::GetFullPath('C:/Users/유영선/AppData/LocalLow/DefaultCompany/OneWayInVc/Saves/LocalStoryTests').TrimEnd('\','/')
$fixtureRoot=[IO.Path]::GetFullPath('C:/remote-OneWayVer2/tmp/LocalStoryOrganizationTests')+[IO.Path]::DirectorySeparatorChar
if($testRoot -ne $knownRoot -and !$testRoot.StartsWith($fixtureRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Root is outside the explicitly allowed test locations.'}
$cli='C:/Users/유영선/AppData/Local/Unity/bin/unity.exe'
$argsJson=ConvertTo-Json -InputObject @($testRoot) -Compress
# Uses the same archive writer as Unity generation. Requires the editor open, Play stopped.
$response=& $cli command run_script --file AgentScripts/LocalStoryArchiveCheck.cs --entry LocalStoryArchiveCheck.Organize --args $argsJson --project-path 'C:/remote-OneWayVer2' --format json
$response
$parsed=($response -join "`n")|ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or !$parsed.success -or !$parsed.data.result.success){throw 'Editor cleanup failed; inspect the returned diagnostics.'}
