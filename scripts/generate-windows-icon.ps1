param(
    [string]$SourcePng = (Join-Path $PSScriptRoot '..\src\AutoEmulatorUpdate.App\Assets\app-icon.png'),
    [string]$OutputIco = (Join-Path $PSScriptRoot '..\src\AutoEmulatorUpdate.App\Assets\app-icon.generated.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng))
try {
    $bitmap = New-Object System.Drawing.Bitmap 256, 256
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.DrawImage($source, 0, 0, 256, 256)
        }
        finally {
            $graphics.Dispose()
        }

        $hIcon = $bitmap.GetHicon()
        try {
            $icon = [System.Drawing.Icon]::FromHandle($hIcon)
            $directory = Split-Path -Parent $OutputIco
            if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
            $stream = [System.IO.File]::Create($OutputIco)
            try {
                $icon.Save($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeIconMethods {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
'@ -ErrorAction SilentlyContinue
            [NativeIconMethods]::DestroyIcon($hIcon) | Out-Null
        }
    }
    finally {
        $bitmap.Dispose()
    }
}
finally {
    $source.Dispose()
}

if (-not (Test-Path $OutputIco) -or (Get-Item $OutputIco).Length -lt 100) {
    throw "Generated ICO is missing or invalid: $OutputIco"
}

Write-Host "Generated Windows icon: $OutputIco"
