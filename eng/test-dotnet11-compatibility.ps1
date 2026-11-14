param (
    [Parameter(Mandatory)][string]$Workspace,
    [Parameter(Mandatory)][string]$DotnetRoot,
    [Parameter(Mandatory)][string]$PackageVersion
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $Workspace 'xsl3-packages'
$scratch = Join-Path $Workspace 'xsl3-net11'
$fixture = Join-Path $repo 'tests/Xsl3.Net11Compatibility'

$env:DOTNET_ROOT = $DotnetRoot
$env:DOTNET_ROOT_X64 = $DotnetRoot
$env:DOTNET_ROOT_ARM64 = $DotnetRoot
$env:PATH = "$DotnetRoot$([IO.Path]::PathSeparator)$env:PATH"
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
$env:NUGET_PACKAGES = Join-Path $scratch 'packages'

New-Item -ItemType Directory -Path $scratch | Out-Null
Copy-Item "$fixture/*" $scratch -Exclude bin,obj
Push-Location $scratch
try {
    $sdk = dotnet --version
    $runtimes = @(dotnet --list-runtimes)
    if ($sdk -notmatch '^11\.' -or ($runtimes -notmatch '^\S+ 11\.').Count -ne 0 -or
        ($runtimes -match '^Microsoft\.NETCore\.App 11\.').Count -ne 1) {
        throw "Expected an isolated .NET 11 SDK and runtime. SDK: $sdk. Runtimes: $runtimes"
    }
    Write-Host "SDK $sdk"
    $runtimes | Write-Host

    $tests = Join-Path $repo 'tests/Xsl3.Tests/bin/Release/net10.0/Xsl3.Tests.dll'
    dotnet vstest $tests '/TestCaseFilter:FullyQualifiedName~Xsl3.Tests.BridgeTests' `
        '/Logger:trx;LogFileName=bridges.trx' '/ResultsDirectory:TestResults'
    [xml]$results = Get-Content TestResults/bridges.trx -Raw
    $counters = $results.TestRun.ResultSummary.Counters
    if ([int]$counters.total -le 0 -or $counters.total -ne $counters.passed) {
        throw 'The XML bridge check requires at least one test and every selected test must pass.'
    }

    $source = [System.Security.SecurityElement]::Escape($feed)
    @"
<configuration>
  <packageSources>
    <clear />
    <add key="ci-artifacts" value="$source" />
  </packageSources>
</configuration>
"@ | Set-Content NuGet.Config
    dotnet build -c Release "-p:Xsl3PackageVersion=$PackageVersion"

    dotnet tool install Xsl3.Cli --version $PackageVersion --tool-path tools --configfile NuGet.Config
    $command = if ($IsWindows) { './tools/xsl3.exe' } else { './tools/xsl3' }
    & $command transform -xsl:Report.xsl -s:Input.xml -o:cli-output.txt
    & $command compile -xsl:Report.xsl -o:CliReport.dll -type:Stylesheets.Report --debug
    dotnet bin/Release/net11.0/Xsl3.Net11Compatibility.dll
}
finally {
    Pop-Location
}
