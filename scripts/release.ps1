<#
.SYNOPSIS
  Builds, stages, zips, optionally compiles the Inno Setup installer (.exe) for
  ONE Revit compatibility group, and optionally installs the Node.aec Connector
  Revit add-in.

.DESCRIPTION
  One compatibility group per invocation (-RevitYear, default "2025-2026";
  groups 2023-2024 / 2025-2026 / 2027, and a single year 2023..2027 is accepted
  as an alias for its group). The Setup is named
  NodeAec.Connector-<version>-R<group>-Setup.exe and installs into
  %ProgramData%\Autodesk\Revit\Addins\<year>\ for EVERY year of the group that
  is installed on the machine, so one Setup covers the whole group and coexists
  with the other groups.

  The payload is always compiled with the group's floor year (2023, 2025 or
  2027): compiling against the oldest API of the group is what makes a single
  DLL load on every year of the group. Passing a non-floor year of a group
  resolves to that group and logs a notice.

.NOTES
  Setup.exe generation requires Inno Setup 6 (ISCC.exe on PATH-adjacent
  standard location). Without it, only the .zip is produced - no failure.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.2 -RevitYear 2025-2026
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.2 -RevitYear 2025-2026 -Install
#>
param(
  [string]$Version = "0.1.2",
  [string]$RevitYear = "2025-2026",
  [string]$Configuration = "Release",
  [switch]$Install,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# -File nao vincula "-Param=valor": o token sobra em $args e o script seguiria
# com os defaults. Falhar alto para qualquer sobra e exigir a sintaxe correta:
# -RevitYear 2025-2026. (Com aspas o token vira o primeiro posicional e e
# barrado pela validação de $Version logo abaixo.)
if ($args.Count -gt 0) {
  throw "Argumentos não reconhecidos: $($args -join ' '). Use -RevitYear <grupo> (ex.: -RevitYear 2023-2024)."
}

# Um token posicional inesperado — ex.: "-RevitYear=2026" passado entre aspas em
# vez de -RevitYear 2025-2026 — cai no primeiro parâmetro ($Version) e seguiria
# para o build sem aviso. Validar o formato falha alto antes de qualquer trabalho.
if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}([.-][0-9A-Za-z]+)*$') {
  throw "Versão inválida: '$Version'. Use o formato SemVer (ex.: 0.1.2 ou 0.1.2-rc1) e passe o grupo com -RevitYear <grupo>."
}

# Um instalador por grupo de compatibilidade do Revit. O grupo define os anos
# que o Setup cobre; o payload é sempre compilado com o ano-base do grupo (o
# menor ano, de API mais antiga), o que permite uma unica DLL carregar em todos
# os anos do grupo.
$RevitYearGroups = [ordered]@{
  "2023-2024" = @("2023","2024")
  "2025-2026" = @("2025","2026")
  "2027"      = @("2027")
}
# Anos avulsos continuam aceitos e resolvem para o grupo do ano.
$RevitYearToGroup = @{
  "2023" = "2023-2024"; "2024" = "2023-2024"
  "2025" = "2025-2026"; "2026" = "2025-2026"
  "2027" = "2027"
}
if ($RevitYearGroups.Contains($RevitYear)) {
  $Group = $RevitYear
}
elseif ($RevitYearToGroup.ContainsKey($RevitYear)) {
  $Group = $RevitYearToGroup[$RevitYear]
  Write-Warning "RevitYear '$RevitYear' pertence ao grupo '$Group': compilando com o ano-base $($RevitYearGroups[$Group][0]) para que uma unica DLL carregue em todos os anos do grupo."
}
else {
  throw "RevitYear '$RevitYear' não suportado: use um grupo (2023-2024, 2025-2026, 2027) ou um ano (2023, 2024, 2025, 2026 ou 2027)."
}
$RevitYears = $RevitYearGroups[$Group]
$BuildYear = $RevitYears[0]

# Um AppId determinístico por ano do Revit. O Setup do grupo usa o AppId do
# ano-base como identidade (assim um setup novo faz upgrade por cima das
# instalações 0.1.1 desse ano) e recebe, via /DPreviousAppIds, os AppIds de
# TODOS os anos do grupo: o installer.iss detecta e desinstala instalações
# anteriores feitas por ano (2024-only, 2026-only, ...) e o desinstalador
# remove o payload de todos os anos do grupo.
$RevitYearAppIds = @{
  "2023" = "{B0438DCE-26F6-42C9-BDC2-710A6D0C1F0E}"
  "2024" = "{4056654E-C440-4A44-9A5D-DEAA238858FA}"
  "2025" = "{63CC4A1D-1937-4BD0-A8A4-C05D36AA20DB}"
  "2026" = "{5A4626FE-9563-4ED3-9C66-EA4567152F01}"
  "2027" = "{C105DE2F-78B0-467C-A1A4-02F9C86BC6BA}"
}
$AppId = $RevitYearAppIds[$BuildYear]
$PreviousAppIds = ($RevitYears | ForEach-Object { $RevitYearAppIds[$_] }) -join ';'

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
$ZipPath = Join-Path $ReleaseDir "NodeAec.Connector-$Version-R$Group.zip"

# Resolve o TFM lendo Directory.Build.props via MSBuild, em vez de repetir a matriz de
# anos aqui — bin\<ano>\<config>\<tfm> é a única fonte de verdade e muda com RevitYear.
$TargetFramework = (& dotnet msbuild $Project -getProperty:TargetFramework -p:RevitYear=$BuildYear -nologo -v:quiet |
  Select-Object -Last 1).ToString().Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($TargetFramework)) {
  throw "Não foi possível resolver TargetFramework para RevitYear=$BuildYear (exit $LASTEXITCODE)."
}
$OutDir = Join-Path $ConnectorRoot "src\NodeAec.Connector\bin\$BuildYear\$Configuration\$TargetFramework"

if (-not $SkipBuild) {
  Write-Host "==> dotnet build $Sln -c $Configuration -p:RevitYear=$BuildYear ($TargetFramework)"
  & dotnet build $Sln -c $Configuration -p:RevitYear=$BuildYear
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

# Stage the .addin with the absolute path for the BUILD year. This staged copy
# only feeds the .zip (the Setup compiles its manifests in [Code], one per
# installed year of the group); keep it aligned with the build year.
$installDir = "C:\ProgramData\Autodesk\Revit\Addins\$BuildYear\NodeAec.Connector"
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

# Optional: Inno Setup .exe installer for THIS Revit group (double-click
# friendly for end users). Requires Inno Setup 6 (https://jrsoftware.org/isdl.php).
# When ISCC.exe is not found the .zip above remains the only artifact - no
# failure. The group's AppId keeps different groups side by side and
# independently uninstallable in Windows Settings > Apps.
$setupName = "NodeAec.Connector-$Version-R$Group-Setup.exe"
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
  $versionNum = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }
  $iss = Join-Path $PSScriptRoot "installer.iss"
  Write-Host "==> ISCC $iss (Revit $Group, AppId $AppId)"
  & $iscc "/DAppVersion=$Version" "/DAppVersionNum=$versionNum" "/DGroupLabel=$Group" "/DRevitYears=$($RevitYears -join ',')" "/DAppId=$AppId" "/DPreviousAppIds=$PreviousAppIds" "/DPayloadStage=$StageDir" "/O$ReleaseDir" $iss
  if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

  $setupPath = Join-Path $ReleaseDir $setupName
  if (-not (Test-Path $setupPath)) { throw "Setup output not found: $setupPath" }
  $setupHash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
  "$setupHash  $setupName" | Out-File "$setupPath.sha256" -Encoding ascii
  Write-Host "==> setup: $setupPath"
  Write-Host "    sha256: $setupHash"

  # Bundle the installer with its checksum: one zip per Setup.exe.
  $setupZip = Join-Path $ReleaseDir ($setupName -replace '\.exe$','.zip')
  if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
  Compress-Archive -Path $setupPath, "$setupPath.sha256" -DestinationPath $setupZip -Force
  $setupZipHash = (Get-FileHash $setupZip -Algorithm SHA256).Hash.ToLowerInvariant()
  "$setupZipHash  $(Split-Path $setupZip -Leaf)" | Out-File "$setupZip.sha256" -Encoding ascii
  Write-Host "==> setup zip: $setupZip"
  Write-Host "    sha256: $setupZipHash"
}
else {
  Write-Warning "Inno Setup 6 (ISCC.exe) not found - only the .zip was generated. Install from https://jrsoftware.org/isdl.php to also build $setupName."
}

# True when Revit Year is installed locally (same detection as installer.iss).
function Test-RevitYearInstalled([string]$Year) {
  foreach ($dir in @("C:\Program Files\Autodesk\Revit $Year", "C:\Program Files\Autodesk\Revit\$Year")) {
    if (Test-Path $dir) { return $true }
  }
  foreach ($hive in @('HKLM:\SOFTWARE\Autodesk\Revit', 'HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit')) {
    if (Test-Path "$hive\$Year") { return $true }
  }
  $loc = (Get-ItemProperty "HKLM:\SOFTWARE\Autodesk\Revit\Autodesk Revit $Year" -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
  return [bool]$loc
}

if ($Install) {
  # Deploy to EVERY year of the group present on this machine, each with its own
  # absolute Assembly path in the .addin manifest (the staged .addin points at
  # the build year and is only used by the .zip).
  $installed = @()
  foreach ($year in $RevitYears) {
    if (-not (Test-RevitYearInstalled $year)) {
      Write-Host "==> Revit $year nao encontrado - pulando"
      continue
    }
    $addinsDir = "$env:ProgramData\Autodesk\Revit\Addins\$year"
    $targetDir = Join-Path $addinsDir "NodeAec.Connector"
    Write-Host "==> install to $targetDir"
    New-Item $targetDir -ItemType Directory -Force | Out-Null
    Copy-Item "$StageDir\*.dll" $targetDir -Force
    if (Test-Path (Join-Path $StageDir "Resources")) {
      Copy-Item (Join-Path $StageDir "Resources") $targetDir -Recurse -Force
    }
    Get-ChildItem $StageDir -Filter *.png -ErrorAction SilentlyContinue |
      Copy-Item -Destination $targetDir -Force
    [xml]$addin = Get-Content $AddinTemplate
    $addin.RevitAddIns.AddIn.Assembly = "C:\ProgramData\Autodesk\Revit\Addins\$year\NodeAec.Connector\$DllName"
    $addin.Save((Join-Path $addinsDir "NodeAec.Connector.addin"))
    $installed += $year
  }
  if ($installed.Count -eq 0) {
    Write-Warning "Nenhum Autodesk Revit do grupo $Group encontrado localmente (anos: $($RevitYears -join ', ')) - nada foi copiado."
  }
  else {
    Write-Host "==> installed Node.aec Connector for Revit $($installed -join ', '). Restart Revit."
  }
}
