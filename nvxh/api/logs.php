<?php
declare(strict_types=1);

define('NVX_API', true);
require dirname(__DIR__) . '/_init.php';

$nvx = nvx_boot_api();
$channel = (string) ($_GET['channel'] ?? 'server');
if ($nvx->log->channel($channel) === null) {
    nvx_json(['error' => 'Unknown log'], 404);
}
nvx_json([
    'channel' => $channel,
    'text' => $nvx->log->tail($channel, 200),
]);
