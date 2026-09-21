param([Parameter(Mandatory = $true)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
# Run in Windows PowerShell (.NET Framework) in its own process. Hardware SDKs
# can keep background threads alive that prevent NUnit's AppDomain unloading.
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $AppDirectory 'Perfview.exe'))
$reader = [Activator]::CreateInstance($assembly.GetType('Perfview.HardwareTemperatureSource'), $true)
try {
    $snapshot = $reader.Read()
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $readings = $snapshot.GetType().GetField('Readings', $flags).GetValue($snapshot)
    $ids = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($reading in $readings) {
        $type = $reading.GetType()
        $id = $type.GetField('Id', $flags).GetValue($reading)
        if (!$ids.Add($id)) { throw "Duplicate sensor identifier: $id" }
        $value = [double]$type.GetField('Celsius', $flags).GetValue($reading)
        if ([double]::IsInfinity($value) -or $value -le -273.15) { throw "Invalid sensor reading: $id" }
        $hardware = $type.GetField('Hardware', $flags).GetValue($reading)
        $name = $type.GetField('Name', $flags).GetValue($reading)
        $component = $type.GetField('Component', $flags).GetValue($reading)
        if ($null -eq $component) { throw "Sensor is not associated with a component: $id" }
        $display = if ([double]::IsNaN($value)) { 'Unavailable' } else { "$value C" }
        Write-Output "$component / $hardware / ${name}: $display"
    }
    foreach ($component in [Enum]::GetValues($assembly.GetType('Perfview.Metric'))) {
        $value = $snapshot.GetType().GetMethod('ForComponent', $flags).Invoke($snapshot, @($component))
        $display = if ([double]::IsNaN($value)) { 'Unavailable' } else { "$value C" }
        Write-Output "Graph ${component}: $display"
    }
    Write-Output "Temperature sensors: $($readings.Count). $($snapshot.GetType().GetField('Status', $flags).GetValue($snapshot))"
}
finally { $reader.Dispose() }
