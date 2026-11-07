param(
    [string]$File = "$PSScriptRoot\dist\CargoLoader.exe",
    [switch]$TrustLocal,
    [string]$Subject = "CN=CargoLoad Self-Signed Code Signing"
)

# Подпись exe, чтобы Windows (SmartScreen) не ругался на «неизвестного издателя».
#
# ВАЖНО: самоподписанный сертификат убирает предупреждение только на машинах,
# где этот сертификат добавлен в доверенные. Для распространения на чужие ПК
# нужен обычный сертификат подписи кода (покупной или корпоративный).

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $File)) { throw "Файл не найден: $File" }

# --- 1. Сертификат ---
$cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Select-Object -First 1

if ($cert) {
    Write-Host "Использую существующий сертификат: $($cert.Thumbprint)"
}
else {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "Создан самоподписанный сертификат: $($cert.Thumbprint)"
}

# --- 2. Экспорт .cer для установки на других машинах ---
$cerPath = Join-Path $PSScriptRoot 'cargoload-selfsigned.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Сертификат экспортирован: $cerPath"

# --- 3. Доверие на этой машине ---
if ($TrustLocal) {
    $already = Get-ChildItem Cert:\CurrentUser\Root -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $already) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
        Write-Host "Сертификат добавлен в доверенные (CurrentUser\Root)"
    }
    else {
        Write-Host "Сертификат уже в доверенных"
    }
}

# --- 4. signtool ---
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    throw "signtool.exe не найден. Нужен Windows SDK (winget install Microsoft.WindowsSDK)."
}

Write-Host "signtool: $($signtool.FullName)"
Write-Host "Подписываю: $File"

& $signtool.FullName sign /sha1 $cert.Thumbprint /fd SHA256 $File
if ($LASTEXITCODE -ne 0) { throw "signtool вернул код $LASTEXITCODE" }

$sig = Get-AuthenticodeSignature -FilePath $File
Write-Host ""
Write-Host "Статус подписи: $($sig.Status)"
Write-Host "Издатель: $($sig.SignerCertificate.Subject)"
Write-Host ""
Write-Host "Чтобы убрать предупреждение на другом ПК:"
Write-Host "  1) скопируйте $cerPath на этот ПК"
Write-Host "  2) откройте файл сертификата -> 'Установить сертификат' -> 'Доверенные издатели' и 'Доверенные корневые центры сертификации'"
Write-Host "  3) либо выполните: Import-Certificate -FilePath <путь к .cer> -CertStoreLocation Cert:\CurrentUser\Root"