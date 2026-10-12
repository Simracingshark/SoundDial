$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SoundDial.Linux.csproj'
$destination = Join-Path $PSScriptRoot 'publish-linux-x64'
dotnet publish $project -c Release -r linux-x64 --self-contained true -o $destination
Write-Host "Linux build: $destination"
