<#
.SYNOPSIS
  Builds, stages, zips, optionally compiles the Inno Setup installer (.exe) for
  ONE Revit year, and optionally installs the Node.aec Connector Revit add-in.

.DESCRIPTION
  One year per invocation (-RevitYear, default 2026, supported 2023..2027).
  The Setup is named NodeAec.Connector-<version>-R<year>-Setup.exe and installs
  only into %ProgramData%\Autodesk\Revit\Addins\<year>\, so each Revit year has
  an independently downloadable installer that coexists with the others.

.NOTES
  Setup.exe generation requires Inno Setup 6 (ISCC.exe on PATH-adjacent
  standard location). Without it, only the .zip is produced - no failure.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.2 -RevitYear 2026
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.2 -RevitYear 2026 -Install
#>
param(
  [string]$Version = "0.1.2",
  [string]$RevitYear = "2026",
  [string]$Configuration = "Release",
  [switch]$Install,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# -File nao vincula "-Param=valor": o token sobra em $args e o script seguiria
# com os defaults. Falhar alto para qualquer sobra e exigir a sintaxe correta:
# -RevitYear 2023. (Com aspas o token vira o primeiro posicional e e barrado
# pela validação de $Version logo abaixo.)
if ($args.Count -gt 0) {
  throw "Argumentos não reconhecidos: $($args -join ' '). Use -RevitYear <ano> (ex.: -RevitYear 2023)."
}

# Um token posicional inesperado — ex.: "-RevitYear=2023" passado entre aspas em
# vez de -RevitYear 2023 — cai no primeiro parâmetro ($Version) e seguiria para
# o build sem aviso. Validar o formato falha alto antes de qualquer trabalho.
if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}([.-][0-9A-Za-z]+)*$') {
  throw "Versão inválida: '$Version'. Use o formato SemVer (ex.: 0.1.2 ou 0.1.2-rc1) e passe o ano com -RevitYear <ano>."
}

# Um AppId determinístico por ano do Revit: identidade de instalação distinta
# permite que anos diferentes coexistam e sejam desinstalados de forma
# independente em Aplicativos. O installer.iss escapa as chaves e deriva a
# chave de desinstalação do mesmo /DAppId.
$RevitYearAppIds = @{
  "2023" = "{B0438DCE-26F6-42C9-BDC2-710A6D0C1F0E}"
  "2024" = "{4056654E-C440-4A44-9A5D-DEAA238858FA}"
  "2025" = "{63CC4A1D-1937-4BD0-A8A4-C05D36AA20DB}"
  "2026" = "{5A4626FE-9563-4ED3-9C66-EA4567152F01}"
  "2027" = "{C105DE2F-78B0-467C-A1A4-02F9C86BC6BA}"
}
if (-not $RevitYearAppIds.ContainsKey($RevitYear)) {
  throw "RevitYear '$RevitYear' não suportado: use 2023, 2024, 2025, 2026 ou 2027."
}
$AppId = $RevitYearAppIds[$RevitYear]

# O script vive em <raiz do repositório>\scripts: a raiz do repositório É a raiz
# do add-in (docs/, src/, tests/ e release/ ficam nela).
$ConnectorRoot = Split-Path $PSScriptRoot -Parent
$RepoRoot = $ConnectorRoot
$Sln = Join-Path $ConnectorRoot "NodeAec.Connector.sln"
$Project = Join-Path $ConnectorRoot "src\NodeAec.Connector\NodeAec.Connector.csproj"
$DllName = "NodeAec.Connector.dll"
$AddinTemplate = Join-Path $ConnectorRoot "src\NodeAec.Connector\NodeAec.Connector.addin"
$ReleaseDir = Join-Path $ConnectorRoot "release"
$StageDir = Join-Path $ReleaseDir "stage\NodeAec.Connector"
$ZipPath = Join-Path $ReleaseDir "NodeAec.Connector-$Version-R$RevitYear.zip"

# Resolve o TFM lendo Directory.Build.props via MSBuild, em vez de repetir a matriz de
# anos aqui — bin\<ano>\<config>\<tfm> é a única fonte de verdade e muda com RevitYear.
$TargetFramework = (& dotnet msbuild $Project -getProperty:TargetFramework -p:RevitYear=$RevitYear -nologo -v:quiet |
  Select-Object -Last 1).ToString().Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($TargetFramework)) {
  throw "Não foi possível resolver TargetFramework para RevitYear=$RevitYear (exit $LASTEXITCODE)."
}
$OutDir = Join-Path $ConnectorRoot "src\NodeAec.Connector\bin\$RevitYear\$Configuration\$TargetFramework"

if (-not $SkipBuild) {
  Write-Host "==> dotnet build $Sln -c $Configuration -p:RevitYear=$RevitYear ($TargetFramework)"
  & dotnet build $Sln -c $Configuration -p:RevitYear=$RevitYear
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

$dll = Join-Path $OutDir $DllName
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

# Stage: clean + copy runtime payload
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item $StageDir -ItemType Directory -Force | Out-Null
Get-ChildItem $OutDir -Filter *.dll |
  Where-Object { $_.Name -notlike "RevitAPI*" -and $_.Name -ne "AdWindows.dll" -and $_.Name -notlike "UIFramework*" } |
  Copy-Item -Destination $StageDir -Force

# Stage resource icons
if (Test-Path (Join-Path $OutDir "Resources")) {
  Copy-Item (Join-Path $OutDir "Resources") $StageDir -Recurse -Force
}
Get-ChildItem $OutDir -Filter *.png -ErrorAction SilentlyContinue |
  Copy-Item -Destination $StageDir -Force

# System.Security.Cryptography.ProtectedData tem destino diferente por família de runtime:
#  * net48 (Revit 2023/2024): vem de pacote NuGet e DEVE ser copiada para o add-in;
#  * net8.0-windows/net10.0-windows (Revit 2025+): o assembly faz parte do runtime
#    Microsoft.WindowsDesktop.App do host, é framework-provided e por isso nem aparece
#    no diretório de saída (copiá-lo seria redundante).
if ($TargetFramework -eq "net48") {
  $dpapiDll = Join-Path $StageDir "System.Security.Cryptography.ProtectedData.dll"
  if (-not (Test-Path $dpapiDll)) {
    throw "Missing DPAPI dependency in build output: System.Security.Cryptography.ProtectedData.dll was not copied to $OutDir."
  }
}
if (Test-Path (Join-Path $RepoRoot "README.md")) {
  Copy-Item (Join-Path $RepoRoot "README.md") (Join-Path $StageDir "README.md") -Force
}

# Stage the .addin with the absolute path for THIS year
$installDir = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear\NodeAec.Connector"
[xml]$addin = Get-Content $AddinTemplate
$addin.RevitAddIns.AddIn.Assembly = "$installDir\$DllName"
$addin.Save((Join-Path $StageDir "NodeAec.Connector.addin"))

# Zip + checksum
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipPath -Force
$hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $ZipPath -Leaf)" | Out-File "$ZipPath.sha256" -Encoding ascii
Write-Host "==> release: $ZipPath"
Write-Host "    sha256: $hash"

# Optional: Inno Setup .exe installer for THIS Revit year (double-click friendly
# for end users). Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). When
# ISCC.exe is not found the .zip above remains the only artifact - no failure.
# The per-year AppId keeps different years side by side and independently
# uninstallable in Windows Settings > Apps.
$setupName = "NodeAec.Connector-$Version-R$RevitYear-Setup.exe"
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
  $versionNum = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }
  $iss = Join-Path $PSScriptRoot "installer.iss"
  Write-Host "==> ISCC $iss (Revit $RevitYear, AppId $AppId)"
  & $iscc "/DAppVersion=$Version" "/DAppVersionNum=$versionNum" "/DRevitYear=$RevitYear" "/DAppId=$AppId" "/DPayloadStage=$StageDir" "/O$ReleaseDir" $iss
  if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

  $setupPath = Join-Path $ReleaseDir $setupName
  if (-not (Test-Path $setupPath)) { throw "Setup output not found: $setupPath" }
  $setupHash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
  "$setupHash  $setupName" | Out-File "$setupPath.sha256" -Encoding ascii
  Write-Host "==> setup: $setupPath"
  Write-Host "    sha256: $setupHash"
}
else {
  Write-Warning "Inno Setup 6 (ISCC.exe) not found - only the .zip was generated. Install from https://jrsoftware.org/isdl.php to also build $setupName."
}

if ($Install) {
  $addinsDir = "$env:ProgramData\Autodesk\Revit\Addins\$RevitYear"
  $targetDir = Join-Path $addinsDir "NodeAec.Connector"
  Write-Host "==> install to $targetDir"
  New-Item $targetDir -ItemType Directory -Force | Out-Null
  Copy-Item "$StageDir\*.dll" $targetDir -Force
  if (Test-Path (Join-Path $StageDir "Resources")) {
    Copy-Item (Join-Path $StageDir "Resources") $targetDir -Recurse -Force
  }
  Get-ChildItem $StageDir -Filter *.png -ErrorAction SilentlyContinue |
    Copy-Item -Destination $targetDir -Force
  Copy-Item (Join-Path $StageDir "NodeAec.Connector.addin") $addinsDir -Force
  Write-Host "==> installed Node.aec Connector. Restart Revit $RevitYear."
}
