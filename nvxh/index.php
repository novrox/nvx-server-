<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('overview');
$status = $nvx->services->status();
$databases = $nvx->databases->list();
$apps = nvx_apps($nvx);
$log = $nvx->log->tail('server', 8);

nvx_header($nvx, 'Overview', 'overview', 'Local web server, services, and databases on this computer.');
?>
<section class="strip">
    <div>
        <span class="kicker">Address</span>
        <a class="url" href="<?= nvx_e($status['url']) ?>"><?= nvx_e($status['url']) ?></a>
    </div>
    <div>
        <span class="kicker">Web uptime</span>
        <strong data-uptime="php"><?= nvx_e($status['services']['php']['state'] === 'running' ? nvx_uptime($status['services']['php']['uptime']) : 'Down') ?></strong>
    </div>
    <div>
        <span class="kicker">Databases</span>
        <strong><?= count($databases) ?></strong>
    </div>
    <div>
        <span class="kicker">Apps</span>
        <strong><?= count($apps) ?></strong>
    </div>
</section>

<section class="cards">
    <?php foreach ($status['services'] as $service): ?>
        <article class="card">
            <div class="card-top">
                <h2><?= nvx_e($service['name']) ?></h2>
                <span class="lamp <?= $service['state'] === 'running' ? 'on' : ($service['state'] === 'blocked' ? 'busy' : 'off') ?>" data-lamp="<?= nvx_e($service['id']) ?>"></span>
            </div>
            <p class="muted"><?= nvx_e($service['summary']) ?></p>
            <p class="state" data-state="<?= nvx_e($service['id']) ?>"><?= nvx_e($service['label']) ?></p>
            <p class="detail" data-detail="<?= nvx_e($service['id']) ?>">Port <?= (int) $service['port'] ?> · <?= nvx_e($service['detail']) ?></p>
        </article>
    <?php endforeach; ?>
</section>

<?php nvx_service_actions($nvx, 'all', '/index.php'); ?>

<section class="split">
    <article class="card">
        <div class="card-top">
            <h2>Local databases</h2>
            <a href="/databases.php">Manage</a>
        </div>
        <?php if ($databases === []): ?>
            <p class="empty">No databases yet. Create one from the Databases page. Each database is a SQLite file in data\databases.</p>
        <?php else: ?>
            <ul class="plain">
                <?php foreach (array_slice($databases, 0, 6) as $database): ?>
                    <li>
                        <a href="/database.php?name=<?= nvx_e(rawurlencode($database['name'])) ?>"><?= nvx_e($database['name']) ?></a>
                        <span class="muted"><?= (int) $database['tables'] ?> tables · <?= nvx_e(nvx_bytes($database['bytes'])) ?></span>
                    </li>
                <?php endforeach; ?>
            </ul>
        <?php endif; ?>
    </article>
    <article class="card">
        <div class="card-top">
            <h2>Apps</h2>
            <a href="/apps.php">View</a>
        </div>
        <?php if ($apps === []): ?>
            <p class="empty">Put an application in nvxh\apps\your-app\index.php and it will be served from this server.</p>
        <?php else: ?>
            <ul class="plain">
                <?php foreach ($apps as $app): ?>
                    <li><a href="<?= nvx_e($app['href']) ?>"><?= nvx_e($app['name']) ?></a><span class="muted">/apps/<?= nvx_e($app['name']) ?>/</span></li>
                <?php endforeach; ?>
            </ul>
        <?php endif; ?>
    </article>
</section>

<section class="card">
    <div class="card-top">
        <h2>Server log</h2>
        <a href="/logs.php?channel=server">Open logs</a>
    </div>
    <pre class="log"><?= nvx_e($log !== '' ? $log : 'No server events yet.') ?></pre>
</section>
<?php nvx_footer();
