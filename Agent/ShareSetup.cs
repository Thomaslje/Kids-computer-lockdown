using KioskShared;

namespace KioskAgent;

/// <summary>Creates the "Share" folder and shares it without a password (everyone on the network can read and write).</summary>
static class ShareSetup
{
    public static OptionsResult Apply()
    {
        FileTransfer.EnsureShareFolder();
        var notes = new List<string>();
        var o = Proc.Ps(@"
$ErrorActionPreference = 'Continue'
$out = @()
$everyone = ([Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([Security.Principal.NTAccount]).Value
try {
    if (Get-SmbShare -Name Share -ErrorAction SilentlyContinue) { Remove-SmbShare -Name Share -Force }
    New-SmbShare -Name Share -Path 'C:\Share' -FullAccess $everyone | Out-Null
} catch { $out += 'Could not create the share: ' + $_.Exception.Message }

# passwordless sharing = what 'turn off password protected sharing' does
$lsa = 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa'
Set-ItemProperty $lsa everyoneincludesanonymous 1 -Type DWord
$srv = 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters'
Set-ItemProperty $srv restrictnullsessaccess 0 -Type DWord
$ns = @((Get-ItemProperty $srv).NullSessionShares) | Where-Object { $_ }
if ($ns -notcontains 'Share') { Set-ItemProperty $srv NullSessionShares ($ns + 'Share') -Type MultiString }
$wk = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
if (-not (Test-Path $wk)) { New-Item $wk -Force | Out-Null }
Set-ItemProperty $wk AllowInsecureGuestAuth 1 -Type DWord

try {
    $g = Get-LocalUser | Where-Object { $_.SID.Value -like '*-501' }
    if ($g) {
        Enable-LocalUser -SID $g.SID
        $cfg = Join-Path $env:TEMP 'kiosk-sec.inf'; $db = Join-Path $env:TEMP 'kiosk-sec.sdb'
        secedit /export /cfg $cfg /areas USER_RIGHTS | Out-Null
        $new = foreach ($l in (Get-Content $cfg)) {
            if ($l -match '^SeDenyNetworkLogonRight\s*=\s*(.*)$') {
                $keep = $Matches[1] -split ',' | ForEach-Object { $_.Trim() } |
                    Where-Object { $_ -and $_ -notmatch '^\*?S-1-5-.*-501$' -and $_ -ne $g.Name }
                'SeDenyNetworkLogonRight = ' + ($keep -join ',')
            } else { $l }
        }
        Set-Content $cfg $new -Encoding Unicode
        secedit /configure /db $db /cfg $cfg /areas USER_RIGHTS /quiet | Out-Null
        Remove-Item $cfg, $db -Force -ErrorAction SilentlyContinue
    }
} catch { $out += 'Guest access: ' + $_.Exception.Message }
$out -join ' '
");
        if (o.Trim().Length > 0) notes.Add(o.Trim());
        notes.Insert(0, @"C:\Share is shared as ""Share"" (no password).");
        return new OptionsResult(false, notes);
    }
}
