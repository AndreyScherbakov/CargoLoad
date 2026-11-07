param(
    [string]$Output = "$PSScriptRoot\dist",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$Folder
)

# Домашние каталоги dotnet и NuGet держим в рабочей папке,
# чтобы сборка не зависела от прав на профиль пользователя.
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

$project = "$PSScriptRoot\CargoLoad\CargoLoad.csproj"

$publishArgs = @(
    $project, "-c", "Release", "-r", $Runtime, "--nologo",
    "-o", $Output,
    "--source", "https://api.nuget.org/v3/index.json",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

if ($Folder) {
    # Папка: exe + рядом библиотеки рантайма. Ничего не распаковывается при запуске.
    $publishArgs += "-p:PublishSingleFile=false"
}
else {
    # Один .exe: рантайм упакован внутрь, при первом запуске распаковывается в %TEMP%.
    $publishArgs += @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

if ($FrameworkDependent) {
    # Маленький размер, но требует установленного .NET 8 Desktop Runtime.
    $publishArgs += @("--self-contained", "false")
}
else {
    # Встроенный рантайм: работает без установки .NET.
    $publishArgs += @("--self-contained", "true")
}

dotnet publish @publishArgs
exit $LASTEXITCODE
