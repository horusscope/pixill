# Builds bin\Pixill.exe.
#
# Nothing needs installing: the C# compiler that ships inside Windows (.NET Framework 4.x) is enough, and the
# program targets the .NET Framework that is already part of Windows 10 and 11. If Visual Studio Build Tools
# are present their newer compiler is used instead; the result is the same.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$roslyn = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
$builtin = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$csc = if (Test-Path $roslyn) { $roslyn } elseif (Test-Path $builtin) { $builtin } else { throw "No C# compiler found (looked for $builtin)" }

New-Item -ItemType Directory -Force "$root\bin", "$root\assets" | Out-Null
$exe = "$root\bin\Pixill.exe"
$ico = "$root\assets\app.ico"

function Compile {
    $iconArg = @()
    if (Test-Path $ico) { $iconArg = @("/win32icon:$ico") }
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /warn:4 `
        "/out:$exe" @iconArg "/resource:$root\fonts\Jersey15-Regular.ttf,Pixill.Jersey15.ttf" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /r:System.Net.Http.dll /r:System.Web.Extensions.dll `
        "$root\src\*.cs"
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

Compile
# The logo is defined in src\AppIcon.cs. Have the fresh exe write it out as an .ico, and if that changed
# (or did not exist yet), compile once more so the exe carries the current icon.
$before = if (Test-Path $ico) { (Get-FileHash $ico).Hash } else { '' }
Start-Process $exe -ArgumentList '--write-icon', "`"$ico`"" -Wait
if ((Get-FileHash $ico).Hash -ne $before) { Compile }

Copy-Item "$root\fonts" "$root\bin" -Recurse -Force
"built $exe"
