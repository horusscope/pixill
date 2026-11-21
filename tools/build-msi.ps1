# Builds dist\Pixill-<version>.msi from installer\Pixill.wxs, and copies the standalone exe beside it
# with a SHA256SUMS.txt for both.
#
# Needs the WiX Toolset 3 command-line tools (candle.exe, light.exe). If they are not installed, the
# official binaries are downloaded once from the WiX GitHub releases into .wix\ (checked against a pinned
# SHA-256) and used from there. Nothing is installed on the machine.
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The version lives in one place, src\AssemblyInfo.cs, so the exe and the installer cannot disagree.
if (-not $Version) { $Version = (Select-String -Path "$root\src\AssemblyInfo.cs" -Pattern 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Matches[0].Groups[1].Value }

& "$root\build.ps1" | Out-Host

$wix = if ($env:WIX -and (Test-Path "$env:WIX\bin\candle.exe")) { "$env:WIX\bin" } else { "$root\.wix" }
if (-not (Test-Path "$wix\candle.exe")) {
    $url = 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip'
    $sha = '6AC824E1642D6F7277D0ED7EA09411A508F6116BA6FAE0AA5F2C7DAA2FF43D31'
    New-Item -ItemType Directory -Force $wix | Out-Null
    $zip = "$wix\wix314-binaries.zip"
    "downloading WiX Toolset 3.14.1 ..."
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing $url -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $sha) { Remove-Item $zip; throw "WiX download does not match the expected SHA-256" }
    Expand-Archive $zip -DestinationPath $wix -Force
    Remove-Item $zip
}

New-Item -ItemType Directory -Force "$root\obj", "$root\dist" | Out-Null
$msi = "$root\dist\Pixill-$Version.msi"
& "$wix\candle.exe" -nologo -arch x64 -ext WixUtilExtension "-dVersion=$Version" "-dRoot=$root" -out "$root\obj\Pixill.wixobj" "$root\installer\Pixill.wxs"
if ($LASTEXITCODE -ne 0) { throw "candle failed" }
& "$wix\light.exe" -nologo -ext WixUtilExtension -spdb -out $msi "$root\obj\Pixill.wixobj"
if ($LASTEXITCODE -ne 0) { throw "light failed" }

Copy-Item "$root\bin\Pixill.exe" "$root\dist\Pixill.exe" -Force
$lines = foreach ($f in "Pixill.exe", "Pixill-$Version.msi") { (Get-FileHash "$root\dist\$f" -Algorithm SHA256).Hash.ToLower() + "  " + $f }
[IO.File]::WriteAllText("$root\dist\SHA256SUMS.txt", ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
"built:"; Get-ChildItem "$root\dist" | ForEach-Object { "  {0,9:N0}  {1}" -f $_.Length, $_.Name }
