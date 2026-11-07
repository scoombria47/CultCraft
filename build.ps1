param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Cult of the Lamb',
    [string]$MinecraftJar = "$env:APPDATA\.minecraft\versions\1.21\1.21.jar",
    [string]$BepInExRoot = $GameRoot,
    [string]$PythonExe = 'python',
    [switch]$PersonalBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$gameManaged = Join-Path $GameRoot 'Cult Of The Lamb_Data\Managed'
$preflightArguments = @('--jar', $MinecraftJar)
if ($PersonalBuild) { $preflightArguments += '--allow-missing-local-assets' }
& $PythonExe (Join-Path $projectRoot 'generate.py') @preflightArguments
if ($LASTEXITCODE -ne 0) { throw 'Design sheet preflight failed; compilation skipped.' }
$dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
$sdk = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$outputDir = Join-Path $projectRoot 'dist\plugins'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$compilerArguments = [System.Collections.Generic.List[string]]::new()
foreach ($option in @('/nologo','/target:library','/nostdlib+','/langversion:latest','/optimize+','/deterministic+','/warn:4')) { $compilerArguments.Add($option) }
$compilerArguments.Add('/out:' + (Join-Path $outputDir 'CultCraft.dll'))
foreach ($reference in (Get-ChildItem -LiteralPath $gameManaged -Filter '*.dll')) { $compilerArguments.Add('/reference:'+$reference.FullName) }
foreach ($reference in (Get-ChildItem -LiteralPath (Join-Path $BepInExRoot 'BepInEx\core') -Filter '*.dll' | Where-Object { $_.Name -ne '0Harmony20.dll' })) { $compilerArguments.Add('/reference:'+$reference.FullName) }
foreach ($source in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs')) { $compilerArguments.Add($source.FullName) }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'dist\.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# Keep the installed reference list below Windows' process command-line limit.
$responseFile = Join-Path $projectRoot 'dist\CultCraft.rsp'
$responseArguments = foreach ($argument in $compilerArguments) { '"' + $argument.Replace('"', '\"') + '"' }
[System.IO.File]::WriteAllLines($responseFile, [string[]]$responseArguments, [System.Text.UTF8Encoding]::new($false))
& $dotnetExe $compiler ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
foreach ($document in @('README.md', 'INSTALL.md', 'LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $outputDir
}
Get-Item -LiteralPath (Join-Path $outputDir 'CultCraft.dll') | Select-Object Name,Length
