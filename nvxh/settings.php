<?php
declare(strict_types=1);

require __DIR__ . '/_init.php';

$nvx = nvx_boot_web('settings');

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    nvx_require_csrf($nvx);
    try {
        $name = trim((string) ($_POST['name'] ?? ''));
        $timezone = trim((string) ($_POST['timezone'] ?? 'UTC'));
        $http = (int) ($_POST['http_port'] ?? 0);
        $mysql = (int) ($_POST['mysql_port'] ?? 0);
        if ($name === '' || strlen($name) > 64) {
            throw new RuntimeException('Enter a server name up to 64 characters.');
        }
        if (!in_array($timezone, timezone_identifiers_list(), true)) {
            throw new RuntimeException('Choose a valid timezone.');
        }
        if ($http < 1 || $http > 65535 || $mysql < 1 || $mysql > 65535) {
            throw new RuntimeException('Ports must be between 1 and 65535.');
        }
        if ($http === $mysql) {
            throw new RuntimeException('The web port and the MySQL port must be different.');
        }

        $nvx->config->update('server', [
            'name' => $name,
            'timezone' => $timezone,
            'bind' => '127.0.0.1',
        ]);
        $nvx->config->update('ports', [
            'http' => (string) $http,
            'mysql' => (string) $mysql,
        ]);

        $note = 'Settings saved. Restart the server so a new port takes effect.';
        if ($http < 1024 || $mysql < 1024) {
            $note .= ' Ports below 1024 may require an administrator.';
        }
        $nvx->auth->flash('ok', $note);
    } catch (Throwable $e) {
        $nvx->auth->flash('err', $e->getMessage());
    }
    nvx_redirect('/settings.php');
}

$timezones = nvx_timezones();
$currentTz = $nvx->config->get('server', 'timezone', 'UTC');
if (!in_array($currentTz, $timezones, true)) {
    array_unshift($timezones, $currentTz);
}
nvx_header($nvx, 'Settings', 'settings', 'This server listens on 127.0.0.1 only. Other computers cannot connect.');
?>
<form method="post" action="/settings.php" class="card stack-form">
    <input type="hidden" name="csrf" value="<?= nvx_e($nvx->auth->csrf()) ?>">
    <label>Server name<input name="name" required maxlength="64" value="<?= nvx_e($nvx->config->get('server', 'name')) ?>"></label>
    <label>Timezone
        <select name="timezone">
            <?php foreach ($timezones as $timezone): ?>
                <option <?= $timezone === $currentTz ? 'selected' : '' ?>><?= nvx_e($timezone) ?></option>
            <?php endforeach; ?>
        </select>
    </label>
    <div class="pair">
        <label>Web port<input name="http_port" required type="number" min="1" max="65535" value="<?= nvx_e($nvx->config->get('ports', 'http', '8088')) ?>"></label>
        <label>MySQL port<input name="mysql_port" required type="number" min="1" max="65535" value="<?= nvx_e($nvx->config->get('ports', 'mysql', '3307')) ?>"></label>
    </div>
    <label>Bind address<input value="127.0.0.1" readonly></label>
    <button class="primary" type="submit">Save settings</button>
</form>
<section class="card">
    <h2>Paths</h2>
    <dl class="kv">
        <div><dt>Web root</dt><dd>nvxh</dd></div>
        <div><dt>Databases</dt><dd>data\databases</dd></div>
        <div><dt>Uploads</dt><dd>data\uploads</dd></div>
        <div><dt>User files</dt><dd>data\user-data</dd></div>
        <div><dt>Backups</dt><dd>data\backups</dd></div>
        <div><dt>Logs</dt><dd>logs</dd></div>
        <div><dt>PHP</dt><dd><?= nvx_e($nvx->services->phpBinary() ?? 'Not found') ?></dd></div>
    </dl>
</section>
<?php nvx_footer();
