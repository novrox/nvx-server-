<?php
declare(strict_types=1);

define('NVX_API', true);
require dirname(__DIR__) . '/_init.php';

$nvx = nvx_boot_api();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    nvx_json(['error' => 'POST required'], 405);
}
nvx_require_csrf($nvx);

try {
    $nvx->services->schedule((string) ($_POST['action'] ?? ''), (string) ($_POST['service'] ?? 'all'));
    nvx_json(['ok' => true]);
} catch (RuntimeException $e) {
    nvx_json(['error' => $e->getMessage()], 400);
}
