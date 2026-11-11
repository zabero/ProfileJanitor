# Signs ProfileJanitor.exe (run after every build - rebuilding removes the signature).
#   -Thumbprint <hash>  use an existing code-signing certificate (domain CA or commercial) - the proper way.
#   without it          creates/reuses a SELF-SIGNED certificate "ProfileJanitor Code Signing" and exports its public
#                       part to ProfileJanitor-signing.cer. Other computers trust the exe only after that .cer is
#                       installed into Trusted Root + Trusted Publishers (e.g. by GPO). SmartScreen ignores self-signed.
param([string]$File = "$PSScriptRoot\ProfileJanitor.exe", [string]$Thumbprint)
$ErrorActionPreference = 'Stop'
if ($Thumbprint) {
    $cert = Get-ChildItem Cert:\LocalMachine\My, Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
} else {
    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert | Where-Object Subject -eq 'CN=ProfileJanitor Code Signing' | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) { $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=ProfileJanitor Code Signing' -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(5) }
    Export-Certificate -Cert $cert -FilePath "$PSScriptRoot\ProfileJanitor-signing.cer" | Out-Null
}
if (-not $cert) { throw 'Certificate not found.' }
# timestamp keeps the signature valid after the certificate expires; needs internet access
$r = Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
'{0}: {1} - signer {2}, thumbprint {3}' -f $File, $r.Status, $cert.Subject, $cert.Thumbprint
