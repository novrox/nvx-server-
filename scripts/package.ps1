param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $root 'dist'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('nvx-package-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'NVX Server'
$archive = Join-Path $dist 'NVX-Server.zip'

function Write-PackageText([string]$RelativePath, [string]$Content) {
    $path = Join-Path $package $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content, [Text.Encoding]::ASCII)
}

try {
    [IO.Directory]::CreateDirectory($package) | Out-Null
    $excludedTopLevel = @('.git', '.vscode', 'bin', 'dist', 'extras', 'logs', 'phpMyAdmin', 'tmp', 'tools', 'uninstall')
    $textExtensions = @('.bat', '.conf', '.css', '.html', '.ini', '.js', '.json', '.manifest', '.md', '.php', '.ps1', '.sql', '.sys', '.template', '.txt', '.xml')

    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        $normalized = $relative.Replace('\', '/')
        $parts = $normalized.Split('/')
        if ($parts[0] -in $excludedTopLevel) { continue }
        if (($parts[0] -eq 'data' -or ($parts.Length -ge 2 -and $parts[0] -eq 'nvxh' -and $parts[1] -eq 'apps')) -and $file.Name -notlike 'README*') { continue }
        if ($parts -contains 'logs' -or $parts -contains 'tmp') { continue }
        if ($normalized -match '^(mysql/(data|bin|lib|share|include|plugin|plugins|support-files)|php)(/|$)') { continue }
        if ($normalized -in @('apache/conf/httpd.conf', 'mysql/conf/my.ini')) { continue }
        if ($normalized -match '(^|/)[^/]+\.(exe|log|pid|pdb|sqlite(-.+)?|zip)$') { continue }
        if ($normalized -eq 'php/snapshot.txt' -or $normalized -eq 'nvx-server.txt' -or $normalized -eq '.gitignore') { continue }
        if ($parts.Length -eq 1 -and $file.Extension -eq '.exe') { continue }

        $destination = Join-Path $package $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }

    $compilerCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Could not find csc.exe. Install .NET Framework 4.x developer tools or Visual Studio.' }
    $icon = Join-Path $package 'nvx.ico'
    if (-not (Test-Path -LiteralPath $icon)) { throw 'nvx.ico is required to build the branded applications.' }

    $source = Join-Path $package 'resources\src\NvxApp'
    $appSources = @('Program.cs', 'MainForm.cs', 'NvxServices.cs', 'NvxPrograms.cs', 'NvxRuntimes.cs', 'NvxTheme.cs', 'NvxConfig.cs', 'NvxDatabase.cs') | ForEach-Object { Join-Path $source $_ }
    $appArguments = @('/nologo', '/optimize+', '/target:winexe', '/platform:x64', ('/out:' + (Join-Path $package 'nvx.exe')), ('/win32icon:' + $icon), '/reference:System.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/win32manifest:' + (Join-Path $source 'app.manifest'))) + $appSources
    & $compiler @appArguments
    if ($LASTEXITCODE -ne 0) { throw 'Building nvx.exe failed.' }

    $setupArguments = @('/nologo', '/optimize+', '/target:winexe', ('/out:' + (Join-Path $package 'nvx-setup.exe')), ('/win32icon:' + $icon), '/reference:System.dll', '/reference:System.Windows.Forms.dll', (Join-Path $source 'Setup.cs'))
    & $compiler @setupArguments
    if ($LASTEXITCODE -ne 0) { throw 'Building nvx-setup.exe failed.' }

    Write-PackageText 'config\paths.sys' @'
; NVX Server paths
; Rewritten when the control panel saves settings.
web = "nvxh"
apache = "apache"
mysql = "mysql"
php = "php"
data = "data"
databases = "data/databases"
uploads = "data/uploads"
backups = "data/backups"
user_data = "data/user-data"
logs = "logs"
tmp = "tmp"
php_executable = ""
'@
    Write-PackageText 'config\ports.sys' @'
; NVX Server ports
; Rewritten when the control panel saves settings.
http = "80"
https = "8443"
mysql = "3307"
ftp = "21"
mail = "25"
'@
    Write-PackageText 'config\security.sys' @'
; NVX Server security
; Rewritten when the control panel saves settings.
bind_localhost_only = "1"
allow_remote = "0"
admin_user = "admin"
admin_password_hash = ""
session_name = "NVXSESSID"
'@
    Write-PackageText 'config\server.sys' @'
; NVX Server server
; Rewritten when the control panel saves settings.
name = "NVX Server"
version = "1.0.0"
installed = "0"
document_root = "nvxh"
bind = "127.0.0.1"
timezone = "UTC"
'@
    Write-PackageText 'config\services.sys' @'
; NVX Server services
; Rewritten when the control panel saves settings.
php = "enabled"
apache = "auto"
mysql = "auto"
'@
    Write-PackageText 'services\apache.sys' @'
name = "Apache"
role = "web"
executable = "apache/bin/httpd.exe"
config = "apache/conf/httpd.conf"
template = "apache/conf/httpd.conf.template"
port = "http"
'@
    Write-PackageText 'services\mysql.sys' @'
name = "MySQL"
role = "database"
executable = "mysql/bin/mysqld.exe"
config = "mysql/conf/my.ini"
template = "mysql/conf/my.ini.template"
port = "mysql"
user = "root"
password = ""
'@
    Write-PackageText 'services\php.sys' @'
name = "PHP"
role = "web"
mode = "builtin"
port = "http"
'@
    Write-PackageText 'install\install.sys' @'
; NVX Server install
; Rewritten when the control panel saves settings.
state = "pending"
installed_at = ""
php_binary = ""
version = "1.0.0"
'@

    $selfTest = Join-Path $package 'nvx.exe'
    & $selfTest '--self-test'
    if ($LASTEXITCODE -ne 0) { throw 'The packaged application failed its SQLite self-test.' }
    Remove-Item -LiteralPath (Join-Path $package 'tmp') -Recurse -Force -ErrorAction SilentlyContinue

    $sensitivePaths = @($root, $root.Replace('\', '/'), [Environment]::GetFolderPath('UserProfile')) | Where-Object { $_ }
    foreach ($file in Get-ChildItem -LiteralPath $package -File -Recurse -Force) {
        if ($file.Extension -notin $textExtensions -and $file.Name -notlike 'README*') { continue }
        $content = [IO.File]::ReadAllText($file.FullName)
        foreach ($sensitivePath in $sensitivePaths) {
            if ($content.IndexOf($sensitivePath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Machine-specific path found in the package: $($file.FullName)"
            }
        }
    }

    [IO.Directory]::CreateDirectory($dist) | Out-Null
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($required in @('NVX Server/nvx.exe', 'NVX Server/nvx-setup.exe', 'NVX Server/nvx.ico', 'NVX Server/nvx-ico.png', 'NVX Server/nvxh/assets/img/nvx-ico.png', 'NVX Server/README.txt', 'NVX Server/scripts/start.bat')) {
            if ($required -notin $entries) { throw "Required file missing from the ZIP: $required" }
        }
        foreach ($entry in $zip.Entries) {
            $normalized = $entry.FullName.Replace('\', '/')
            if ($normalized -match '^(bin/|mysql/data/|php/|logs/|tmp/)|(^|/)(httpd\.conf|my\.ini|php\.ini)$|\.(log|pid|pdb|sqlite(-.+)?|zip)$') {
                throw "Generated or machine-specific file found in the ZIP: $normalized"
            }
            if ($entry.Length -eq 0 -or $entry.Name -notmatch '\.(bat|conf|css|html|ini|js|json|manifest|md|php|ps1|sql|sys|template|txt|xml)$') { continue }
            $reader = New-Object IO.StreamReader($entry.Open())
            try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
            foreach ($sensitivePath in $sensitivePaths) {
                if ($content.IndexOf($sensitivePath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    throw "Machine-specific path found inside the ZIP: $normalized"
                }
            }
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "Created clean installer package: $archive"
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}