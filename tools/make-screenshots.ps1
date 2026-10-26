# Takes README screenshots of dist\CleanCache1C.exe on a demo profile (see make-demo-profile.ps1):
#   .\tools\make-screenshots.ps1 -Profile C:\temp\demo-profile
# Writes docs\screenshots\main.png, search.png, cleanup.png. Don't touch the mouse while it runs.

param([Parameter(Mandatory)] [string] $Profile)

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace Native -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
public struct RECT { public int L, T, R, B; }
'@

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null
$UIA = [System.Windows.Automation.AutomationElement]
$Name = [System.Windows.Automation.AutomationElement]::NameProperty

function Save-Window([IntPtr]$hwnd, [string]$file) {
    [Native.W]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600
    $r = New-Object Native.W+RECT
    # DWMWA_EXTENDED_FRAME_BOUNDS: the visible frame, without the invisible resize border
    [Native.W]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "saved $file"
}

function Find-ByName($parent, [string]$text) {
    $cond = New-Object System.Windows.Automation.PropertyCondition $Name, $text
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Click($element) {
    $b = $element.Current.BoundingRectangle
    [Native.W]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null
    [Native.W]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)  # left down
    [Native.W]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)  # left up
    Start-Sleep -Milliseconds 500
}

$exe = Join-Path $root 'dist\CleanCache1C.exe'
$p = Start-Process $exe -ArgumentList '--profile', "`"$Profile`"" -PassThru
try {
    $p.WaitForInputIdle(10000) | Out-Null
    Start-Sleep -Seconds 4          # sizes are computed in background
    $p.Refresh()
    $hwnd = $p.MainWindowHandle
    $win = $UIA::FromHandle($hwnd)

    Save-Window $hwnd 'main.png'

    # search
    # the search box comes right after its label
    $label = Find-ByName $win '   Поиск:'
    $search = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetNextSibling($label)
    Click $search
    [System.Windows.Forms.SendKeys]::SendWait('Торговля')
    Start-Sleep -Milliseconds 500
    Save-Window $hwnd 'search.png'
    [System.Windows.Forms.SendKeys]::SendWait('{END}+{HOME}{DEL}')
    Start-Sleep -Milliseconds 500

    # mark orphaned folders and open the cleanup confirmation
    Click (Find-ByName $win 'Отметить отсутствующие в списке')
    Click (Find-ByName $win 'Очистить кэш отмеченных')
    Start-Sleep -Seconds 1
    Save-Window $hwnd 'cleanup.png'
}
finally {
    # the confirmation is never answered: killing the process leaves the files untouched
    if (-not $p.HasExited) { $p.Kill() }
}
