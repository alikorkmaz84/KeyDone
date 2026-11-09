# KeyDone'un kurulum dosyasını ve taşınabilir sürümünü üretir.
# Kullanım:  powershell -ExecutionPolicy Bypass -File paketle.ps1 [-Version 0.4.0]
# Çıktı:     artifacts\dagitim\KeyDone-<sürüm>-Kurulum.exe ve KeyDone-<sürüm>-Tasinabilir.zip
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\KeyDone\KeyDone.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw 'Sürüm bulunamadı.' }

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'yayin'
$output = Join-Path $artifacts 'dagitim'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Force $output | Out-Null

Write-Host "KeyDone $Version derleniyor..."
# Kendi içinde .NET barındırır: kullanıcının ayrıca .NET kurması gerekmez.
& $dotnet publish $project -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $publish -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Get-ChildItem $publish -Filter *.pdb | Remove-Item

Write-Host 'Taşınabilir sürüm hazırlanıyor...'
$portable = Join-Path $artifacts 'KeyDone'
Copy-Item $publish $portable -Recurse
@"
Bu dosya KeyDone'un taşınabilir kipte çalışmasını sağlar.
Ayarlar ve sesler bu klasördeki "Veriler" klasöründe tutulur; klasörü USB belleğe kopyalayıp başka bilgisayarda kullanabilirsiniz.
Kasanızı da bu klasöre kaydederseniz sürücü harfi değişse bile KeyDone onu bulur.
Bu dosyayı silerseniz KeyDone ayarlarını yeniden Windows kullanıcı klasöründe tutar.
"@ | Set-Content -Encoding UTF8 (Join-Path $portable 'tasinabilir.txt')
Compress-Archive -Path $portable -DestinationPath (Join-Path $output "KeyDone-$Version-Tasinabilir.zip")

Write-Host 'Kurulum dosyası hazırlanıyor...'
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) bulunamadı. https://jrsoftware.org/isdl.php adresinden kurun.' }
& $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$output" (Join-Path $root 'installer\KeyDone.iss')
if ($LASTEXITCODE -ne 0) { throw 'Kurulum dosyası üretilemedi.' }

Write-Host ''
Write-Host 'Hazır:'
Get-ChildItem $output | ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB) }
