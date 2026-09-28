<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('logs');
$channels = [
    'server' => 'Server',
    'error' => 'Errors',
    'access' => 'Access',
    'php' => 'PHP',
    'apache' => 'Apache',
    'mysql' => 'MySQL',
];
$channel = (string) ($_GET['channel'] ?? 'server');
if (!isset($channels[$channel])) {
    $channel = 'server';
}
$log = $nvx->log->tail($channel, 200);
nvx_header($nvx, 'Logs', 'logs', 'Recent lines from the NVX logs.');
?>
<nav class="tabs">
    <?php foreach ($channels as $key => $label): ?>
        <a <?= $key === $channel ? 'aria-current="page"' : '' ?> href="/logs.php?channel=<?= nvx_e($key) ?>"><?= nvx_e($label) ?></a>
    <?php endforeach; ?>
</nav>
<section class="card">
    <pre class="log"><?= nvx_e($log !== '' ? $log : 'This log is empty.') ?></pre>
</section>
<?php nvx_footer();
