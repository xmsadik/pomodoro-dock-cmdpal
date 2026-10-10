<#
.SYNOPSIS
    Exports the Pomodoro Dock code-signing certificate (with private key) to a password-protected .pfx.
.DESCRIPTION
    Prompts for the password so it never appears in the command line, shell history or logs.
    Keep the password somewhere other than the .pfx (e.g. a password manager).
    Restore on a new machine with:
        Import-PfxCertificate -FilePath <file.pfx> -CertStoreLocation Cert:\CurrentUser\My -Password (Read-Host -AsSecureString)
    pack.ps1 -Sign then finds and reuses the restored CN=PomodoroDockDev certificate.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Destination,
    [string] $Thumbprint
)

$ErrorActionPreference = 'Stop'
$cert = if ($Thumbprint) { Get-Item "Cert:\CurrentUser\My\$Thumbprint" }
        else { Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=PomodoroDockDev' -and $_.HasPrivateKey } | Select-Object -First 1 }
if (-not $cert) { throw 'No CN=PomodoroDockDev certificate found. Run scripts\pack.ps1 -Sign first.' }
$Thumbprint = $cert.Thumbprint
if (-not $cert.HasPrivateKey) { throw "Certificate $Thumbprint has no private key in this store." }

$password = Read-Host -AsSecureString 'PFX password (min. 12 characters)'
$confirm = Read-Host -AsSecureString 'Repeat password'
$plain = [Net.NetworkCredential]::new('', $password).Password
if ($plain -ne [Net.NetworkCredential]::new('', $confirm).Password) { throw 'Passwords do not match.' }
if ($plain.Length -lt 12) { throw 'Password must be at least 12 characters.' }

if (Test-Path $Destination -PathType Container) { $Destination = Join-Path $Destination 'PomodoroDock-signing.pfx' }
Export-PfxCertificate -Cert $cert -FilePath $Destination -Password $password -CryptoAlgorithmOption AES256_SHA256 | Out-Null
Write-Host "Exported to $Destination"
