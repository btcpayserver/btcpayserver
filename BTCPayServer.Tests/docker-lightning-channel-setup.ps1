$BCMD="./docker-bitcoin-cli.ps1"
$GCMD="./docker-bitcoin-generate.ps1"
$C_LN="./docker-customer-lncli.ps1"
$M_LN="./docker-merchant-lncli.ps1"
$C_CL="./docker-customer-lightning-cli.ps1"
$M_CL="./docker-merchant-lightning-cli.ps1"

function Connect-Node {
    param (
        [string]$cmd,
        [string]$uri,
        [string]$desc
    )
    $connid = Invoke-Expression "$cmd connect $uri" 2>$null
    if ($connid -match "already connected") {
        Write-Host ("{0} {1}" -f "Already Connected", $desc)
    }
    else {
        $connidObj = $connid | ConvertFrom-Json -ErrorAction SilentlyContinue
        $success = if ($connidObj -and $uri -like "$($connidObj.id)*") { "YES" } else { "NO" }
        Write-Host ("{0} {1}" -f $success, $desc)
    }
}

function Get-ChannelCount {
    param (
        [string]$cmd,
        [string]$id
    )
    $count = 0
    if ($cmd -match "lightning-cli") {
        $channels = (Invoke-Expression "$cmd listchannels" 2>$null | ConvertFrom-Json).channels
        $count = ($channels | Where-Object { $_.destination -eq $id -and $_.active -eq $true }).Count
    }
    elseif ($cmd -match "lncli") {
        $channels = (Invoke-Expression "$cmd listchannels" 2>$null | ConvertFrom-Json).channels
        $count = ($channels | Where-Object { $_.remote_pubkey -eq $id -and $_.active -eq $true }).Count
    }
    return $count
}

function New-Channel {
    param (
        [string]$cmd,
        [string]$id,
        [string]$desc,
        [string]$opts = ""
    )
    $count = Get-ChannelCount -cmd $cmd -id $id
    if ($count -eq 0) {
        # Fund onchain wallet
        if ($cmd -match "lightning-cli") {
            $btcaddr = (Invoke-Expression "$cmd newaddr" 2>$null | ConvertFrom-Json).bech32
        }
        elseif ($cmd -match "lncli") {
            $btcaddr = (Invoke-Expression "$cmd newaddress p2wkh" 2>$null | ConvertFrom-Json).address
        }
        Invoke-Expression "$BCMD sendtoaddress $btcaddr 0.615" > $null
        Invoke-Expression "$GCMD 10" > $null
        # Open channel
        if ($cmd -match "lightning-cli") {
            Invoke-Expression "$cmd -k fundchannel id=$id amount=5000000 push_msat=2450000 $opts" > $null
        }
        elseif ($cmd -match "lncli") {
            Invoke-Expression "$cmd openchannel $id 5000000 2450000 $opts" > $null
        }
        Invoke-Expression "$GCMD 20" > $null
        Start-Sleep -Seconds 1
        $count = Get-ChannelCount -cmd $cmd -id $id
    }
    $success = if ($count -gt 0) { "Success" } else { "Failed" }
    Write-Host ("{0} {1}" -f $success, $desc)
}

function Get-Mapped-Port {
    param (
        [string]$service_name
    )
    $container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=$service_name)
    $mapped_port_info = $(docker port $container_id)
    $mapped_port = ($mapped_port_info -split ':')[1]

    return $mapped_port
}

$c_cl_info = (& $C_CL getinfo | ConvertFrom-Json)
$m_cl_info = (& $M_CL getinfo | ConvertFrom-Json)
$mapped_c_cl_port = Get-Mapped-Port -service_name "customer_lightningd"
$mapped_m_cl_port = Get-Mapped-Port -service_name "merchant_lightningd"
$c_cl_uri=($c_cl_info.id + "@localhost:" + $mapped_c_cl_port)
$m_cl_uri=($m_cl_info.id + "@localhost:" + $mapped_m_cl_port)

$c_ln_info = (& $C_LN getinfo | ConvertFrom-Json)
$m_ln_info = (& $M_LN getinfo | ConvertFrom-Json)
$mapped_c_ln_port = Get-Mapped-Port -service_name "customer_lnd"
$mapped_m_ln_port = Get-Mapped-Port -service_name "merchant_lnd"
$m_ln_id=$m_ln_info.identity_pubkey
$c_ln_uri=($c_ln_info.identity_pubkey + "@localhost:" + $mapped_c_ln_port)
$m_ln_uri=($m_ln_info.identity_pubkey + "@localhost:" + $mapped_m_ln_port)

Write-Host "`nNodes`n-----`n"
Write-Host ("Merchant c-lightning: {0}" -f $m_cl_uri)
Write-Host ("Merchant LND:         {0}" -f $m_ln_uri)
Write-Host ("Customer c-lightning: {0}" -f $c_cl_uri)
Write-Host ("Customer LND:         {0}" -f $c_ln_uri)

Write-Host "`nConnecting all parties`n----------------------`n"

Connect-Node -cmd $M_CL -uri $c_cl_uri -desc "Merchant (c-lightning) to Customer (c-lightning)"
Connect-Node -cmd $M_CL -uri $c_ln_uri -desc "Merchant (c-lightning) to Customer (LND)"
Connect-Node -cmd $M_CL -uri $m_ln_uri -desc "Merchant (c-lightning) to Merchant (LND)"
Connect-Node -cmd $C_CL -uri $m_cl_uri -desc "Customer (c-lightning) to Merchant (c-lightning)"
Connect-Node -cmd $C_CL -uri $m_ln_uri -desc "Customer (c-lightning) to Merchant (LND)"
Connect-Node -cmd $C_CL -uri $c_ln_uri -desc "Customer (c-lightning) to Customer (LND)"
Connect-Node -cmd $M_LN -uri $c_cl_uri -desc "Merchant (LND) to Customer (c-lightning)"
Connect-Node -cmd $M_LN -uri $c_cl_uri -desc "Merchant (LND) to Customer (c-lightning)"
Connect-Node -cmd $M_LN -uri $c_ln_uri -desc "Merchant (LND) to Customer (LND)"
Connect-Node -cmd $C_LN -uri $m_cl_uri -desc "Customer (LND) to Merchant (c-lightning)"
Connect-Node -cmd $C_LN -uri $c_cl_uri -desc "Customer (LND) to Customer (c-lightning)"
Connect-Node -cmd $C_LN -uri $m_ln_uri -desc "Customer (LND) to Merchant (LND)"


Write-Host "`nEstablishing channels`n----------------------`n"

New-Channel -cmd $C_CL -id $m_cl_info.id -desc "Customer (c-lightning) to Merchant (c-lightning)"
New-Channel -cmd $C_CL -id $m_ln_id -desc "Customer (c-lightning) to Merchant (LND)"
New-Channel -cmd $C_LN -id $c_cl_info.id -desc "Customer (LND) to Customer (c-lightning)"
New-Channel -cmd $M_CL -id $m_ln_id -desc "Merchant (c-lightning) to Merchant (LND)" -opts "announce=false"
New-Channel -cmd $C_LN -id $m_ln_id -desc "Customer (LND) to Merchant (LND)" -opts "--private"