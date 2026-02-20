[Reflection.Assembly]::LoadFrom("f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\PhotonUnityNetworking.dll") > $null
[Reflection.Assembly]::LoadFrom("f:\Games\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed\Assembly-CSharp.dll") > $null
$asm = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.ManifestModule.Name -eq "Assembly-CSharp.dll" }
$types = $asm.GetTypes()
$rpcs = @()
foreach ($t in $types) {
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static)) {
        if ($m.GetCustomAttributes($true).GetType().Name -match "PunRPC") {
            continue
        }
        foreach ($attr in $m.GetCustomAttributes($true)) {
            if ($attr.GetType().Name -eq "PunRPC") {
                $rpcs += $m.Name
            }
        }
    }
}
$rpcs | Select-Object -Unique | Sort-Object
