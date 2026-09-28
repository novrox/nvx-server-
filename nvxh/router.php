<?php
declare(strict_types=1);

$path = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH);
$path = rawurldecode(is_string($path) ? $path : '/');
if ($path === '') {
    $path = '/';
}

$root = dirname(__DIR__);
$localOnly = nvx_router_value($root . DIRECTORY_SEPARATOR . 'config' . DIRECTORY_SEPARATOR . 'security.sys', 'bind_localhost_only');
$allowRemote = nvx_router_value($root . DIRECTORY_SEPARATOR . 'config' . DIRECTORY_SEPARATOR . 'security.sys', 'allow_remote');
$ip = $_SERVER['REMOTE_ADDR'] ?? '';
$local = $ip === '127.0.0.1' || $ip === '::1';
if (!$local && $localOnly !== '0' && $allowRemote !== '1') {
    http_response_code(403);
    echo 'NVX Server accepts connections from this computer only.';
    return true;
}

if ($path === '/router.php' || str_contains($path, '..') || str_contains($path, "\0")) {
    http_response_code(404);
    echo 'Not found.';
    return true;
}

$segments = explode('/', trim($path, '/'));
foreach ($segments as $segment) {
    if ($segment !== '' && $segment[0] === '.' && $segment !== '.well-known') {
        http_response_code(403);
        echo 'Forbidden.';
        return true;
    }
}

if (preg_match('/\.(sqlite(?:-(?:wal|shm|journal))?|db|sys|ini|log|ps1|bat)$/i', $path) === 1) {
    http_response_code(403);
    echo 'Forbidden.';
    return true;
}

if (!str_starts_with($path, '/assets/')) {
    $logDir = $root . DIRECTORY_SEPARATOR . 'logs';
    if (!is_dir($logDir)) {
        @mkdir($logDir, 0775, true);
    }
    $line = '[' . gmdate('Y-m-d H:i:s') . ' UTC] ' . $ip . ' "' . ($_SERVER['REQUEST_METHOD'] ?? 'GET') . ' ' . $path . '"' . PHP_EOL;
    @file_put_contents($logDir . DIRECTORY_SEPARATOR . 'access.log', $line, FILE_APPEND | LOCK_EX);
}

$full = __DIR__ . str_replace('/', DIRECTORY_SEPARATOR, $path);
if ($path !== '/' && is_dir($full)) {
    $index = rtrim($full, '\\/') . DIRECTORY_SEPARATOR . 'index.php';
    if (is_file($index)) {
        header('Location: ' . rtrim($path, '/') . '/index.php');
        return true;
    }
    $htmlIndex = rtrim($full, '\\/') . DIRECTORY_SEPARATOR . 'index.html';
    if (is_file($htmlIndex)) {
        header('Location: ' . rtrim($path, '/') . '/index.html');
        return true;
    }
}

if ($path === '/') {
    header('Location: /index.php');
    return true;
}

if (is_file($full)) {
    return false;
}

http_response_code(404);
echo 'Not found.';
return true;

function nvx_router_value(string $file, string $key): string
{
    if (!is_file($file)) {
        return '';
    }
    $lines = file($file, FILE_IGNORE_NEW_LINES);
    if ($lines === false) {
        return '';
    }
    foreach ($lines as $line) {
        $line = trim($line);
        if ($line === '' || $line[0] === ';' || $line[0] === '#') {
            continue;
        }
        $split = strpos($line, '=');
        if ($split === false || trim(substr($line, 0, $split)) !== $key) {
            continue;
        }
        $value = trim(substr($line, $split + 1));
        $length = strlen($value);
        if ($length >= 2 && $value[0] === '"' && $value[$length - 1] === '"') {
            return substr($value, 1, -1);
        }
        return $value;
    }
    return '';
}
