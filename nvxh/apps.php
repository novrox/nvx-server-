<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('apps');
$apps = nvx_apps($nvx);
nvx_header($nvx, 'Apps', 'apps', 'Applications in nvxh\\apps are served by this server.');
?>
<section class="card">
    <?php if ($apps === []): ?>
        <p class="empty">No apps yet.</p>
    <?php else: ?>
        <div class="table-wrap">
            <table>
                <thead><tr><th>App</th><th>Address</th></tr></thead>
                <tbody>
                <?php foreach ($apps as $app): ?>
                    <tr>
                        <td data-label="App"><?= nvx_e($app['name']) ?></td>
                        <td data-label="Address"><a href="<?= nvx_e($app['href']) ?>"><?= nvx_e($app['href']) ?></a></td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        </div>
    <?php endif; ?>
</section>
<section class="card">
    <h2>Add an app</h2>
    <p>Create a folder under <span class="mono">nvxh\apps</span> with an <span class="mono">index.php</span> file. Restarting is not required. The app is public on this computer, so keep the server bound to 127.0.0.1.</p>
    <pre class="log">nvxh\apps\my-app\index.php</pre>
</section>
<?php nvx_footer();
