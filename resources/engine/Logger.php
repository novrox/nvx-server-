<?php
declare(strict_types=1);

final class Logger
{
    public function __construct(private string $root)
    {
        $this->root = rtrim($this->root, "\\/");
    }

    public function server(string $message): void
    {
        $this->write('logs/server.log', $message);
    }

    public function error(string $message): void
    {
        $this->write('logs/error.log', $message);
    }

    public function access(string $message): void
    {
        $this->write('logs/access.log', $message);
    }

    public function tail(string $channel, int $lines = 200): string
    {
        $file = $this->channel($channel);
        if ($file === null || !is_file($file)) {
            return '';
        }
        $size = filesize($file);
        if ($size === false || $size === 0) {
            return '';
        }
        $handle = fopen($file, 'rb');
        if ($handle === false) {
            return '';
        }
        $read = (int) min($size, 65536);
        fseek($handle, -$read, SEEK_END);
        $chunk = fread($handle, $read);
        fclose($handle);
        if ($chunk === false) {
            return '';
        }
        $rows = preg_split("/\r\n|\n|\r/", $chunk) ?: [];
        return implode("\n", array_slice($rows, -$lines));
    }

    public function channel(string $channel): ?string
    {
        $map = [
            'server' => 'logs/server.log',
            'error' => 'logs/error.log',
            'access' => 'logs/access.log',
            'php' => 'php/logs/error.log',
            'apache' => 'apache/logs/error.log',
            'mysql' => 'mysql/logs/error.log',
        ];
        if (!isset($map[$channel])) {
            return null;
        }
        return $this->root . DIRECTORY_SEPARATOR . str_replace('/', DIRECTORY_SEPARATOR, $map[$channel]);
    }

    private function write(string $relative, string $message): void
    {
        $path = $this->root . DIRECTORY_SEPARATOR . str_replace('/', DIRECTORY_SEPARATOR, $relative);
        $directory = dirname($path);
        if (!is_dir($directory)) {
            mkdir($directory, 0775, true);
        }
        $line = '[' . gmdate('Y-m-d H:i:s') . ' UTC] ' . str_replace(["\r", "\n"], ' ', $message) . PHP_EOL;
        file_put_contents($path, $line, FILE_APPEND | LOCK_EX);
    }
}
