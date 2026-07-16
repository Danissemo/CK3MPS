$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Path = Join-Path $Root 'source\Workflow.cs'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$text = ([IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")

$old = @'
            IPAddress bindAddress;
            string advertisedAddress = DetectPrimaryIpv4Address();
            if (!IPAddress.TryParse(advertisedAddress, out bindAddress)
                || bindAddress.AddressFamily != AddressFamily.InterNetwork)
                bindAddress = IPAddress.Loopback;
            TcpListener listener = new TcpListener(bindAddress, 0);
            listener.Start();
'@.Replace("`r`n", "`n")

$new = @'
            TcpListener listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
'@.Replace("`r`n", "`n")

if (-not $text.Contains('new TcpListener(IPAddress.Any, 0)')) {
    $index = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw 'Expected primary-interface parity listener block was not found.'
    }
    $text = $text.Substring(0, $index) + $new + $text.Substring($index + $old.Length)
    [IO.File]::WriteAllText($Path, $text, $Utf8NoBom)
}

if (-not $text.Contains('Text = DetectPrimaryIpv4Address()')) {
    throw 'Parity join dialog no longer advertises the primary LAN IPv4 address.'
}

Write-Host 'LAN parity listener now accepts both LAN and loopback clients.' -ForegroundColor Green
