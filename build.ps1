# Build both .exe files from source using Windows PowerShell 5.1.
# Run: powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1"
# No administrator privileges are required for compilation.

#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run build.ps1 with Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 (pwsh.exe).'
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configSource = Join-Path $root 'EnvConfig.cs'
$sshSource = Join-Path $root 'SshShim.cs'
$askpassSource = Join-Path $root 'CodexAskpass.cs'
$sshExe = Join-Path $root 'ssh.exe'
$askpassExe = Join-Path $root 'codex-askpass.exe'
$sshTemp = Join-Path $root 'ssh.build.exe'
$askpassTemp = Join-Path $root 'codex-askpass.build.exe'

foreach ($source in @($configSource, $sshSource, $askpassSource)) {
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing source file: $source"
    }
}

try {
    Remove-Item -LiteralPath $sshTemp, $askpassTemp -Force -ErrorAction SilentlyContinue

    # EnvConfig has no using directives, so append it after each program's
    # source. Each executable includes the shared .env reader independently.
    $configCode = Get-Content -LiteralPath $configSource -Raw
    $sshCode = (Get-Content -LiteralPath $sshSource -Raw) + [Environment]::NewLine + $configCode
    $askpassCode = (Get-Content -LiteralPath $askpassSource -Raw) + [Environment]::NewLine + $configCode

    Add-Type -TypeDefinition $sshCode `
        -Language CSharp -OutputAssembly $sshTemp -OutputType ConsoleApplication -ErrorAction Stop

    Add-Type -TypeDefinition $askpassCode `
        -Language CSharp -OutputAssembly $askpassTemp -OutputType ConsoleApplication -ErrorAction Stop

    if (!(Test-Path -LiteralPath $sshTemp -PathType Leaf) -or
        !(Test-Path -LiteralPath $askpassTemp -PathType Leaf)) {
        throw 'Compilation did not produce both executables.'
    }

    # Replace only after BOTH sources have compiled successfully.
    Move-Item -LiteralPath $sshTemp -Destination $sshExe -Force
    Move-Item -LiteralPath $askpassTemp -Destination $askpassExe -Force

    Write-Host 'Built:' -ForegroundColor Green
    Get-Item -LiteralPath $sshExe, $askpassExe |
        Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
}
finally {
    Remove-Item -LiteralPath $sshTemp, $askpassTemp -Force -ErrorAction SilentlyContinue
}
