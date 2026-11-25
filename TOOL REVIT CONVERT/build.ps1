$ErrorActionPreference = "Stop"
$env:Path += ";C:\Program Files\dotnet"
$src = "https://api.nuget.org/v3/index.json"
dotnet build "$PSScriptRoot\src\RvtConvert.Addin\RvtConvert.Addin.csproj" -c Release --source $src /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Addin build failed" }
dotnet build "$PSScriptRoot\src\RvtConvert.App\RvtConvert.App.csproj" -c Release --source $src /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "App build failed" }
$exe = "$PSScriptRoot\src\RvtConvert.App\bin\Release\net48\BIMLab Revit Convert.exe"
New-Item -ItemType Directory -Force "$PSScriptRoot\RELEASE" | Out-Null
Copy-Item $exe "$PSScriptRoot\RELEASE\BIMLab Revit Convert.exe" -Force
Get-ChildItem "$PSScriptRoot\RELEASE" | Select Name,Length

