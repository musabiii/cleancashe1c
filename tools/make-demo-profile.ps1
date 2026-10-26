# Creates a fake user profile with made-up 1C bases and cache folders, for screenshots and testing:
#   .\tools\make-demo-profile.ps1 -Path C:\temp\demo-profile
#   dist\CleanCache1C.exe --profile C:\temp\demo-profile
# Cache files are sparse: they report the given size but take almost no disk space.

param([Parameter(Mandatory)] [string] $Path)

$roaming = Join-Path $Path 'AppData\Roaming\1C'
$local = Join-Path $Path 'AppData\Local\1C'
if (Test-Path $Path) { Remove-Item -Recurse -Force $Path }
New-Item -ItemType Directory -Force (Join-Path $roaming '1CEStart'), (Join-Path $roaming '1cv8'), (Join-Path $local '1cv8') | Out-Null

function New-SparseFile([string]$file, [long]$size) {
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [System.IO.File]::Create($file).Dispose()
    fsutil sparse setflag $file | Out-Null
    $fs = [System.IO.File]::OpenWrite($file)
    $fs.SetLength($size)
    $fs.Dispose()
}

# Name, Folder, Connect, cache size, settings size (0 = no folder)
$bases = @(
    @('Бухгалтерия предприятия 3.0 (ООО Ромашка)', 'Бухгалтерия', 'Srvr="srv1c";Ref="bp_romashka";', 1840MB, 12MB),
    @('Управление торговлей 11.5', 'Торговля', 'Srvr="srv1c";Ref="ut11";', 1210MB, 8MB),
    @('Тестовая копия УТ', 'Торговля', 'Srvr="srv1c-test";Ref="ut11_test";Usr="admin";Pwd="12345";', 520MB, 2MB),
    @('Розница 2.3', 'Торговля', 'Srvr="srv1c";Ref="retail";', 455MB, 3MB),
    @('Зарплата и управление персоналом 3.1', 'Зарплата', 'Srvr="srv1c";Ref="zup31";', 640MB, 5MB),
    @('Бухгалтерия (ИП Иванов)', 'Бухгалтерия', 'File="D:\Bases\ip_ivanov";', 310MB, 900KB),
    @('Документооборот 3.0', '', 'Srvr="srv1c";Ref="do3";', 220MB, 1500KB),
    @('Демо: ERP Управление предприятием 2', 'Демо', 'File="C:\Demo\erp";', 760MB, 40KB),
    @('Демо: Управление нашей фирмой', 'Демо', 'File="C:\Demo\unf";', 95MB, 25KB),
    @('Архив БП 2021', 'Архив', 'File="D:\Archive\bp2021";', 31MB, 0),
    @('Новая база', '', 'File="D:\Bases\new";', 0, 0)
)
# cache folders left from bases that are no longer in the list
$orphans = @(380MB, 150MB, 12MB)

$v8i = New-Object System.Text.StringBuilder
$folders = $bases | ForEach-Object { $_[1] } | Where-Object { $_ } | Sort-Object -Unique
foreach ($f in $folders) {
    [void]$v8i.AppendLine("[$f]").AppendLine("ID=$([guid]::NewGuid())").AppendLine('OrderInList=-1').AppendLine('Folder=/').AppendLine('External=0')
}

foreach ($b in $bases) {
    $id = [guid]::NewGuid().ToString()
    [void]$v8i.AppendLine("[$($b[0])]").AppendLine("Connect=$($b[2])").AppendLine("ID=$id")
    [void]$v8i.AppendLine("Folder=/$($b[1])").AppendLine('External=0').AppendLine('App=Auto').AppendLine('Version=8.3')
    if ($b[3] -gt 0) {
        New-SparseFile (Join-Path $local "1cv8\$id\Config\config.bin") ([long]($b[3] * 0.7))
        New-SparseFile (Join-Path $local "1cv8\$id\SICache\cache.bin") ([long]($b[3] * 0.3))
    }
    if ($b[4] -gt 0) {
        New-SparseFile (Join-Path $roaming "1cv8\$id\settings.pfl") ([long]$b[4])
    }
}

foreach ($size in $orphans) {
    New-SparseFile (Join-Path $local "1cv8\$([guid]::NewGuid())\Config\config.bin") ([long]$size)
}

[System.IO.File]::WriteAllText((Join-Path $roaming '1CEStart\ibases.v8i'), $v8i.ToString(), (New-Object System.Text.UTF8Encoding $true))
"Demo profile: $Path"
