<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

require __DIR__ . '/bootstrap.php';

try {
    exit(nvx_cli($argv));
} catch (Throwable $e) {
    $nvx = nvx();
    $nvx->log->error($e->getMessage());
    $nvx->services->noteAction(false, $e->getMessage());
    fwrite(STDERR, $e->getMessage() . PHP_EOL);
    exit(1);
}

function nvx_cli(array $argv): int
{
    $nvx = nvx();
    $command = $argv[1] ?? 'status';
    $delay = 0;
    $service = 'all';
    $yes = false;
    $options = [];
    foreach (array_slice($argv, 2) as $argument) {
        if (preg_match('/^--delay=(\d+)$/', $argument, $matches) === 1) {
            $delay = min(10, (int) $matches[1]);
            continue;
        }
        if ($argument === '--yes') {
            $yes = true;
            continue;
        }
        if (preg_match('/^--([A-Za-z0-9-]+)=(.*)$/', $argument, $matches) === 1) {
            $options[$matches[1]] = $matches[2];
            continue;
        }
        if (str_starts_with($argument, '--')) {
            throw new RuntimeException('Unknown option ' . $argument);
        }
        $service = $argument;
    }
    if ($delay > 0) {
        sleep($delay);
    }

    switch ($command) {
        case 'help':
            echo <<<TXT
NVX Server commands
  status
  start [php|apache|mysql|all]
  stop [php|apache|mysql|all]
  restart [php|apache|mysql|all]
  control
  setup --user=admin --password=secret --port=8088 --mysql-port=3307 --name="NVX Server"
  backup
  uninstall --yes
  update

TXT;
            return 0;

        case 'status':
            $status = $nvx->services->status();
            echo $status['name'] . ' ' . $status['version'] . PHP_EOL;
            echo 'Address: ' . $status['url'] . PHP_EOL . PHP_EOL;
            foreach ($status['services'] as $item) {
                echo sprintf("%-12s %-16s port %d\n", $item['name'], $item['label'], $item['port']);
                echo '             ' . $item['detail'] . PHP_EOL;
            }
            return 0;

        case 'start':
            $message = $nvx->services->start($service);
            $nvx->services->noteAction(true, '');
            echo $message . PHP_EOL;
            return 0;

        case 'stop':
            $message = $nvx->services->stop($service);
            $nvx->services->noteAction(true, '');
            echo $message . PHP_EOL;
            return 0;

        case 'restart':
            $message = $nvx->services->restart($service);
            $nvx->services->noteAction(true, '');
            echo $message . PHP_EOL;
            return 0;

        case 'control':
            $message = $nvx->services->start('all');
            $nvx->services->noteAction(true, '');
            $databases = $nvx->databases->list();
            $url = $nvx->services->url();
            echo $message . PHP_EOL;
            echo 'Local databases are ready (' . count($databases) . ' in data\\databases).' . PHP_EOL;
            echo 'NVX Server is available at ' . $url . PHP_EOL;
            echo 'CONTROL_URL=' . $url . PHP_EOL;
            return 0;

        case 'setup':
            echo "NVX Server does not use an account. Open the control panel to use databases and services.\n";
            return 0;

        case 'backup':
            echo 'Backup: ' . $nvx->installer->backup() . PHP_EOL;
            return 0;

        case 'uninstall':
            if (!$yes) {
                echo "This stops NVX Server, deletes local databases, and resets settings.\n";
                echo "Run again with --yes to continue.\n";
                return 1;
            }
            $nvx->installer->uninstall(true);
            echo "NVX Server was reset. Delete this folder to remove the program files.\n";
            return 0;

        case 'update':
            echo 'NVX Server ' . $nvx->config->get('server', 'version', '1.0.0') . PHP_EOL;
            echo 'PHP ' . PHP_VERSION . PHP_EOL;
            echo 'PHP binary: ' . ($nvx->services->phpBinary() ?? 'not found') . PHP_EOL;
            echo 'Apache: ' . (is_file($nvx->path('apache/bin/httpd.exe')) ? 'found' : 'not installed') . PHP_EOL;
            echo 'MySQL: ' . (is_file($nvx->path('mysql/bin/mysqld.exe')) ? 'found' : 'not installed') . PHP_EOL;
            echo "Replace files in php\\, apache\\, and mysql\\, then restart the server.\n";
            return 0;

        default:
            throw new RuntimeException('Unknown command. Run help for the list.');
    }
}
