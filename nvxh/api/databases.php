<?php
declare(strict_types=1);

define('NVX_API', true);
require dirname(__DIR__) . '/_init.php';

$nvx = nvx_boot_api();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    try {
        $action = (string) ($_POST['action'] ?? 'create');
        if ($action === 'drop') {
            $nvx->databases->drop((string) ($_POST['name'] ?? ''), (string) ($_POST['confirm'] ?? ''));
        } else {
            $nvx->databases->create((string) ($_POST['name'] ?? ''));
        }
    } catch (Throwable $e) {
        nvx_json(['error' => $e->getMessage()], 400);
    }
}

nvx_json(['databases' => $nvx->databases->list()]);
