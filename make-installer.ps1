param(
    [string]$Version = "1.2.1",
    [switch]$SkipSign
)

# Сборка установщика «Раскладка грузов»:
#   1) publish приложения в pkg\app
#   2) подпись exe (самоподписанный сертификат)
#   3) компиляция установщика Inno Setup
#   4) подпись установщика

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$env:DOTNET_CLI_HOME = "$root\.dotnet-home"
$env:NUGET_PACKAGES = "$root\.nuget"
$env:APPDATA = "$root\.appdata"
$env:LOCALAPPDATA = "$root\.localappdata"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

foreach ($dir in $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, "$env:APPDATA\NuGet", $env:LOCALAPPDATA) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

$pkg = Join-Path $root 'pkg\app'
$iss = Join-Path $root 'installer\setup.iss'
$outDir = Join-Path $root 'installer\out'
$exeName = 'CargoLoader.exe'

# --- инструменты ---

$iscc = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe',
    (Join-Path $env:USERPROFILE 'AppData\Local\Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Не найден ISCC.exe (Inno Setup). Установите: winget install JRSoftware.InnoSetup"
}

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

function Get-SigningCert {
    $subject = "CN=CargoLoad Self-Signed Code Signing"
    $cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
        Write-Host "Создан сертификат: $($cert.Thumbprint)"
    }
    return $cert
}

function Sign-File([string]$Path, $Cert) {
    if (-not $signtool) { Write-Warning "signtool не найден — подпись пропущена"; return }
    if (-not $Cert) { Write-Warning "нет сертификата — подпись пропущена"; return }

    & $signtool.FullName sign /sha1 $Cert.Thumbprint /fd SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool вернул код $LASTEXITCODE для $Path" }
    $sig = Get-AuthenticodeSignature -FilePath $Path
    Write-Host "Подписано: $([IO.Path]::GetFileName($Path)) — статус $($sig.Status)"
}

# --- 1. Публикация ---

Write-Host "[1/4] Публикация приложения..."
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }

dotnet publish (Join-Path $root 'CargoLoad\CargoLoad.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:AssemblyName=CargoLoader `
    -p:Version=$Version `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $pkg `
    --source https://api.nuget.org/v3/index.json `
    --nologo

if (-not (Test-Path (Join-Path $pkg $exeName))) { throw "Не найден $exeName после публикации" }

# --- 2. Подпись exe ---

if (-not $SkipSign) {
    Write-Host "[2/4] Подпись приложения..."
    $cert = Get-SigningCert
    Sign-File (Join-Path $pkg $exeName) $cert
}
else {
    Write-Host "[2/4] Подпись пропущена (-SkipSign)"
}

# --- 3. Компиляция установщика ---

Write-Host "[3/4] Сборка установщика..."
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $iscc "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC вернул код $LASTEXITCODE" }

$setup = Get-ChildItem $outDir -Filter '*.exe' | Select-Object -First 1
if (-not $setup) { throw "Установщик не создан" }

# --- 4. Подпись установщика ---

if (-not $SkipSign) {
    Write-Host "[4/4] Подпись установщика..."
    $cert = Get-SigningCert
    Sign-File $setup.FullName $cert
}
else {
    Write-Host "[4/4] Подпись установщика пропущена"
}

Write-Host ""
Write-Host "Готово: $($setup.FullName)"
Write-Host ("Размер: {0:N1} МБ" -f ($setup.Length / 1MB))