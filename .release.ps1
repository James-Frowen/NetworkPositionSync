# PowerShell version of .release.sh for Windows CI

Write-Host "Running release script with [SOURCE_PATH=$env:SOURCE_PATH, TARGET_PATH=$env:TARGET_PATH, args=$args]"

# Extract version (remove -xxx suffix)
$VER = $args[0] -replace "-[a-z]*", ""

# Update AssemblyVersion in AssemblyInfo.cs file
$assemblyInfoPath = Join-Path $env:SOURCE_PATH "Runtime\AssemblyInfo.cs"
(Get-Content $assemblyInfoPath) `
| ForEach-Object { $_ -replace 'AssemblyVersion\(".*"\)', "AssemblyVersion(`"$VER`")" } `
| Set-Content $assemblyInfoPath

# unity-packer equivalent command
$unityPackerArgs = @(
    "pack", "NetworkPositionSync.unitypackage",
    "${env:SOURCE_PATH}\Runtime", "${env:TARGET_PATH}\Runtime",
    "${env:SOURCE_PATH}\CHANGELOG.md", "${env:TARGET_PATH}\CHANGELOG.md",
    "${env:SOURCE_PATH}\LICENSE", "${env:TARGET_PATH}\LICENSE",
    "${env:SOURCE_PATH}\package.json", "${env:TARGET_PATH}\package.json",
    "${env:SOURCE_PATH}\Readme.txt", "${env:TARGET_PATH}\Readme.txt"
)
unity-packer @unityPackerArgs
