<?php
declare(strict_types=1);

if (PHP_SAPI === 'cli') {
    fwrite(STDOUT, "Start NVX Server, then open /setup.php in the browser.\r\n");
    fwrite(STDOUT, "Or run: php resources\\engine\\cli.php setup --user=admin --password=your-password\r\n");
    exit(0);
}

if (!isset($nvx) || !$nvx instanceof NvxContext) {
    http_response_code(500);
    echo 'Open setup from the NVX web root.';
    exit;
}

$error = null;
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    try {
        $nvx->installer->apply($_POST);
        $nvx->auth->login();
        nvx_redirect('/index.php');
    } catch (RuntimeException $e) {
        $error = $e->getMessage();
    }
}

$checks = $nvx->installer->requirements();
$timezones = nvx_timezones();
nvx_header($nvx, 'Set up NVX Server', 'overview', 'Create the admin account for this computer. The server stays on 127.0.0.1.', true);
?>
<?php if ($error !== null): ?><div class="flash err"><?= nvx_e($error) ?></div><?php endif; ?>
<ul class="checks">
    <?php foreach ($checks as $check): ?>
        <li class="<?= $check['ok'] ? 'ok' : 'bad' ?>">
            <span><?= nvx_e($check['label']) ?></span>
            <small><?= nvx_e($check['detail']) ?></small>
        </li>
    <?php endforeach; ?>
</ul>
<?php if ($nvx->installer->ready()): ?>
<form method="post" action="/setup.php" class="stack-form">
    <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
    <label>Server name<input name="name" required maxlength="64" value="<?= nvx_e((string) ($_POST['name'] ?? 'NVX Server')) ?>"></label>
    <label>Admin name<input name="user" required maxlength="32" pattern="[A-Za-z][A-Za-z0-9_-]{2,31}" value="<?= nvx_e((string) ($_POST['user'] ?? 'admin')) ?>"></label>
    <label>Password<input type="password" name="password" required minlength="8" autocomplete="new-password"></label>
    <label>Confirm password<input type="password" name="password2" required minlength="8" autocomplete="new-password"></label>
    <div class="pair">
        <label>Web port<input name="http_port" type="number" min="1" max="65535" required value="<?= nvx_e((string) ($_POST['http_port'] ?? $nvx->config->get('ports', 'http', '8088'))) ?>"></label>
        <label>MySQL port<input name="mysql_port" type="number" min="1" max="65535" required value="<?= nvx_e((string) ($_POST['mysql_port'] ?? $nvx->config->get('ports', 'mysql', '3307'))) ?>"></label>
    </div>
    <label>Timezone
        <select name="timezone">
            <?php foreach ($timezones as $timezone): ?>
                <option <?= $timezone === 'UTC' ? 'selected' : '' ?>><?= nvx_e($timezone) ?></option>
            <?php endforeach; ?>
        </select>
    </label>
    <button class="primary" type="submit">Create server</button>
</form>
<?php else: ?>
<p class="empty">Fix the items above, then reload this page.</p>
<?php endif; ?>
<?php nvx_footer(true);
