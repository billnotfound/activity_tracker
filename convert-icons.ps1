# Convert PNG icons to ICO format for tray icon
# Requires .NET Framework (built into Windows)

Add-Type -AssemblyName System.Drawing

function Convert-PngToIco {
    param(
        [string]$PngPath,
        [string]$IcoPath
    )

    $png = [System.Drawing.Image]::FromFile($PngPath)
    $icon = [System.Drawing.Icon]::FromHandle($png.GetHicon())

    $stream = [System.IO.File]::Create($IcoPath)
    $icon.Save($stream)
    $stream.Close()

    $png.Dispose()
    $icon.Dispose()

    Write-Host "Created $IcoPath"
}

# Convert timer icon
$timerPng = "WinActivityTracker.Web\src\icon\timer48px.png"
$timerIco = "WinActivityTracker.Service\Resources\timer.ico"

if (Test-Path $timerPng) {
    Convert-PngToIco -PngPath $timerPng -IcoPath $timerIco
} else {
    Write-Error "Timer PNG not found: $timerPng"
}

# Convert settings icon
$settingsPng = "WinActivityTracker.Web\src\icon\settings48px.png"
$settingsIco = "WinActivityTracker.Service\Resources\settings.ico"

if (Test-Path $settingsPng) {
    Convert-PngToIco -PngPath $settingsPng -IcoPath $settingsIco
} else {
    Write-Error "Settings PNG not found: $settingsPng"
}

Write-Host "Icon conversion complete!"
