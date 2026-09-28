<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('services');

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    $action = (string) ($_POST['action'] ?? '');
    $service = (string) ($_POST['service'] ?? 'all');
    $return = (string) ($_POST['return'] ?? '/services.php');
    if (!in_array($return, ['/index.php', '/services.php'], true)) {
        $return = '/services.php';
    }
    try {
        $nvx->services->schedule($action, $service);
        $cuttingWeb = ($action === 'stop' || $action === 'restart') && ($service === 'php' || $service === 'all');
        $message = $cuttingWeb
            ? 'The web server will pause for a moment. If this page disconnects, open it again from the same address, or run scripts\\start.bat.'
            : 'That change is being applied. Status refreshes on this page.';
        $nvx->auth->flash('ok', $message);
    } catch (RuntimeException $e) {
        $nvx->auth->flash('err', $e->getMessage());
    }
    nvx_redirect($return);
}

$status = $nvx->services->status();
nvx_header($nvx, 'Services', 'services', 'Start and stop the web server, Apache, and MySQL.');
?>
<section class="stack">
    <?php foreach ($status['services'] as $service): ?>
        <article class="card service-row">
            <div>
                <div class="card-top">
                    <h2><?= nvx_e($service['name']) ?></h2>
                    <span class="lamp <?= $service['state'] === 'running' ? 'on' : ($service['state'] === 'blocked' ? 'busy' : 'off') ?>" data-lamp="<?= nvx_e($service['id']) ?>"></span>
                </div>
                <p class="state" data-state="<?= nvx_e($service['id']) ?>"><?= nvx_e($service['label']) ?></p>
                <p class="detail" data-detail="<?= nvx_e($service['id']) ?>">Port <?= (int) $service['port'] ?> · <?= nvx_e($service['detail']) ?></p>
                <p class="muted">Startup mode: <?= nvx_e($service['mode']) ?><?php if ($service['pid'] > 0): ?> · pid <?= (int) $service['pid'] ?><?php endif; ?></p>
            </div>
            <?php nvx_service_actions($nvx, $service['id'], '/services.php'); ?>
        </article>
    <?php endforeach; ?>
</section>
<section class="card">
    <h2>All services</h2>
    <p class="muted">PHP serves nvxh until you set Apache to enabled in config\services.sys and place httpd.exe in apache\bin. MySQL starts on its own when mysqld.exe is in mysql\bin and its mode is auto.</p>
    <?php nvx_service_actions($nvx, 'all', '/services.php'); ?>
</section>
<?php nvx_footer();
