<?php
declare(strict_types=1);

function nvx_e(mixed $value): string
{
    return htmlspecialchars((string) $value, ENT_QUOTES, 'UTF-8');
}

function nvx_bytes(int $bytes): string
{
    if ($bytes < 1024) {
        return $bytes . ' B';
    }
    if ($bytes < 1048576) {
        return number_format($bytes / 1024, 1) . ' KB';
    }
    return number_format($bytes / 1048576, 1) . ' MB';
}

function nvx_uptime(int $seconds): string
{
    if ($seconds < 60) {
        return $seconds . 's';
    }
    $minutes = intdiv($seconds, 60);
    if ($minutes < 60) {
        return $minutes . 'm';
    }
    return intdiv($minutes, 60) . 'h ' . ($minutes % 60) . 'm';
}

function nvx_timezones(): array
{
    return [
        'UTC',
        'America/Los_Angeles',
        'America/Denver',
        'America/Chicago',
        'America/New_York',
        'Europe/London',
        'Europe/Paris',
        'Asia/Tokyo',
        'Asia/Shanghai',
        'Australia/Sydney',
    ];
}

function nvx_apps(NvxContext $nvx): array
{
    $directory = $nvx->path('nvxh/apps');
    $apps = [];
    foreach (scandir($directory) ?: [] as $item) {
        if ($item === '.' || $item === '..' || str_starts_with($item, '.')) {
            continue;
        }
        $index = $directory . DIRECTORY_SEPARATOR . $item . DIRECTORY_SEPARATOR . 'index.php';
        $htmlIndex = $directory . DIRECTORY_SEPARATOR . $item . DIRECTORY_SEPARATOR . 'index.html';
        if (is_file($index) || is_file($htmlIndex)) {
            $apps[] = [
                'name' => $item,
                'href' => '/apps/' . rawurlencode($item) . '/',
            ];
        }
    }
    usort($apps, static fn (array $a, array $b): int => strcasecmp($a['name'], $b['name']));
    return $apps;
}

function nvx_header(NvxContext $nvx, string $title, string $active, string $lead = '', bool $bare = false): void
{
    $name = $nvx->config->get('server', 'name', 'NVX Server');
    $version = $nvx->config->get('server', 'version', '1.0.0');
    $lang = 'en';
    $localeFile = $nvx->path('resources/locale/en.sys');
    if (is_file($localeFile)) {
        $locale = $nvx->config->readSections('resources/locale/en.sys');
        if (($locale['default']['code'] ?? '') !== '') {
            $lang = $locale['default']['code'];
        }
    }
    $nav = [
        'overview' => ['Overview', '/index.php'],
        'services' => ['Services', '/services.php'],
        'databases' => ['Databases', '/databases.php'],
        'apps' => ['Apps', '/apps.php'],
        'logs' => ['Logs', '/logs.php'],
        'settings' => ['Settings', '/settings.php'],
    ];
    echo '<!DOCTYPE html><html lang="' . nvx_e($lang) . '"><head><meta charset="utf-8">';
    echo '<meta name="viewport" content="width=device-width, initial-scale=1">';
    echo '<meta name="robots" content="noindex">';
    echo '<title>' . nvx_e($title) . ' · ' . nvx_e($name) . '</title>';
    echo '<link rel="icon" type="image/png" href="/assets/img/nvx-ico.png">';
    echo '<link rel="stylesheet" href="/assets/css/nvx.css?v=3">';
    echo '</head>';
    if ($bare) {
        echo '<body class="bare-body"><main class="panel">';
        echo '<a class="brand" href="/index.php"><img src="/assets/img/nvx-ico.png" alt=""><span><strong>NVX</strong><small>SERVER</small></span></a>';
        echo '<h1>' . nvx_e($title) . '</h1>';
        if ($lead !== '') {
            echo '<p class="lead">' . nvx_e($lead) . '</p>';
        }
        nvx_flash($nvx);
        return;
    }

    $php = $nvx->services->status()['services']['php'];
    echo '<body data-poll="status"><div class="shell"><aside class="side">';
    echo '<a class="brand" href="/index.php"><img src="/assets/img/nvx-ico.png" alt=""><span><strong>NVX</strong><small>SERVER</small></span></a><nav>';
    foreach ($nav as $key => $item) {
        $class = $key === $active ? ' class="active"' : '';
        $current = $key === $active ? ' aria-current="page"' : '';
        echo '<a' . $class . $current . ' href="' . nvx_e($item[1]) . '">' . nvx_e($item[0]) . '</a>';
    }
    echo '</nav><div class="side-foot">';
    echo '<span class="lamp ' . ($php['state'] === 'running' ? 'on' : 'off') . '" data-lamp="php"></span>';
    echo '<small class="connection" data-connection aria-live="polite">Connecting...</small>';
    echo '<span data-url>' . nvx_e($nvx->services->url()) . '</span>';
    echo '<small>v' . nvx_e($version) . '</small><a class="credit" href="mailto:hello@novrox.com">Created by Novrox · hello@novrox.com</a></div></aside><div class="content">';
    nvx_flash($nvx);
    $failure = $nvx->services->failure();
    if ($failure !== '') {
        echo '<div class="flash err">' . nvx_e($failure) . '</div>';
    }
    echo '<header class="page-head"><div><h1>' . nvx_e($title) . '</h1>';
    if ($lead !== '') {
        echo '<p class="lead">' . nvx_e($lead) . '</p>';
    }
    echo '</div></header>';
}

function nvx_footer(bool $bare = false): void
{
    if ($bare) {
        echo '</main><script src="/assets/js/nvx.js"></script></body></html>';
        return;
    }
    echo '</div></div><script src="/assets/js/nvx.js"></script></body></html>';
}

function nvx_flash(NvxContext $nvx): void
{
    $flash = $nvx->auth->takeFlash();
    if ($flash === null) {
        return;
    }
    $type = ($flash['type'] ?? '') === 'ok' ? 'ok' : 'err';
    echo '<div class="flash ' . $type . '">' . nvx_e((string) ($flash['message'] ?? '')) . '</div>';
}

function nvx_service_actions(NvxContext $nvx, string $service, string $return): void
{
    echo '<form class="actions" method="post" action="/services.php">';
    echo '<input type="hidden" name="csrf" value="' . nvx_e($nvx->auth->csrf()) . '">';
    echo '<input type="hidden" name="service" value="' . nvx_e($service) . '">';
    echo '<input type="hidden" name="return" value="' . nvx_e($return) . '">';
    echo '<button class="primary" name="action" value="start" type="submit">Start</button>';
    echo '<button name="action" value="restart" type="submit">Restart</button>';
    echo '<button class="danger" name="action" value="stop" type="submit">Stop</button>';
    echo '</form>';
}
