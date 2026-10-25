param([string]$OutputDirectory = 'dist\RAWER-0.6.2')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '.NET Framework C# compiler was not found.' }
$taskOutput = Join-Path $taskRoot $OutputDirectory
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$taskCommon = @('/nologo','/optimize+','/debug-','/platform:x64','/codepage:65001',('/win32icon:' + (Join-Path $taskRoot 'assets\RAWER.ico')),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
foreach ($taskArtName in @('source','compression','folder','results')) {
    $taskArtPath = Join-Path $taskRoot ('assets\comic-0.6\' + $taskArtName + '.png')
    if (-not (Test-Path -LiteralPath $taskArtPath)) {throw ('Missing artwork: ' + $taskArtName)}
    $taskCommon += '/resource:' + $taskArtPath + ',Rawer.Art.' + $taskArtName + '.png'
}
& $taskCompiler @taskCommon '/target:winexe' ('/out:' + (Join-Path $taskOutput 'RAWER.exe')) @taskSources
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
& $taskCompiler @taskCommon '/target:exe' ('/out:' + (Join-Path $taskOutput 'RAWER.Cli.exe')) @taskSources
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
Get-Item -LiteralPath (Join-Path $taskOutput 'RAWER.exe'),(Join-Path $taskOutput 'RAWER.Cli.exe') | Select-Object Name,Length
