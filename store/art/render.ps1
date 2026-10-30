# Renders the Store images (poster, box and hero art) from the pages next to this script into store/images,
# with Microsoft Edge in headless mode at their exact pixel sizes. The screenshots come from store/screenshots.
#   pwsh store/art/render.ps1
$ErrorActionPreference = 'Stop'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge bulunamadı' }

$out = Join-Path $PSScriptRoot '..\images'
New-Item -ItemType Directory -Force $out | Out-Null
# A profile of its own, so the user's Edge profile is neither used nor changed.
$profile = Join-Path ([IO.Path]::GetTempPath()) 'postaci-store-art'

$jobs = @(
    @{ Page = 'poster'; Lang = 'tr'; Width = 1440; Height = 2160; File = 'poster-9x16-tr.png' },
    @{ Page = 'poster'; Lang = 'en'; Width = 1440; Height = 2160; File = 'poster-9x16-en.png' },
    @{ Page = 'box'; Lang = 'tr'; Width = 2160; Height = 2160; File = 'box-1x1.png' },
    @{ Page = 'hero'; Lang = 'tr'; Width = 3840; Height = 2160; File = 'hero-16x9-tr.png' },
    @{ Page = 'hero'; Lang = 'en'; Width = 3840; Height = 2160; File = 'hero-16x9-en.png' }
)
Add-Type -AssemblyName System.Drawing
foreach ($job in $jobs) {
    $url = ([Uri](Join-Path $PSScriptRoot "$($job.Page).html")).AbsoluteUri + "?lang=$($job.Lang)"
    $target = [IO.Path]::GetFullPath((Join-Path $out $job.File))
    # Started and waited for: called directly, msedge.exe hands over to another process and returns at once.
    Start-Process -FilePath $edge -Wait -RedirectStandardError (Join-Path ([IO.Path]::GetTempPath()) 'postaci-store-art.log') -ArgumentList @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', '--no-first-run',
        "--user-data-dir=$profile", '--allow-file-access-from-files', '--virtual-time-budget=3000',
        "--window-size=$($job.Width),$($job.Height)", "--screenshot=$target", $url)
    $image = [System.Drawing.Image]::FromFile($target)
    "$($job.File): $($image.Width)×$($image.Height)"
    $image.Dispose()
}
