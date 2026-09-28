<?php
declare(strict_types=1);

define('NVX_API', true);
require dirname(__DIR__) . '/_init.php';

$nvx = nvx_boot_api();
nvx_json($nvx->services->status());
