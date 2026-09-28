<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('databases');

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    $action = (string) ($_POST['action'] ?? '');
    try {
        if ($action === 'create') {
            $nvx->databases->create((string) ($_POST['name'] ?? ''));
            $nvx->auth->flash('ok', 'Database created.');
        } elseif ($action === 'drop') {
            $nvx->databases->drop((string) ($_POST['name'] ?? ''), (string) ($_POST['confirm'] ?? ''));
            $nvx->auth->flash('ok', 'Database deleted.');
        } elseif ($action === 'mysql-create') {
            $nvx->databases->createMysql((string) ($_POST['name'] ?? ''));
            $nvx->auth->flash('ok', 'MySQL database created.');
        } elseif ($action === 'mysql-drop') {
            $nvx->databases->dropMysql((string) ($_POST['name'] ?? ''), (string) ($_POST['confirm'] ?? ''));
            $nvx->auth->flash('ok', 'MySQL database deleted.');
        } else {
            throw new RuntimeException('Unknown action.');
        }
    } catch (Throwable $e) {
        $nvx->auth->flash('err', $e->getMessage());
    }
    nvx_redirect('/databases.php');
}

$databases = $nvx->databases->list();
$mysql = $nvx->databases->mysqlOverview();
nvx_header($nvx, 'Databases', 'databases', 'SQLite databases stored in data\\databases. MySQL appears here when that service is running.');
?>
<section class="split">
    <article class="card">
        <h2>Create a local database</h2>
        <form method="post" action="/databases.php" class="stack-form">
            <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
            <input type="hidden" name="action" value="create">
            <label>Name<input name="name" required maxlength="63" pattern="[A-Za-z][A-Za-z0-9_-]{0,62}" placeholder="notes"></label>
            <button class="primary" type="submit">Create</button>
        </form>
    </article>
    <article class="card">
        <h2>Stored on this computer</h2>
        <p class="muted">A local database is one SQLite file. Open it to add tables and run SQL. Nothing is sent off this machine.</p>
    </article>
</section>

<section class="card">
    <h2>Local databases</h2>
    <?php if ($databases === []): ?>
        <p class="empty">No local databases yet.</p>
    <?php else: ?>
        <div class="table-wrap">
            <table>
                <thead><tr><th>Name</th><th>Tables</th><th>Size</th><th>Updated</th><th></th></tr></thead>
                <tbody>
                <?php foreach ($databases as $database): ?>
                    <tr>
                        <td data-label="Name"><a href="/database.php?name=<?= nvx_e(rawurlencode($database['name'])) ?>"><?= nvx_e($database['name']) ?></a></td>
                        <td data-label="Tables"><?= (int) $database['tables'] ?></td>
                        <td data-label="Size"><?= nvx_e(nvx_bytes($database['bytes'])) ?></td>
                        <td data-label="Updated"><?= nvx_e(gmdate('Y-m-d H:i', $database['updated'])) ?> UTC</td>
                        <td data-label="Delete">
                            <form method="post" action="/databases.php" class="inline">
                                <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
                                <input type="hidden" name="action" value="drop">
                                <input type="hidden" name="name" value="<?= nvx_e($database['name']) ?>">
                                <input name="confirm" placeholder="Type name to delete" aria-label="Confirm delete <?= nvx_e($database['name']) ?>">
                                <button class="danger" type="submit">Delete</button>
                            </form>
                        </td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        </div>
    <?php endif; ?>
</section>

<section class="card">
    <h2>MySQL</h2>
    <?php if (!$mysql['connected']): ?>
        <p class="empty">MySQL is not connected on 127.0.0.1:<?= (int) $nvx->config->get('ports', 'mysql', '3307') ?>. Place mysqld.exe in mysql\bin and start the MySQL service. Local SQLite databases keep working either way.</p>
    <?php else: ?>
        <form method="post" action="/databases.php" class="stack-form">
            <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
            <input type="hidden" name="action" value="mysql-create">
            <label>New MySQL database<input name="name" required maxlength="63" pattern="[A-Za-z][A-Za-z0-9_-]{0,62}"></label>
            <button class="primary" type="submit">Create on MySQL</button>
        </form>
        <div class="table-wrap">
            <table>
                <thead><tr><th>Database</th><th></th></tr></thead>
                <tbody>
                <?php foreach ($mysql['databases'] as $database): ?>
                    <tr>
                        <td data-label="Database"><?= nvx_e($database['name']) ?><?php if ($database['system']): ?> <span class="muted">system</span><?php endif; ?></td>
                        <td data-label="Delete">
                            <?php if (!$database['system']): ?>
                                <form method="post" action="/databases.php" class="inline">
                                    <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
                                    <input type="hidden" name="action" value="mysql-drop">
                                    <input type="hidden" name="name" value="<?= nvx_e($database['name']) ?>">
                                    <input name="confirm" placeholder="Type name to delete" aria-label="Confirm delete <?= nvx_e($database['name']) ?>">
                                    <button class="danger" type="submit">Delete</button>
                                </form>
                            <?php endif; ?>
                        </td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        </div>
    <?php endif; ?>
</section>
<?php nvx_footer();
