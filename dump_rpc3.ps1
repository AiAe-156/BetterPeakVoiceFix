Add-Type -Path "f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\PhotonUnityNetworking.dll"
$asm = [Reflection.Assembly]::LoadFrom("f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\Assembly-CSharp.dll")
$types = try { $asm.GetTypes() } catch { $_.Exception.Types | Where-Object { $_ -ne $null } }
$rpcs = @()

foreach ($t in $types) {
    if ($t -eq $null) { continue }
    # GetMethods for public/private instance/static
    $methods = try { $t.GetMethods([System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic) } catch { @() }
    
    foreach ($m in $methods) {
        $hasPunRPC = $false
        try {
            # Check CustomAttributes data (avoids loading the actual attribute type if missing deps)
            foreach ($attrData in $m.GetCustomAttributesData()) {
                if ($attrData.AttributeType.Name -eq "PunRPC") {
                    $hasPunRPC = $true
                    break
                }
            }
        } catch { }
        
        if ($hasPunRPC) {
            $rpcs += $m.Name
        }
    }
}
$rpcs | Select-Object -Unique | Sort-Object
