$ErrorActionPreference = 'Stop'
$nativeFolder = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$facade = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/netstandard') -Filter netstandard.dll -Recurse | Select-Object -First 1
if (-not $facade) { throw '.NET Framework 4.8 is required.' }
$cscArgs = @('/nologo','/optimize+','/platform:x64','/target:exe',('/out:'+(Join-Path $nativeFolder 'AudioHost.exe')),'/r:System.Web.Extensions.dll',('/r:'+$facade.FullName))
Get-ChildItem $nativeFolder -Filter 'NAudio*.dll' | ForEach-Object { $cscArgs += ('/r:'+$_.FullName) }
$cscArgs += (Join-Path $nativeFolder 'AudioHost.cs')
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw 'Native audio build failed.' }
& (Join-Path $nativeFolder 'AudioHost.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Native audio buffer self-test failed.' }
