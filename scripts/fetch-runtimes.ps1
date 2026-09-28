param(
    [ValidateSet('all', 'php', 'mysql')]
    [string]$Component = 'all'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Tmp = Join-Path $Root 'tmp'
New-Item -ItemType Directory -Force -Path $Tmp | Out-Null
$UserAgent = 'Mozilla/5.0 (Windows NT; Windows NT 10.0; en-US) NVX-Server/1.0'

function Write-NvxLog([string]$Message) {
    Write-Host $Message
}

function Get-RemoteFile([string]$Url, [string]$Destination) {
    Write-NvxLog "Downloading $Url"
    $directory = Split-Path $Destination -Parent
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $partial = $Destination + '.part'
    if (Test-Path $partial) { Remove-Item $partial -Force }
    Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing -UserAgent $UserAgent
    if (Test-Path $Destination) { Remove-Item $Destination -Force }
    Move-Item $partial $Destination
}

function Expand-NvxZip([string]$Zip, [string]$Destination) {
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $Zip -DestinationPath $Destination -Force
}

function Copy-NvxTree([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-NvxTree $_.FullName $target
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Install-NvxPhp {
    $cgi = Join-Path $Root 'php\php-cgi.exe'
    $cgiBin = Join-Path $Root 'php\bin\php-cgi.exe'
    if ((Test-Path $cgi) -or (Test-Path $cgiBin)) {
        Write-NvxLog 'PHP runtime already present.'
        return
    }

    $urls = @(
        'https://windows.php.net/downloads/releases/latest/php-8.3-nts-Win32-vs16-x64-latest.zip',
        'https://windows.php.net/downloads/releases/latest/php-8.2-nts-Win32-vs16-x64-latest.zip',
        'https://windows.php.net/downloads/releases/php-8.3.16-nts-Win32-vs16-x64.zip',
        'https://windows.php.net/downloads/releases/php-8.3.12-nts-Win32-vs16-x64.zip',
        'https://windows.php.net/downloads/releases/archives/php-8.3.12-nts-Win32-vs16-x64.zip'
    )
    $zip = Join-Path $Tmp 'php-runtime.zip'
    $unpacked = Join-Path $Tmp 'php-unpack'
    $downloaded = $false
    foreach ($url in $urls) {
        try {
            Get-RemoteFile $url $zip
            $downloaded = $true
            break
        }
        catch {
            Write-NvxLog "PHP download missed $url"
        }
    }
    if (-not $downloaded) {
        throw 'Could not download PHP. NVX Server needs php-cgi.exe in php\.'
    }

    Expand-NvxZip $zip $unpacked
    $source = $unpacked
    $nested = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    if ($nested -and (Test-Path (Join-Path $nested.FullName 'php-cgi.exe'))) {
        $source = $nested.FullName
    }
    elseif (-not (Test-Path (Join-Path $unpacked 'php-cgi.exe'))) {
        $found = Get-ChildItem $unpacked -Filter 'php-cgi.exe' -Recurse | Select-Object -First 1
        if (-not $found) { throw 'The PHP zip did not contain php-cgi.exe.' }
        $source = $found.DirectoryName
    }

    $phpRoot = Join-Path $Root 'php'
    New-Item -ItemType Directory -Force -Path $phpRoot | Out-Null
    Copy-NvxTree $source $phpRoot
    $bin = Join-Path $phpRoot 'bin'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    foreach ($name in @('php.exe', 'php-cgi.exe')) {
        $from = Join-Path $phpRoot $name
        if (Test-Path $from) {
            Copy-Item $from (Join-Path $bin $name) -Force
        }
    }

    Write-NvxPhpIni
    Remove-Item $unpacked -Recurse -Force -ErrorAction SilentlyContinue
    Write-NvxLog 'PHP runtime installed into php\'
}

function Write-NvxPhpIni {
    $phpRoot = Join-Path $Root 'php'
    $ext = Join-Path $phpRoot 'ext'
    $slashRoot = $Root.Replace('\', '/')
    $slashExt = $ext.Replace('\', '/')
    $extensions = @()
    foreach ($pair in @(
            @{ Name = 'pdo_sqlite'; Dll = 'php_pdo_sqlite.dll' },
            @{ Name = 'sqlite3'; Dll = 'php_sqlite3.dll' },
            @{ Name = 'pdo_mysql'; Dll = 'php_pdo_mysql.dll' },
            @{ Name = 'mysqli'; Dll = 'php_mysqli.dll' },
            @{ Name = 'openssl'; Dll = 'php_openssl.dll' },
            @{ Name = 'mbstring'; Dll = 'php_mbstring.dll' },
            @{ Name = 'zip'; Dll = 'php_zip.dll' },
            @{ Name = 'curl'; Dll = 'php_curl.dll' },
            @{ Name = 'fileinfo'; Dll = 'php_fileinfo.dll' },
            @{ Name = 'gd'; Dll = 'php_gd.dll' }
        )) {
        if (Test-Path (Join-Path $ext $pair.Dll)) {
            $extensions += ('extension=' + $pair.Name)
        }
    }

    $ini = @"
; NVX Server PHP runtime. Do not point this at XAMPP.
expose_php = Off
display_errors = Off
log_errors = On
memory_limit = 256M
post_max_size = 32M
upload_max_filesize = 32M
max_execution_time = 60
date.timezone = UTC
cgi.force_redirect = 0
cgi.fix_pathinfo = 1
extension_dir = "$slashExt"
$($extensions -join "`r`n")
error_log = "$slashRoot/php/logs/error.log"
session.save_path = "$slashRoot/tmp/sessions"
sys_temp_dir = "$slashRoot/tmp"
upload_tmp_dir = "$slashRoot/tmp"
"@
    New-Item -ItemType Directory -Force -Path (Join-Path $phpRoot 'conf') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $phpRoot 'logs') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'tmp\sessions') | Out-Null
    Set-Content -Path (Join-Path $phpRoot 'php.ini') -Value $ini -Encoding ASCII
    Set-Content -Path (Join-Path $phpRoot 'conf\php.ini') -Value $ini -Encoding ASCII
}

function Get-NvxMysqlPort {
    $file = Join-Path $Root 'config\ports.sys'
    if (Test-Path $file) {
        foreach ($line in Get-Content $file) {
            if ($line -match '^\s*mysql\s*=\s*"?(\d+)"?') { return $Matches[1] }
        }
    }
    return '3307'
}

function Install-NvxMysql {
    $mysqld = Join-Path $Root 'mysql\bin\mysqld.exe'
    if (Test-Path $mysqld) {
        Write-NvxLog 'MySQL runtime already present.'
        return
    }

    $urls = @(
        'https://archive.mariadb.org/mariadb-11.4.7/winx64-packages/mariadb-11.4.7-winx64.zip',
        'https://archive.mariadb.org/mariadb-11.4.5/winx64-packages/mariadb-11.4.5-winx64.zip',
        'https://archive.mariadb.org/mariadb-10.11.11/winx64-packages/mariadb-10.11.11-winx64.zip',
        'https://mirror.mariadb.org/mariadb-11.4.7/winx64-packages/mariadb-11.4.7-winx64.zip'
    )
    $zip = Join-Path $Tmp 'mysql-runtime.zip'
    $unpacked = Join-Path $Tmp 'mysql-unpack'
    $downloaded = Test-Path $zip
    if ($downloaded) {
        Write-NvxLog 'Using the MariaDB zip already in tmp\'
    }
    else {
        foreach ($url in $urls) {
            try {
                Get-RemoteFile $url $zip
                $downloaded = $true
                break
            }
            catch {
                Write-NvxLog "MySQL download missed $url"
            }
        }
    }
    if (-not $downloaded) {
        throw 'Could not download MariaDB. NVX Server needs mysqld.exe in mysql\bin.'
    }

    Expand-NvxZip $zip $unpacked
    $found = Get-ChildItem $unpacked -Filter 'mysqld.exe' -Recurse | Select-Object -First 1
    if (-not $found) { throw 'The MariaDB zip did not contain mysqld.exe.' }
    $sourceHome = Split-Path $found.DirectoryName -Parent
    $mysqlRoot = Join-Path $Root 'mysql'
    New-Item -ItemType Directory -Force -Path $mysqlRoot | Out-Null
    foreach ($name in @('bin', 'lib', 'share', 'include', 'plugins', 'support-files')) {
        $from = Join-Path $sourceHome $name
        if (Test-Path $from) {
            Copy-NvxTree $from (Join-Path $mysqlRoot $name)
        }
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $mysqlRoot 'data') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $mysqlRoot 'logs') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $mysqlRoot 'conf') | Out-Null
    Remove-Item $unpacked -Recurse -Force -ErrorAction SilentlyContinue
    Write-NvxLog 'MySQL runtime installed into mysql\'
}

if ($Component -eq 'all' -or $Component -eq 'php') { Install-NvxPhp }
if ($Component -eq 'all' -or $Component -eq 'mysql') { Install-NvxMysql }
Write-NvxLog 'NVX Server runtimes are ready.'
