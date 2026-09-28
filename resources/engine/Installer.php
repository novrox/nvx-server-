<?php
declare(strict_types=1);

final class Installer
{
    public function __construct(private NvxContext $nvx)
    {
    }

    public function requirements(): array
    {
        $required = $this->nvx->config->read('requirements');
        $checks = [];
        $phpMin = $required['php_min'] !== '' ? $required['php_min'] : '8.1';
        $checks[] = [
            'label' => 'PHP ' . $phpMin . ' or newer',
            'ok' => version_compare(PHP_VERSION, $phpMin, '>='),
            'detail' => 'This process is PHP ' . PHP_VERSION,
        ];
        $extensions = array_filter(array_map('trim', explode(',', $required['extensions'] !== '' ? $required['extensions'] : 'pdo,pdo_sqlite,json,session')));
        foreach ($extensions as $extension) {
            $checks[] = [
                'label' => 'Extension ' . $extension,
                'ok' => extension_loaded($extension),
                'detail' => extension_loaded($extension) ? 'Loaded' : 'Not loaded',
            ];
        }
        $directories = array_filter(array_map('trim', explode(',', $required['directories'])));
        foreach ($directories as $directory) {
            $path = $this->nvx->path($directory);
            $writable = is_dir($path) && is_writable($path);
            $checks[] = [
                'label' => $directory . ' is writable',
                'ok' => $writable,
                'detail' => $writable ? 'Ready' : 'Not writable',
            ];
        }
        return $checks;
    }

    public function ready(): bool
    {
        foreach ($this->requirements() as $check) {
            if (!$check['ok']) {
                return false;
            }
        }
        return true;
    }

    public function apply(array $input): void
    {
        if ($this->nvx->config->get('server', 'installed') === '1') {
            throw new RuntimeException('NVX Server is already set up.');
        }
        if (!$this->ready()) {
            throw new RuntimeException('Fix the requirements before continuing.');
        }
        $name = trim((string) ($input['name'] ?? ''));
        $user = trim((string) ($input['user'] ?? ''));
        $password = (string) ($input['password'] ?? '');
        $confirm = (string) ($input['password2'] ?? '');
        $timezone = trim((string) ($input['timezone'] ?? 'UTC'));
        $http = (int) ($input['http_port'] ?? 0);
        $mysql = (int) ($input['mysql_port'] ?? 0);

        if ($name === '' || strlen($name) > 64) {
            throw new RuntimeException('Enter a server name up to 64 characters.');
        }
        if (preg_match('/^[A-Za-z][A-Za-z0-9_-]{2,31}$/', $user) !== 1) {
            throw new RuntimeException('The admin name must be 3 to 32 characters and start with a letter.');
        }
        if (strlen($password) < 8 || strlen($password) > 200) {
            throw new RuntimeException('Use a password of at least 8 characters.');
        }
        if (!hash_equals($password, $confirm)) {
            throw new RuntimeException('The passwords do not match.');
        }
        if (!in_array($timezone, timezone_identifiers_list(), true)) {
            throw new RuntimeException('Choose a valid timezone.');
        }
        if ($http < 1 || $http > 65535 || $mysql < 1 || $mysql > 65535) {
            throw new RuntimeException('Ports must be between 1 and 65535.');
        }
        if ($http === $mysql) {
            throw new RuntimeException('The web port and the MySQL port must be different.');
        }

        $this->nvx->config->update('server', [
            'name' => $name,
            'installed' => '1',
            'timezone' => $timezone,
            'bind' => '127.0.0.1',
        ]);
        $this->nvx->config->update('ports', [
            'http' => (string) $http,
            'mysql' => (string) $mysql,
        ]);
        $this->nvx->config->update('security', [
            'admin_user' => $user,
            'admin_password_hash' => password_hash($password, PASSWORD_DEFAULT),
            'bind_localhost_only' => '1',
            'allow_remote' => '0',
        ]);
        $this->nvx->config->update('install', [
            'state' => 'ready',
            'installed_at' => gmdate('c'),
            'php_binary' => $this->nvx->services->phpBinary() ?? '',
        ]);
        $this->nvx->log->server('Setup completed for ' . $name . '.');
    }

    public function backup(): string
    {
        $stamp = gmdate('Ymd-His');
        $base = $this->nvx->path('data/backups/nvx-' . $stamp);
        if (class_exists(ZipArchive::class)) {
            $zipPath = $base . '.zip';
            $zip = new ZipArchive();
            if ($zip->open($zipPath, ZipArchive::CREATE) !== true) {
                throw new RuntimeException('Could not create the backup archive.');
            }
            $this->addTree($zip, $this->nvx->path('config'), 'config');
            $this->addTree($zip, $this->nvx->path('data/databases'), 'data/databases');
            $this->addTree($zip, $this->nvx->path('data/user-data'), 'data/user-data');
            $this->addTree($zip, $this->nvx->path('data/uploads'), 'data/uploads');
            $zip->close();
            $this->nvx->log->server('Backup written to data/backups/' . basename($zipPath) . '.');
            return $zipPath;
        }

        if (!mkdir($base, 0775, true) && !is_dir($base)) {
            throw new RuntimeException('Could not create the backup folder.');
        }
        $this->copyTree($this->nvx->path('config'), $base . DIRECTORY_SEPARATOR . 'config');
        $this->copyTree($this->nvx->path('data/databases'), $base . DIRECTORY_SEPARATOR . 'databases');
        $this->nvx->log->server('Backup written to data/backups/' . basename($base) . '.');
        return $base;
    }

    public function uninstall(bool $confirmed): void
    {
        if (!$confirmed) {
            throw new RuntimeException('Uninstall was not confirmed.');
        }
        try {
            $this->nvx->services->stop('all');
        } catch (Throwable $e) {
            $this->nvx->log->error('Stop during uninstall: ' . $e->getMessage());
        }
        $this->deleteChildren($this->nvx->path('data/databases'), ['.sqlite', '.sqlite-journal', '.sqlite-wal', '.sqlite-shm']);
        $this->deleteChildren($this->nvx->path('tmp'), []);
        $this->emptyLog($this->nvx->path('logs/server.log'));
        $this->emptyLog($this->nvx->path('logs/error.log'));
        $this->emptyLog($this->nvx->path('logs/access.log'));
        $defaults = $this->nvx->config->readSections('install/defaults.sys');
        foreach (['server', 'ports', 'paths', 'services', 'security'] as $name) {
            if (isset($defaults[$name]) && is_array($defaults[$name])) {
                $this->nvx->config->write($name, $defaults[$name]);
            }
        }
        $this->nvx->config->write('install', [
            'state' => 'pending',
            'installed_at' => '',
            'php_binary' => '',
            'version' => '1.0.0',
        ]);
        foreach (['tmp/sessions', 'tmp/pids'] as $directory) {
            $path = $this->nvx->path($directory);
            if (!is_dir($path)) {
                mkdir($path, 0775, true);
            }
        }
        $this->nvx->log->server('NVX Server was reset. Program files are still in place.');
    }

    private function addTree(ZipArchive $zip, string $directory, string $prefix): void
    {
        if (!is_dir($directory)) {
            return;
        }
        $items = new RecursiveIteratorIterator(
            new RecursiveDirectoryIterator($directory, FilesystemIterator::SKIP_DOTS)
        );
        foreach ($items as $item) {
            if (!$item->isFile()) {
                continue;
            }
            $relative = substr($item->getPathname(), strlen($directory) + 1);
            $zip->addFile($item->getPathname(), $prefix . '/' . str_replace('\\', '/', $relative));
        }
    }

    private function copyTree(string $source, string $destination): void
    {
        if (!is_dir($source)) {
            return;
        }
        if (!is_dir($destination)) {
            mkdir($destination, 0775, true);
        }
        $items = scandir($source) ?: [];
        foreach ($items as $item) {
            if ($item === '.' || $item === '..') {
                continue;
            }
            $from = $source . DIRECTORY_SEPARATOR . $item;
            $to = $destination . DIRECTORY_SEPARATOR . $item;
            if (is_dir($from)) {
                $this->copyTree($from, $to);
            } else {
                copy($from, $to);
            }
        }
    }

    private function deleteChildren(string $directory, array $suffixes): void
    {
        if (!is_dir($directory)) {
            return;
        }
        $items = scandir($directory) ?: [];
        foreach ($items as $item) {
            if ($item === '.' || $item === '..' || str_ends_with($item, '.txt')) {
                continue;
            }
            $path = $directory . DIRECTORY_SEPARATOR . $item;
            if (is_dir($path)) {
                $this->deleteChildren($path, []);
                @rmdir($path);
                continue;
            }
            if ($suffixes === [] || $this->matchesSuffix($item, $suffixes)) {
                @unlink($path);
            }
        }
    }

    private function matchesSuffix(string $name, array $suffixes): bool
    {
        foreach ($suffixes as $suffix) {
            if (str_ends_with($name, $suffix)) {
                return true;
            }
        }
        return false;
    }

    private function emptyLog(string $path): void
    {
        if (is_file($path)) {
            file_put_contents($path, '');
        }
    }
}
