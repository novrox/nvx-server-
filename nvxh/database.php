<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('databases');
$name = (string) ($_GET['name'] ?? $_POST['name'] ?? '');

try {
    $file = $nvx->databases->file($name);
} catch (RuntimeException $e) {
    $nvx->auth->flash('err', $e->getMessage());
    nvx_redirect('/databases.php');
}

if (isset($_GET['download'])) {
    if (!is_file($file)) {
        http_response_code(404);
        echo 'Database not found.';
        exit;
    }
    header('Content-Type: application/octet-stream');
    header('Content-Disposition: attachment; filename="' . $name . '.sqlite"');
    header('Content-Length: ' . (string) filesize($file));
    readfile($file);
    exit;
}

$result = null;
$sql = (string) ($_POST['sql'] ?? '');
$error = null;

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    $action = (string) ($_POST['action'] ?? '');
    try {
        if ($action === 'create-table') {
            $columns = [];
            $posted = $_POST['columns'] ?? [];
            if (is_array($posted)) {
                foreach ($posted as $column) {
                    if (is_array($column)) {
                        $columns[] = $column;
                    }
                }
            }
            $nvx->databases->createTable($name, (string) ($_POST['table'] ?? ''), $columns);
            $nvx->auth->flash('ok', 'Table created.');
            nvx_redirect('/database.php?name=' . rawurlencode($name));
        } elseif ($action === 'drop-table') {
            $nvx->databases->dropTable($name, (string) ($_POST['table'] ?? ''));
            $nvx->auth->flash('ok', 'Table deleted.');
            nvx_redirect('/database.php?name=' . rawurlencode($name));
        } elseif ($action === 'query') {
            $result = $nvx->databases->query($name, $sql);
        } else {
            throw new RuntimeException('Unknown action.');
        }
    } catch (Throwable $e) {
        $error = $e->getMessage();
    }
}

$tables = is_file($file) ? $nvx->databases->tables($name) : [];
$browse = null;
$table = (string) ($_GET['table'] ?? '');
if ($table !== '' && $result === null && $error === null) {
    try {
        $browse = $nvx->databases->browse($name, $table);
    } catch (Throwable $e) {
        $error = $e->getMessage();
    }
}

nvx_header($nvx, $name, 'databases', 'SQLite database in data\\databases\\' . $name . '.sqlite');
?>
<p class="toolbar">
    <a href="/databases.php">All databases</a>
    <a href="/database.php?name=<?= nvx_e(rawurlencode($name)) ?>&amp;download=1">Download</a>
</p>
<?php if ($error !== null): ?><div class="flash err"><?= nvx_e($error) ?></div><?php endif; ?>

<section class="split">
    <article class="card">
        <h2>Tables</h2>
        <?php if ($tables === []): ?>
            <p class="empty">No tables yet.</p>
        <?php else: ?>
            <ul class="plain">
                <?php foreach ($tables as $item): ?>
                    <li>
                        <a href="/database.php?name=<?= nvx_e(rawurlencode($name)) ?>&amp;table=<?= nvx_e(rawurlencode($item)) ?>"><?= nvx_e($item) ?></a>
                        <form method="post" action="/database.php?name=<?= nvx_e(rawurlencode($name)) ?>">
                            <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
                            <input type="hidden" name="action" value="drop-table">
                            <input type="hidden" name="name" value="<?= nvx_e($name) ?>">
                            <input type="hidden" name="table" value="<?= nvx_e($item) ?>">
                            <button class="text" type="submit">Delete</button>
                        </form>
                    </li>
                <?php endforeach; ?>
            </ul>
        <?php endif; ?>
    </article>
    <article class="card">
        <h2>Create a table</h2>
        <form method="post" action="/database.php?name=<?= nvx_e(rawurlencode($name)) ?>" class="stack-form">
            <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
            <input type="hidden" name="action" value="create-table">
            <input type="hidden" name="name" value="<?= nvx_e($name) ?>">
            <label>Table name<input name="table" required pattern="[A-Za-z_][A-Za-z0-9_]{0,62}" placeholder="items"></label>
            <?php for ($i = 0; $i < 4; $i++): ?>
                <div class="pair">
                    <label>Column<input name="columns[<?= $i ?>][name]" placeholder="<?= $i === 0 ? 'title' : 'optional' ?>"></label>
                    <label>Type
                        <select name="columns[<?= $i ?>][type]">
                            <?php foreach (['TEXT', 'INTEGER', 'REAL', 'NUMERIC', 'BLOB'] as $type): ?>
                                <option><?= $type ?></option>
                            <?php endforeach; ?>
                        </select>
                    </label>
                </div>
            <?php endfor; ?>
            <button class="primary" type="submit">Create table</button>
        </form>
    </article>
</section>

<?php if ($browse !== null): ?>
    <section class="card">
        <h2><?= nvx_e($table) ?></h2>
        <?php nvx_rows($browse); ?>
    </section>
<?php endif; ?>

<section class="card">
    <h2>SQL</h2>
    <p class="muted">One statement at a time. ATTACH and load_extension are disabled.</p>
    <form method="post" action="/database.php?name=<?= nvx_e(rawurlencode($name)) ?>" class="stack-form">
        <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
        <input type="hidden" name="action" value="query">
        <input type="hidden" name="name" value="<?= nvx_e($name) ?>">
        <textarea name="sql" rows="6" required><?= nvx_e($sql) ?></textarea>
        <button class="primary" type="submit">Run</button>
    </form>
    <?php if ($result !== null): ?>
        <?php if ($result['kind'] === 'write'): ?>
            <p class="ok-text">Statement completed. Rows affected: <?= (int) $result['affected'] ?>.</p>
        <?php else: ?>
            <?php if ($result['truncated']): ?><p class="muted">Showing the first 100 rows.</p><?php endif; ?>
            <?php nvx_rows($result); ?>
        <?php endif; ?>
    <?php endif; ?>
</section>
<?php
nvx_footer();

function nvx_rows(array $result): void
{
    if ($result['rows'] === []) {
        echo '<p class="empty">No rows.</p>';
        return;
    }
    echo '<div class="table-wrap"><table><thead><tr>';
    foreach ($result['columns'] as $column) {
        echo '<th>' . nvx_e((string) $column) . '</th>';
    }
    echo '</tr></thead><tbody>';
    foreach ($result['rows'] as $row) {
        echo '<tr>';
        foreach ($result['columns'] as $column) {
            $value = $row[$column] ?? null;
            if ($value === null) {
                echo '<td class="muted" data-label="' . nvx_e((string) $column) . '">NULL</td>';
            } else {
                $text = (string) $value;
                $short = strlen($text) > 180 ? substr($text, 0, 180) . '...' : $text;
                echo '<td data-label="' . nvx_e((string) $column) . '">' . nvx_e($short) . '</td>';
            }
        }
        echo '</tr>';
    }
    echo '</tbody></table></div>';
}
