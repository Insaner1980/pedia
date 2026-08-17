#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$SonarArgs
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [Console]::OutputEncoding

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$ReportPath
    )

    & dotnet @Arguments 2>&1 |
        Tee-Object -FilePath $ReportPath -Append |
        Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-komento epäonnistui (exit $LASTEXITCODE)."
    }
}

if ($SonarArgs.Count -gt 0) {
    $cli = Get-Command sonar.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $cli) {
        throw 'sonar.exe ei löytynyt PATHista.'
    }

    & $cli.Source @SonarArgs
    exit $(if ($null -ne $global:LASTEXITCODE) { [int]$global:LASTEXITCODE } else { 0 })
}

if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
    throw 'SONAR_TOKEN ei ole asetettu tälle PowerShell-istunnolle.'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$reportsDir = Join-Path $repoRoot 'reports'
$reportPath = Join-Path $reportsDir 'sonar.txt'
$testResultsDir = Join-Path $reportsDir 'sonar-test-results'

New-Item -ItemType Directory -Force -Path $reportsDir | Out-Null
$reportsRoot = [IO.Path]::GetFullPath($reportsDir) + [IO.Path]::DirectorySeparatorChar
$testResultsRoot = [IO.Path]::GetFullPath($testResultsDir) + [IO.Path]::DirectorySeparatorChar
if (-not $testResultsRoot.StartsWith($reportsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Testitulosten polku ei ole reports-kansion sisällä: $testResultsRoot"
}
if (Test-Path -LiteralPath $testResultsDir) {
    Remove-Item -LiteralPath $testResultsDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null
Set-Content -LiteralPath $reportPath -Encoding utf8 -Value @(
    'sonar'
    "Root: $repoRoot"
    'Project: Insaner1980_pedia'
    "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ''
)

Push-Location -LiteralPath $repoRoot
try {
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'restore'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'restore', 'Pedia.sln', '-p:Platform=x64'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'run', 'dotnet-sonarscanner', '--', 'begin'
        '/k:Insaner1980_pedia'
        '/o:insaner1980'
        "/d:sonar.token=$env:SONAR_TOKEN"
        '/d:sonar.cs.opencover.reportsPaths=reports/sonar-test-results/**/coverage.opencover.xml'
        '/d:sonar.cs.vstest.reportsPaths=reports/sonar-test-results/*.trx'
        '/d:sonar.coverage.exclusions=src/Pedia.App/**'
        '/d:sonar.qualitygate.wait=true'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'build', 'Pedia.sln', '-c', 'Release', '-p:Platform=x64', '--no-restore', '--no-incremental'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'test', 'tests\Pedia.Tests\Pedia.Tests.csproj'
        '-c', 'Release'
        '-p:Platform=x64'
        '--no-restore'
        '--no-build'
        '--results-directory', 'reports\sonar-test-results'
        '--logger', 'trx;LogFileName=sonar.trx'
        '--collect', 'XPlat Code Coverage'
        '--'
        'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'run', 'dotnet-sonarscanner', '--', 'end'
        "/d:sonar.token=$env:SONAR_TOKEN"
    )
}
finally {
    Pop-Location
}
