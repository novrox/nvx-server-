<?php
declare(strict_types=1);

final class Config
{
    private array $cache = [];

    /** @var array<string, string> */
    private array $files = [
        'server' => 'config/server.sys',
        'ports' => 'config/ports.sys',
        'paths' => 'config/paths.sys',
        'services' => 'config/services.sys',
        'security' => 'config/security.sys',
        'install' => 'install/install.sys',
        'requirements' => 'install/requirements.sys',
        'apache' => 'services/apache.sys',
        'mysql' => 'services/mysql.sys',
        'php' => 'services/php.sys',
    ];

    public function __construct(private string $root)
    {
        $this->root = rtrim($this->root, "\\/");
    }

    public function get(string $name, string $key, string $default = ''): string
    {
        $values = $this->read($name);
        return $values[$key] ?? $default;
    }

    public function read(string $name): array
    {
        if (!isset($this->cache[$name])) {
            $this->cache[$name] = $this->parseFile($this->absolute($this->files[$name] ?? $name));
        }
        return $this->cache[$name];
    }

    public function readSections(string $relative): array
    {
        $sections = [];
        $section = 'default';
        foreach ($this->lines($this->absolute($relative)) as $line) {
            if (preg_match('/^\[([A-Za-z0-9_-]+)\]$/', $line, $matches) === 1) {
                $section = $matches[1];
                $sections[$section] ??= [];
                continue;
            }
            $pair = $this->pair($line);
            if ($pair !== null) {
                $sections[$section][$pair[0]] = $pair[1];
            }
        }
        return $sections;
    }

    public function update(string $name, array $changes): void
    {
        $values = $this->read($name);
        foreach ($changes as $key => $value) {
            $values[(string) $key] = str_replace(["\r", "\n"], '', (string) $value);
        }
        $this->write($name, $values);
    }

    public function write(string $name, array $values): void
    {
        if (!isset($this->files[$name])) {
            throw new RuntimeException('Unknown configuration: ' . $name);
        }
        $lines = [
            '; NVX Server ' . $name,
            '; Rewritten when the control panel saves settings.',
        ];
        foreach ($values as $key => $value) {
            if (!preg_match('/^[A-Za-z0-9_-]+$/', (string) $key)) {
                continue;
            }
            $lines[] = $key . ' = ' . $this->encode((string) $value);
        }
        $path = $this->absolute($this->files[$name]);
        $directory = dirname($path);
        if (!is_dir($directory)) {
            mkdir($directory, 0775, true);
        }
        if (file_put_contents($path, implode("\r\n", $lines) . "\r\n") === false) {
            throw new RuntimeException('Could not write ' . $this->files[$name]);
        }
        $this->cache[$name] = $values;
    }

    private function absolute(string $relative): string
    {
        if (preg_match('/^[A-Za-z]:[\\\\\\/]/', $relative) === 1) {
            return $relative;
        }
        return $this->root . DIRECTORY_SEPARATOR . str_replace(['/', '\\'], DIRECTORY_SEPARATOR, $relative);
    }

    private function parseFile(string $path): array
    {
        $values = [];
        foreach ($this->lines($path) as $line) {
            if (str_starts_with($line, '[')) {
                continue;
            }
            $pair = $this->pair($line);
            if ($pair !== null) {
                $values[$pair[0]] = $pair[1];
            }
        }
        return $values;
    }

    private function lines(string $path): array
    {
        if (!is_file($path)) {
            return [];
        }
        $raw = file($path, FILE_IGNORE_NEW_LINES);
        if ($raw === false) {
            return [];
        }
        $lines = [];
        foreach ($raw as $index => $line) {
            if ($index === 0) {
                $line = preg_replace('/^\xEF\xBB\xBF/', '', $line) ?? $line;
            }
            $line = trim($line);
            if ($line === '' || str_starts_with($line, ';') || str_starts_with($line, '#')) {
                continue;
            }
            $lines[] = $line;
        }
        return $lines;
    }

    private function pair(string $line): ?array
    {
        $split = strpos($line, '=');
        if ($split === false) {
            return null;
        }
        $key = trim(substr($line, 0, $split));
        $value = trim(substr($line, $split + 1));
        if ($key === '') {
            return null;
        }
        $length = strlen($value);
        if ($length >= 2 && $value[0] === '"' && $value[$length - 1] === '"') {
            $value = str_replace(['\\"', '\\\\'], ['"', '\\'], substr($value, 1, -1));
        }
        return [$key, $value];
    }

    private function encode(string $value): string
    {
        return '"' . str_replace(['\\', '"'], ['\\\\', '\\"'], $value) . '"';
    }
}
