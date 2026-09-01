Set-StrictMode -Version 2.0

function Get-KrsMSBuildPath {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return [IO.Path]::GetFullPath($command.Source)
    }

    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $found = & $vswhere -latest -products * `
            -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if ($found -and (Test-Path -LiteralPath $found -PathType Leaf)) {
            return [IO.Path]::GetFullPath($found)
        }
    }

    throw "MSBuild was not found through PATH or vswhere. Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack."
}

function Get-KrsNetFrameworkCscPath {
    $windowsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    $candidate = Join-Path $windowsRoot "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "The system .NET Framework x64 compiler was not found: $candidate"
    }
    return [IO.Path]::GetFullPath($candidate)
}
