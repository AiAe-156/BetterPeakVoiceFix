[Reflection.Assembly]::LoadFrom("f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\PhotonUnityNetworking.dll") > $null
$asm = [Reflection.Assembly]::LoadFrom("f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\Assembly-CSharp.dll")
$types = try { $asm.GetTypes() } catch { $_.Exception.Types | Where-Object { $_ -ne $null } }
$rpcs = @()
foreach ($t in $types) {
    if ($t -eq $null) { continue }
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static)) {
        try {
            foreach ($attr in $m.GetCustomAttributes($false)) {
                if ($attr.GetType().Name -eq "PunRPC") {
                    $rpcs += $m.Name
                }
            }
        } catch { }
    }
}
$rpcs | Select-Object -Unique | Sort-Object
