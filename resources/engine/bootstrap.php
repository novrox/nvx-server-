<?php
declare(strict_types=1);

define('NVX_ROOT', dirname(__DIR__, 2));

require __DIR__ . '/Config.php';
require __DIR__ . '/Logger.php';
require __DIR__ . '/Auth.php';
require __DIR__ . '/ServiceManager.php';
require __DIR__ . '/DatabaseManager.php';
require __DIR__ . '/Installer.php';
require __DIR__ . '/view.php';

set_exception_handler(static function (Throwable $e): void {
    try {
        if (defined('NVX_ROOT')) {
            (new Logger(NVX_ROOT))->error($e->getMessage());
        }
    } catch (Throwable $ignore) {
    }

    if (PHP_SAPI === 'cli') {
        fwrite(STDERR, $e->getMessage() . PHP_EOL);
        exit(1);
    }

    http_response_code(500);
    if (defined('NVX_API')) {
        header('Content-Type: application/json; charset=utf-8');
        echo json_encode(['error' => 'Server error']);
        exit;
    }

    echo 'NVX Server could not complete that request. The details are in logs/error.log.';
    exit;
});

function nvx(): NvxContext
{
    static $context = null;
    if ($context === null) {
        $context = new NvxContext(NVX_ROOT);
    }
    return $context;
}

function nvx_boot_web(string $page): NvxContext
{
    $nvx = nvx();
    header('Cache-Control: no-store');
    header('X-Content-Type-Options: nosniff');
    header('X-Frame-Options: DENY');
    header('Referrer-Policy: same-origin');
    header("Content-Security-Policy: default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'");

    $nvx->auth->assertLocalPage();
    $nvx->auth->startSession();
    if ($page === 'login' || $page === 'setup') {
        nvx_redirect('/index.php');
    }

    return $nvx;
}

function nvx_boot_api(): NvxContext
{
    $nvx = nvx();
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    header('X-Content-Type-Options: nosniff');

    if (!$nvx->auth->isLocal() && $nvx->config->get('security', 'bind_localhost_only') !== '0') {
        nvx_json(['error' => 'NVX Server accepts connections from this computer only.'], 403);
    }

    $nvx->auth->startSession();
    return $nvx;
}

function nvx_redirect(string $path): void
{
    header('Location: ' . $path);
    exit;
}

function nvx_json(array $payload, int $code = 200): void
{
    http_response_code($code);
    if (!headers_sent()) {
        header('Content-Type: application/json; charset=utf-8');
    }
    echo json_encode($payload, JSON_UNESCAPED_SLASHES);
    exit;
}

function nvx_require_csrf(NvxContext $nvx): void
{
    $token = $_POST['csrf'] ?? ($_SERVER['HTTP_X_CSRF_TOKEN'] ?? '');
    if (!$nvx->auth->verifyCsrf(is_string($token) ? $token : '')) {
        if (defined('NVX_API')) {
            nvx_json(['error' => 'Invalid request token.'], 400);
        }
        http_response_code(400);
        echo 'Invalid request token. Reload the page and try again.';
        exit;
    }
}

final class NvxContext
{
    public Config $config;
    public Logger $log;
    public Auth $auth;
    public ServiceManager $services;
    public DatabaseManager $databases;
    public Installer $installer;

    public function __construct(public string $root)
    {
        $this->root = rtrim($this->root, "\\/");
        $this->config = new Config($this->root);
        $this->log = new Logger($this->root);
        $this->prepare();
        $this->auth = new Auth($this);
        $this->services = new ServiceManager($this);
        $this->databases = new DatabaseManager($this);
        $this->installer = new Installer($this);

        $timezone = $this->config->get('server', 'timezone', 'UTC');
        date_default_timezone_set(in_array($timezone, timezone_identifiers_list(), true) ? $timezone : 'UTC');
    }

    public function path(string $relative): string
    {
        $relative = str_replace(['/', '\\'], DIRECTORY_SEPARATOR, $relative);
        return $this->root . DIRECTORY_SEPARATOR . ltrim($relative, DIRECTORY_SEPARATOR);
    }

    private function prepare(): void
    {
        $directories = [
            'data/databases',
            'data/user-data',
            'data/uploads',
            'data/backups',
            'logs',
            'tmp/sessions',
            'tmp/pids',
            'apache/logs',
            'mysql/logs',
            'php/logs',
            'nvxh/apps',
        ];
        foreach ($directories as $directory) {
            $path = $this->path($directory);
            if (!is_dir($path) && !mkdir($path, 0775, true) && !is_dir($path)) {
                throw new RuntimeException('Could not create ' . $directory);
            }
        }
    }
}
