[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    & dotnet restore ManagedD100.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build ManagedD100.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet test ManagedD100.sln --configuration Release --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & dotnet pack src/Rodinbell.D100/Rodinbell.D100.csproj --configuration Release --no-build --no-restore --output artifacts/packages
    if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
}
finally { Pop-Location }
