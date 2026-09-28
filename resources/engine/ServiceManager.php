<?php
declare(strict_types=1);

final class ServiceManager
{
    private ?array $statusCache = null;

    public function __construct(private NvxContext $nvx)
    {
    }

    public function status(): array
    {
        if ($this->statusCache !== null) {
            return $this->statusCache;
        }
        $this->statusCache = [
            'name' => $this->nvx->config->get('server', 'name', 'NVX Server'),
            'version' => $this->nvx->config->get('server', 'version', '1.0.0'),
            'url' => $this->url(),
            'bind' => $this->bindAddress(),
            'services' => [
                'php' => $this->describe('php'),
                'apache' => $this->describe('apache'),
                'mysql' => $this->describe('mysql'),
            ],
        ];
        return $this->statusCache;
    }

    public function url(): string
    {
        $meta = $this->readPid('php');
        $port = $this->httpPort();
        if ($meta !== null && $this->alive($meta['pid']) && $meta['port'] > 0) {
            $port = $meta['port'];
        }
        return $port === 80
            ? 'http://localhost/'
            : 'http://localhost:' . $port . '/';
    }

    public function httpPort(): int
    {
        return $this->port('http', 8088);
    }

    public function mysqlPort(): int
    {
        return $this->port('mysql', 3307);
    }

    public function bindAddress(): string
    {
        $bind = $this->nvx->config->get('server', 'bind', '127.0.0.1');
        $localOnly = $this->nvx->config->get('security', 'bind_localhost_only') !== '0'
            && $this->nvx->config->get('security', 'allow_remote') !== '1';
        if ($localOnly && $bind !== '127.0.0.1' && $bind !== '::1') {
            return '127.0.0.1';
        }
        if (filter_var($bind, FILTER_VALIDATE_IP)) {
            return $bind;
        }
        return '127.0.0.1';
    }

    public function phpBinary(): ?string
    {
        $configured = $this->nvx->config->get('paths', 'php_executable');
        $candidates = [
            $configured,
            $this->nvx->path('php/bin/php.exe'),
            $this->nvx->path('php/php.exe'),
        ];
        foreach ($candidates as $candidate) {
            if ($candidate !== '' && is_file($candidate)) {
                return $candidate;
            }
        }
        return null;
    }

    public function assertCan(string $action, string $service): void
    {
        $this->guard($action, $service);
        if ($action === 'stop') {
            return;
        }
        $names = $service === 'all' ? $this->startTargets($service) : [$service];
        foreach ($names as $name) {
            if ($name === 'apache' && $this->binary('apache') === null) {
                throw new RuntimeException('Apache is not installed. Place httpd.exe in apache\\bin.');
            }
            if ($name === 'mysql' && $this->binary('mysql') === null) {
                throw new RuntimeException('MySQL is not installed. Place mysqld.exe in mysql\\bin.');
            }
            if ($name === 'php' && $this->phpBinary() === null) {
                throw new RuntimeException('PHP was not found. Place php.exe in php\\bin.');
            }
        }
    }

    public function schedule(string $action, string $service): void
    {
        $this->assertCan($action, $service);
        $php = $this->phpBinary();
        if ($php === null) {
            throw new RuntimeException('PHP was not found. Place php.exe in php\\bin.');
        }
        // The web page is the PHP server. A separate process applies stop and restart
        // after this request finishes, so the panel can answer before the port closes.
        $this->spawn($php, [
            $this->nvx->path('resources/engine/cli.php'),
            $action,
            $service,
            '--delay=1',
        ], false);
        $this->nvx->log->server('Scheduled ' . $action . ' for ' . $service . '.');
    }

    public function start(string $service): string
    {
        $this->guard('start', $service);
        $notes = [];
        foreach ($this->startTargets($service) as $name) {
            $notes[] = $this->startOne($name);
        }
        if ($notes === []) {
            return 'Nothing was started. Apache and MySQL start when their programs are installed, or when you set them to enabled.';
        }
        return implode(' ', $notes);
    }

    public function stop(string $service): string
    {
        $this->guard('stop', $service);
        $notes = [];
        foreach ($this->explicitTargets($service) as $name) {
            $notes[] = $this->stopOne($name);
        }
        return implode(' ', $notes);
    }

    public function restart(string $service): string
    {
        $this->guard('restart', $service);
        $stopped = $this->stop($service);
        $started = $this->start($service);
        return trim($stopped . ' ' . $started);
    }

    public function failure(): string
    {
        $file = $this->nvx->path('tmp/last-action.txt');
        if (!is_file($file)) {
            return '';
        }
        return trim((string) file_get_contents($file));
    }

    public function noteAction(bool $ok, string $message): void
    {
        $file = $this->nvx->path('tmp/last-action.txt');
        if ($ok) {
            if (is_file($file)) {
                unlink($file);
            }
            return;
        }
        file_put_contents($file, $message);
    }

    private function startOne(string $name): string
    {
        $described = $this->describe($name);
        if ($described['state'] === 'running') {
            return $described['name'] . ' is already running.';
        }
        if ($described['state'] === 'blocked') {
            throw new RuntimeException($described['name'] . ' cannot start because port ' . $described['port'] . ' is already in use.');
        }
        if ($name === 'php') {
            return $this->startPhp();
        }
        if ($name === 'apache') {
            return $this->startApache();
        }
        return $this->startMysql();
    }

    private function startPhp(): string
    {
        $php = $this->phpBinary();
        if ($php === null) {
            throw new RuntimeException('PHP was not found. Place php.exe in php\\bin.');
        }
        if ($this->describe('apache')['state'] === 'running') {
            throw new RuntimeException('Apache is using the HTTP port. Stop Apache before starting the PHP web server.');
        }
        $port = $this->httpPort();
        $bind = $this->bindAddress();
        if ($this->portOpen($bind, $port)) {
            throw new RuntimeException('Port ' . $port . ' is already in use.');
        }
        $ini = $this->writePhpIni($php);
        $this->assertSqlite($php, $ini);
        $web = $this->nvx->path('nvxh');
        $router = $this->nvx->path('nvxh/router.php');
        $pid = $this->spawn($php, ['-c', $ini, '-S', $bind . ':' . $port, '-t', $web, $router]);
        if (!$this->waitForPort($bind, $port, true)) {
            $this->taskkill($pid);
            $detail = trim($this->nvx->log->tail('php', 5));
            throw new RuntimeException('The PHP web server did not start. ' . $detail);
        }
        $listener = $this->listenerPid($port) ?: $pid;
        $this->writePid('php', $listener, $port);
        $this->nvx->log->server('PHP web server started at ' . $this->url() . ' (pid ' . $listener . ').');
        return 'PHP web server started at ' . $this->url();
    }

    private function startApache(): string
    {
        $binary = $this->binary('apache');
        if ($binary === null) {
            throw new RuntimeException('Apache is not installed. Place httpd.exe in apache\\bin.');
        }
        if ($this->describe('php')['state'] === 'running') {
            throw new RuntimeException('The PHP web server is using the HTTP port. Stop it before starting Apache.');
        }
        $port = $this->httpPort();
        $bind = $this->bindAddress();
        if ($this->portOpen($bind, $port)) {
            throw new RuntimeException('Port ' . $port . ' is already in use.');
        }
        $config = $this->writeApacheConfig();
        $pid = $this->spawn($binary, ['-d', $this->nvx->path('apache'), '-f', $config]);
        if (!$this->waitForPort($bind, $port, true)) {
            $this->taskkill($pid);
            $detail = trim($this->nvx->log->tail('apache', 5));
            throw new RuntimeException('Apache did not start. ' . $detail);
        }
        $listener = $this->listenerPid($port) ?: $pid;
        $this->writePid('apache', $listener, $port);
        $this->nvx->log->server('Apache started on port ' . $port . ' (pid ' . $listener . ').');
        return 'Apache started on port ' . $port . '.';
    }

    private function startMysql(): string
    {
        $binary = $this->binary('mysql');
        if ($binary === null) {
            throw new RuntimeException('MySQL is not installed. Place mysqld.exe in mysql\\bin.');
        }
        $port = $this->mysqlPort();
        $bind = $this->bindAddress();
        if ($this->portOpen($bind, $port)) {
            throw new RuntimeException('MySQL port ' . $port . ' is already in use.');
        }
        $config = $this->writeMysqlConfig();
        $datadir = $this->nvx->path('mysql/data');
        if (!is_dir($datadir . DIRECTORY_SEPARATOR . 'mysql')) {
            $this->nvx->log->server('Initializing the MySQL data directory.');
            $this->runWait($binary, ['--defaults-file=' . $config, '--initialize-insecure', '--datadir=' . $datadir]);
        }
        $pid = $this->spawn($binary, ['--defaults-file=' . $config, '--console']);
        if (!$this->waitForPort($bind, $port, true)) {
            $this->taskkill($pid);
            $detail = trim($this->nvx->log->tail('mysql', 5));
            throw new RuntimeException('MySQL did not start. ' . $detail);
        }
        $listener = $this->listenerPid($port) ?: $pid;
        $this->writePid('mysql', $listener, $port);
        $this->nvx->log->server('MySQL started on port ' . $port . ' (pid ' . $listener . ').');
        return 'MySQL started on port ' . $port . '.';
    }

    private function stopOne(string $name): string
    {
        $meta = $this->readPid($name);
        $label = $this->serviceName($name);
        if ($meta === null || !$this->alive($meta['pid'])) {
            $this->clearPid($name);
            return $label . ' is already stopped.';
        }
        if ($meta['pid'] === getmypid()) {
            throw new RuntimeException('Refusing to stop the control process.');
        }
        $this->taskkill($meta['pid']);
        $port = $meta['port'] > 0 ? $meta['port'] : ($name === 'mysql' ? $this->mysqlPort() : $this->httpPort());
        $this->waitForPort($this->bindAddress(), $port, false);
        $this->clearPid($name);
        $this->nvx->log->server($label . ' stopped.');
        return $label . ' stopped.';
    }

    private function describe(string $name): array
    {
        $port = $name === 'mysql' ? $this->mysqlPort() : $this->httpPort();
        $meta = $this->readPid($name);
        $alive = $meta !== null && $this->alive($meta['pid']);
        if ($meta !== null && !$alive) {
            $this->clearPid($name);
            $meta = null;
        }
        $listenPort = $alive && $meta !== null && $meta['port'] > 0 ? $meta['port'] : $port;
        $binaryReady = $name === 'php' ? $this->phpBinary() !== null : $this->binary($name) !== null;
        $mode = $this->nvx->config->get('services', $name, 'auto');

        if ($alive) {
            $state = 'running';
        } elseif (!$binaryReady && $mode !== 'enabled') {
            $state = 'missing';
        } elseif ($this->portOpen($this->bindAddress(), $listenPort)) {
            $state = 'blocked';
        } elseif ($mode === 'disabled') {
            $state = 'disabled';
        } elseif (!$binaryReady) {
            $state = 'missing';
        } else {
            $state = 'stopped';
        }

        $detail = match ($name) {
            'php' => 'Serves the nvxh web root',
            'apache' => $binaryReady ? 'HTTP server in apache\\bin' : 'Place httpd.exe in apache\\bin',
            default => $binaryReady ? 'Database server in mysql\\bin' : 'Place mysqld.exe in mysql\\bin',
        };
        if ($state === 'blocked') {
            $php = $name === 'php' ? null : $this->readPid('php');
            $heldByPhp = $php !== null && $this->alive($php['pid']) && $php['port'] === $listenPort;
            $detail = $heldByPhp
                ? 'The PHP web server is using this port.'
                : 'Port ' . $listenPort . ' is already in use.';
        }
        if ($state === 'running' && $alive && $meta !== null && $meta['port'] !== $port) {
            $detail = 'Restart to use port ' . $port . '.';
        }

        return [
            'id' => $name,
            'name' => $this->serviceName($name),
            'summary' => match ($name) {
                'php' => 'Built-in web server',
                'apache' => 'HTTP server',
                default => 'Database server',
            },
            'state' => $state,
            'label' => $this->label($state),
            'port' => $state === 'running' ? $listenPort : $port,
            'detail' => $detail,
            'pid' => $alive && $meta !== null ? $meta['pid'] : 0,
            'uptime' => $state === 'running' && $meta !== null ? max(0, time() - $meta['started']) : 0,
            'mode' => $mode,
        ];
    }

    private function startTargets(string $service): array
    {
        if ($service !== 'all') {
            return [$service];
        }
        $targets = [];
        if ($this->nvx->config->get('services', 'php', 'enabled') !== 'disabled') {
            $targets[] = 'php';
        }
        if ($this->wants('apache')) {
            $targets[] = 'apache';
        }
        if ($this->wants('mysql')) {
            $targets[] = 'mysql';
        }
        if (in_array('apache', $targets, true) && in_array('php', $targets, true)) {
            $targets = array_values(array_filter($targets, static fn (string $name): bool => $name !== 'php'));
        }
        return $targets;
    }

    private function wants(string $name): bool
    {
        $mode = $this->nvx->config->get('services', $name, 'auto');
        if ($mode === 'disabled') {
            return false;
        }
        if ($name === 'apache' && $mode !== 'enabled') {
            return false;
        }
        if ($mode === 'enabled') {
            return true;
        }
        return $this->binary($name) !== null;
    }

    private function explicitTargets(string $service): array
    {
        if ($service === 'all') {
            return ['php', 'apache', 'mysql'];
        }
        return [$service];
    }

    private function guard(string $action, string $service): void
    {
        if (!in_array($action, ['start', 'stop', 'restart'], true)) {
            throw new RuntimeException('Unknown action.');
        }
        if (!in_array($service, ['all', 'php', 'apache', 'mysql'], true)) {
            throw new RuntimeException('Unknown service.');
        }
    }

    private function binary(string $name): ?string
    {
        $key = $name === 'apache' ? 'apache' : 'mysql';
        $relative = $this->nvx->config->get($key, 'executable');
        if ($relative === '') {
            $relative = $name === 'apache' ? 'apache/bin/httpd.exe' : 'mysql/bin/mysqld.exe';
        }
        $path = $this->resolve($relative);
        return is_file($path) ? $path : null;
    }

    private function resolve(string $path): string
    {
        if (preg_match('/^[A-Za-z]:[\\\\\\/]/', $path) === 1) {
            return $path;
        }
        return $this->nvx->path($path);
    }

    private function writePhpIni(string $php): string
    {
        $extensionDir = $this->extensionDir($php);
        $lines = [];
        $preferences = $this->nvx->path('php/conf/php.ini');
        if (is_file($preferences)) {
            $lines[] = (string) file_get_contents($preferences);
        }
        $lines[] = 'extension_dir = "' . $this->slash($extensionDir) . '"';
        foreach ([
            'pdo_sqlite' => 'php_pdo_sqlite.dll',
            'sqlite3' => 'php_sqlite3.dll',
            'pdo_mysql' => 'php_pdo_mysql.dll',
            'mysqli' => 'php_mysqli.dll',
            'openssl' => 'php_openssl.dll',
            'mbstring' => 'php_mbstring.dll',
            'zip' => 'php_zip.dll',
        ] as $extension => $dll) {
            if (is_file($extensionDir . DIRECTORY_SEPARATOR . $dll)) {
                $lines[] = 'extension=' . $extension;
            }
        }
        $lines[] = 'log_errors = On';
        $lines[] = 'error_log = "' . $this->slash($this->nvx->path('php/logs/error.log')) . '"';
        $lines[] = 'session.save_path = "' . $this->slash($this->nvx->path('tmp/sessions')) . '"';
        $lines[] = 'sys_temp_dir = "' . $this->slash($this->nvx->path('tmp')) . '"';
        $lines[] = 'upload_tmp_dir = "' . $this->slash($this->nvx->path('tmp')) . '"';
        $timezone = $this->nvx->config->get('server', 'timezone', 'UTC');
        $lines[] = 'date.timezone = "' . (in_array($timezone, timezone_identifiers_list(), true) ? $timezone : 'UTC') . '"';
        $path = $this->nvx->path('tmp/php-server.ini');
        file_put_contents($path, implode("\r\n", $lines) . "\r\n");
        return $path;
    }

    private function assertSqlite(string $php, string $ini): void
    {
        $output = shell_exec(escapeshellarg($php) . ' -c ' . escapeshellarg($ini) . ' -m');
        if (!is_string($output) || !preg_match('/^pdo_sqlite$/mi', $output)) {
            throw new RuntimeException('PDO SQLite did not load. Check php\\conf\\php.ini and the ext folder next to php.exe.');
        }
    }

    private function extensionDir(string $php): string
    {
        $candidates = [
            dirname($php) . DIRECTORY_SEPARATOR . 'ext',
            dirname($php, 2) . DIRECTORY_SEPARATOR . 'ext',
            $this->nvx->path('php/ext'),
        ];
        foreach ($candidates as $candidate) {
            if (is_dir($candidate)) {
                return $candidate;
            }
        }
        return dirname($php) . DIRECTORY_SEPARATOR . 'ext';
    }

    private function writeApacheConfig(): string
    {
        $template = $this->nvx->path('apache/conf/httpd.conf.template');
        if (!is_file($template)) {
            throw new RuntimeException('Missing apache\\conf\\httpd.conf.template.');
        }
        $root = $this->slash($this->nvx->root);
        $text = strtr((string) file_get_contents($template), [
            '__ROOT__' => $root,
            '__BIND__' => $this->bindAddress(),
            '__PORT__' => (string) $this->httpPort(),
        ]);
        $modules = '';
        $moduleDir = $this->nvx->path('apache/modules');
        $known = [
            'access_compat_module' => 'mod_access_compat.so',
            'authz_core_module' => 'mod_authz_core.so',
            'dir_module' => 'mod_dir.so',
            'log_config_module' => 'mod_log_config.so',
            'mime_module' => 'mod_mime.so',
            'rewrite_module' => 'mod_rewrite.so',
        ];
        foreach ($known as $module => $file) {
            if (is_file($moduleDir . DIRECTORY_SEPARATOR . $file)) {
                $modules .= 'LoadModule ' . $module . ' modules/' . $file . "\r\n";
            }
        }
        $phpModule = $this->nvx->path('php/php8apache2_4.dll');
        if (is_file($phpModule)) {
            $modules .= 'LoadModule php_module "' . $this->slash($phpModule) . "\"\r\n";
            $modules .= "AddType application/x-httpd-php .php\r\n";
            $modules .= 'PHPIniDir "' . $this->slash($this->nvx->path('php/conf')) . "\"\r\n";
        }
        $text = str_replace('__MODULES__', $modules, $text);
        $path = $this->nvx->path('apache/conf/httpd.conf');
        file_put_contents($path, $text);
        return $path;
    }

    private function writeMysqlConfig(): string
    {
        $template = $this->nvx->path('mysql/conf/my.ini.template');
        if (!is_file($template)) {
            throw new RuntimeException('Missing mysql\\conf\\my.ini.template.');
        }
        $root = $this->slash($this->nvx->root);
        $text = strtr((string) file_get_contents($template), [
            '__ROOT__' => $root,
            '__BIND__' => $this->bindAddress(),
            '__PORT__' => (string) $this->mysqlPort(),
        ]);
        $path = $this->nvx->path('mysql/conf/my.ini');
        file_put_contents($path, $text);
        return $path;
    }

    private function spawn(string $file, array $arguments): int
    {
        $quotedArgs = [];
        foreach ($arguments as $argument) {
            $quotedArgs[] = $this->psQuote((string) $argument);
        }
        // Redirecting Start-Process output waits until that process exits, which never
        // happens for a server. Launch it hidden and return the process id immediately.
        $script = '$p = Start-Process -FilePath ' . $this->psQuote($file)
            . ' -ArgumentList @(' . implode(',', $quotedArgs) . ')'
            . ' -WorkingDirectory ' . $this->psQuote($this->nvx->root)
            . " -WindowStyle Hidden -PassThru\r\nWrite-Output \$p.Id\r\n";
        $output = $this->powershell($script);
        $pid = $this->parsePid($output);
        if ($pid < 10) {
            throw new RuntimeException('Could not start the process. ' . trim(substr($output, 0, 400)));
        }
        return $pid;
    }

    private function runWait(string $file, array $arguments): void
    {
        $parts = [escapeshellarg($file)];
        foreach ($arguments as $argument) {
            $parts[] = escapeshellarg((string) $argument);
        }
        $output = shell_exec(implode(' ', $parts) . ' 2>&1');
        if ($output !== null && trim($output) !== '') {
            $this->nvx->log->server(trim($output));
        }
    }

    private function taskkill(int $pid): void
    {
        if ($pid < 10 || $pid === getmypid()) {
            return;
        }
        shell_exec('taskkill /PID ' . $pid . ' /F /T 2>&1');
    }

    private function listenerPid(int $port): int
    {
        $output = shell_exec('netstat -ano -p TCP');
        if (!is_string($output)) {
            return 0;
        }
        $pattern = '/:' . $port . '\s+0\.0\.0\.0:0\s+\S+\s+(\d+)\s*$/';
        foreach (preg_split("/\r\n|\n/", $output) ?: [] as $line) {
            if (preg_match($pattern, trim($line), $matches) === 1) {
                return (int) $matches[1];
            }
        }
        return 0;
    }

    private function powershell(string $script): string
    {
        $file = $this->nvx->path('tmp/run-' . bin2hex(random_bytes(4)) . '.ps1');
        file_put_contents($file, $script);
        $command = 'powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File '
            . escapeshellarg($file) . ' 2>&1';
        $output = shell_exec($command);
        @unlink($file);
        return is_string($output) ? $output : '';
    }

    private function psQuote(string $value): string
    {
        return "'" . str_replace("'", "''", $value) . "'";
    }

    private function parsePid(string $output): int
    {
        if (preg_match('/(\d+)\s*$/', trim($output), $matches) === 1) {
            return (int) $matches[1];
        }
        return 0;
    }

    private function waitForPort(string $bind, int $port, bool $open): bool
    {
        for ($attempt = 0; $attempt < 20; $attempt++) {
            $isOpen = $this->portOpen($bind, $port);
            if ($isOpen === $open) {
                return true;
            }
            usleep(250000);
        }
        return $this->portOpen($bind, $port) === $open;
    }

    private function portOpen(string $bind, int $port): bool
    {
        $serverPort = (int) ($_SERVER['SERVER_PORT'] ?? 0);
        if (PHP_SAPI === 'cli-server' && $serverPort === $port) {
            return true;
        }
        $output = shell_exec('netstat -ano -p TCP');
        if (!is_string($output)) {
            return false;
        }
        $address = ($bind === '0.0.0.0' ? '0.0.0.0' : $bind) . ':' . $port;
        $quoted = preg_quote($address, '/');
        return preg_match('/' . $quoted . '\s+0\.0\.0\.0:0\s+/', $output) === 1
            || preg_match('/' . $quoted . '\s+\[::\]:0\s+/', $output) === 1;
    }

    private function alive(int $pid): bool
    {
        if ($pid < 10) {
            return false;
        }
        $output = shell_exec('tasklist /FI "PID eq ' . $pid . '" /FO CSV /NH');
        return is_string($output) && str_contains($output, '"' . $pid . '"');
    }

    private function writePid(string $service, int $pid, int $port): void
    {
        file_put_contents($this->nvx->path('tmp/pids/' . $service . '.json'), json_encode([
            'pid' => $pid,
            'port' => $port,
            'started' => time(),
        ]));
    }

    private function readPid(string $service): ?array
    {
        $file = $this->nvx->path('tmp/pids/' . $service . '.json');
        if (!is_file($file)) {
            return null;
        }
        $data = json_decode((string) file_get_contents($file), true);
        if (!is_array($data) || !isset($data['pid'])) {
            return null;
        }
        return [
            'pid' => (int) $data['pid'],
            'port' => (int) ($data['port'] ?? 0),
            'started' => (int) ($data['started'] ?? time()),
        ];
    }

    private function clearPid(string $service): void
    {
        $file = $this->nvx->path('tmp/pids/' . $service . '.json');
        if (is_file($file)) {
            unlink($file);
        }
    }

    private function port(string $key, int $fallback): int
    {
        $value = $this->nvx->config->get('ports', $key, (string) $fallback);
        if (preg_match('/^\d+$/', $value) !== 1) {
            return $fallback;
        }
        $port = (int) $value;
        return ($port >= 1 && $port <= 65535) ? $port : $fallback;
    }

    private function serviceName(string $name): string
    {
        return match ($name) {
            'php' => 'PHP',
            'apache' => 'Apache',
            default => 'MySQL',
        };
    }

    private function label(string $state): string
    {
        return match ($state) {
            'running' => 'Running',
            'stopped' => 'Stopped',
            'missing' => 'Not installed',
            'blocked' => 'Port in use',
            'disabled' => 'Disabled',
            default => 'Unknown',
        };
    }

    private function slash(string $path): string
    {
        return str_replace('\\', '/', $path);
    }
}
