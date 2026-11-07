param(
    [string]$Project = "$PSScriptRoot\CargoLoad\CargoLoad.csproj",
    [string]$Configuration = "Debug"
)

# Песочница DSH не даёт писать в профиль пользователя, поэтому домашние
# каталоги dotnet и NuGet перенаправлены внутрь рабочей папки.
$env:DOTNET_CLI_HOME        = "$PSScriptRoot\.dotnet-home"
$env:NUGET_PACKAGES         = "$PSScriptRoot\.nuget"
$env:APPDATA                = "$PSScriptRoot\.appdata"
$env:LOCALAPPDATA           = "$PSScriptRoot\.localappdata"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO          = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

foreach ($dir in $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, "$env:APPDATA\NuGet", $env:LOCALAPPDATA) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

dotnet build $Project -c $Configuration --nologo
exit $LASTEXITCODE
