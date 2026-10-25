# Turns the full-size frames in assets\originals\ (kept out of the repository) into the trimmed, smaller sprites that get embedded in Capri.exe (assets\art\).
# Transparent frames are cropped to the figure; every image is scaled down so its longest side is at most $limit pixels.
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File tools\make-art.ps1

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ArtTrim
{
    // The box around every pixel that is at least a little visible, or the whole image when nothing is see-through.
    public static Rectangle Visible(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1, clear = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bytes[y * data.Stride + x * 4 + 3] > 12) { if (x < left) left = x; if (x > right) right = x; if (y < top) top = y; if (y > bottom) bottom = y; }
                    else clear++;
                }
            if (right < 0 || clear < bitmap.Width * bitmap.Height / 50) return new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
"@ -ReferencedAssemblies System.Drawing

$limit = 760
$source = Join-Path $PSScriptRoot '..\assets\originals'
$target = Join-Path $PSScriptRoot '..\assets\art'
New-Item -ItemType Directory -Force $target | Out-Null

$names = [ordered]@{
    'Capri_sleep.png'                           = 'sleep'
    'capri_laying_on_back_towards_right.png'    = 'laying'
    'capri_happy_half_body.png'                 = 'happy'
    'capri_happy_face_only.png'                 = 'happyface'
    'capri_standing_happy_eyes_closed.png'      = 'content'
    'capri_mouth_open_hands_towards_camera.png' = 'mouth'
    'capri_arms_to_the_side_half_body.png'      = 'arms'
    'capri_leaning_right.png'                   = 'lean'
    'capri_siting.png'                          = 'sit'
    'capri6a.png'                               = 'scream'
    'capri6b.png'                               = 'talk-open'
    'capri6c.png'                               = 'talk-closed'
    'capri_eyes.png'                            = 'eyes'
    'capri_eyes_closed.png'                     = 'eyes-closed'
    'capri_eyes_looking_left.png'               = 'eyes-left'
    'capri_eyes_looking_right.png'              = 'eyes-right'
    'capri_eyes_many.png'                       = 'eyes-many'
    'capri_eyes_many_glitch.png'                = 'eyes-glitch'
}

foreach ($file in $names.Keys) {
    $path = Join-Path $source $file
    if (-not (Test-Path $path)) { Write-Host "missing  $file"; continue }
    $original = New-Object System.Drawing.Bitmap $path
    $box = [ArtTrim]::Visible($original)
    $pad = 6
    $crop = [System.Drawing.Rectangle]::Intersect((New-Object System.Drawing.Rectangle 0, 0, $original.Width, $original.Height), (New-Object System.Drawing.Rectangle ($box.X - $pad), ($box.Y - $pad), ($box.Width + 2 * $pad), ($box.Height + 2 * $pad)))
    if ($box.Width -eq $original.Width -and $box.Height -eq $original.Height) { $crop = $box }
    $scale = [Math]::Min(1.0, $limit / [Math]::Max($crop.Width, $crop.Height))
    $width = [int][Math]::Round($crop.Width * $scale)
    $height = [int][Math]::Round($crop.Height * $scale)
    $result = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($result)
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.PixelOffsetMode = 'HighQuality'
    $graphics.CompositingMode = 'SourceCopy'
    $graphics.DrawImage($original, (New-Object System.Drawing.Rectangle 0, 0, $width, $height), $crop, 'Pixel')
    $graphics.Dispose()
    $out = Join-Path $target ($names[$file] + '.png')
    $result.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    '{0,-44} {1}x{2} -> {3}x{4}  {5:N0} KB  {6}' -f $file, $original.Width, $original.Height, $width, $height, ((Get-Item $out).Length / 1KB), ($names[$file] + '.png')
    $result.Dispose()
    $original.Dispose()
}
