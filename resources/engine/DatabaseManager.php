<?php
declare(strict_types=1);

final class DatabaseManager
{
    private const ROW_LIMIT = 100;

    public function __construct(private NvxContext $nvx)
    {
    }

    public function list(): array
    {
        $directory = $this->nvx->path('data/databases');
        $files = glob($directory . DIRECTORY_SEPARATOR . '*.sqlite') ?: [];
        $databases = [];
        foreach ($files as $file) {
            $name = basename($file, '.sqlite');
            if (!$this->validName($name)) {
                continue;
            }
            $pdo = $this->connect($name);
            $tables = (int) $pdo->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")->fetchColumn();
            $databases[] = [
                'name' => $name,
                'engine' => 'SQLite',
                'tables' => $tables,
                'bytes' => (int) filesize($file),
                'updated' => (int) filemtime($file),
            ];
        }
        usort($databases, static fn (array $a, array $b): int => strcasecmp($a['name'], $b['name']));
        return $databases;
    }

    public function create(string $name): void
    {
        $safe = $this->requireName($name);
        foreach ($this->list() as $database) {
            if (strcasecmp($database['name'], $safe) === 0) {
                throw new RuntimeException('A database named ' . $safe . ' already exists.');
            }
        }
        $pdo = $this->connect($safe);
        $pdo->exec('PRAGMA user_version = 1');
        $this->nvx->log->server('Created local database ' . $safe . '.');
    }

    public function drop(string $name, string $confirm): void
    {
        $safe = $this->requireName($name);
        if ($confirm !== $safe) {
            throw new RuntimeException('Type the database name to delete it.');
        }
        $file = $this->file($safe);
        if (!is_file($file)) {
            throw new RuntimeException('That database does not exist.');
        }
        unset($pdo);
        foreach ([$file, $file . '-journal', $file . '-wal', $file . '-shm'] as $path) {
            if (is_file($path) && !unlink($path)) {
                throw new RuntimeException('Could not delete ' . $safe . '. Stop anything using that database and try again.');
            }
        }
        $this->nvx->log->server('Deleted local database ' . $safe . '.');
    }

    public function tables(string $name): array
    {
        $pdo = $this->connect($this->requireName($name));
        $rows = $pdo->query("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")->fetchAll(PDO::FETCH_COLUMN);
        return array_map('strval', $rows);
    }

    public function createTable(string $name, string $table, array $columns): void
    {
        $safe = $this->requireName($name);
        $tableName = $this->requireIdentifier($table, 'Table name');
        $defs = [];
        foreach ($columns as $column) {
            if (!is_array($column)) {
                continue;
            }
            $columnName = trim((string) ($column['name'] ?? ''));
            $type = strtoupper(trim((string) ($column['type'] ?? '')));
            if ($columnName === '') {
                continue;
            }
            $columnName = $this->requireIdentifier($columnName, 'Column name');
            if (!in_array($type, ['INTEGER', 'TEXT', 'REAL', 'BLOB', 'NUMERIC'], true)) {
                throw new RuntimeException('Choose a column type of INTEGER, TEXT, REAL, BLOB, or NUMERIC.');
            }
            $defs[] = '"' . $columnName . '" ' . $type;
        }
        if ($defs === []) {
            throw new RuntimeException('Add at least one column.');
        }
        $pdo = $this->connect($safe);
        $pdo->exec('CREATE TABLE "' . $tableName . '" (' . implode(', ', $defs) . ')');
        $this->nvx->log->server('Created table ' . $safe . '.' . $tableName . '.');
    }

    public function dropTable(string $name, string $table): void
    {
        $safe = $this->requireName($name);
        $tableName = $this->requireIdentifier($table, 'Table name');
        $this->connect($safe)->exec('DROP TABLE "' . $tableName . '"');
        $this->nvx->log->server('Dropped table ' . $safe . '.' . $tableName . '.');
    }

    public function browse(string $name, string $table): array
    {
        $safe = $this->requireName($name);
        $tableName = $this->requireIdentifier($table, 'Table name');
        return $this->query($safe, 'SELECT * FROM "' . $tableName . '" LIMIT ' . self::ROW_LIMIT);
    }

    public function query(string $name, string $sql): array
    {
        $safe = $this->requireName($name);
        $sql = trim($sql);
        if ($sql === '') {
            throw new RuntimeException('Enter a SQL statement.');
        }
        if (strlen($sql) > 20000) {
            throw new RuntimeException('That statement is too long.');
        }
        if (preg_match('/\b(attach|detach|load_extension)\b/i', $sql) === 1) {
            throw new RuntimeException('ATTACH and load_extension are disabled.');
        }
        $parts = array_values(array_filter(array_map('trim', explode(';', $sql)), static fn (string $part): bool => $part !== ''));
        if (count($parts) !== 1) {
            throw new RuntimeException('Run one statement at a time.');
        }
        if (preg_match('/^(select|insert|update|delete|create|drop|alter|with)\b/i', $parts[0]) !== 1) {
            throw new RuntimeException('Start the statement with SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, or WITH.');
        }
        $pdo = $this->connect($safe);
        $reading = preg_match('/^(select|with)\b/i', $parts[0]) === 1;
        if ($reading) {
            $statement = $pdo->query($parts[0]);
            $rows = $statement->fetchAll(PDO::FETCH_ASSOC);
            $truncated = count($rows) > self::ROW_LIMIT;
            return [
                'kind' => 'rows',
                'columns' => $rows === [] ? [] : array_keys($rows[0]),
                'rows' => array_slice($rows, 0, self::ROW_LIMIT),
                'truncated' => $truncated,
                'affected' => count($rows),
            ];
        }
        $affected = $pdo->exec($parts[0]);
        return [
            'kind' => 'write',
            'columns' => [],
            'rows' => [],
            'truncated' => false,
            'affected' => $affected === false ? 0 : $affected,
        ];
    }

    public function file(string $name): string
    {
        return $this->nvx->path('data/databases/' . $this->requireName($name) . '.sqlite');
    }

    public function mysqlOverview(): array
    {
        try {
            $pdo = $this->mysql();
            $rows = $pdo->query('SHOW DATABASES')->fetchAll(PDO::FETCH_COLUMN);
            $databases = [];
            foreach ($rows as $row) {
                $name = (string) $row;
                $databases[] = [
                    'name' => $name,
                    'system' => in_array($name, ['information_schema', 'performance_schema', 'mysql', 'sys'], true),
                ];
            }
            return ['connected' => true, 'error' => '', 'databases' => $databases];
        } catch (Throwable $e) {
            return ['connected' => false, 'error' => $e->getMessage(), 'databases' => []];
        }
    }

    public function createMysql(string $name): void
    {
        $safe = $this->requireName($name);
        $this->mysql()->exec('CREATE DATABASE `' . $safe . '` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci');
        $this->nvx->log->server('Created MySQL database ' . $safe . '.');
    }

    public function dropMysql(string $name, string $confirm): void
    {
        $safe = $this->requireName($name);
        if ($confirm !== $safe) {
            throw new RuntimeException('Type the database name to delete it.');
        }
        if (in_array($safe, ['information_schema', 'performance_schema', 'mysql', 'sys'], true)) {
            throw new RuntimeException('That is a MySQL system database.');
        }
        $this->mysql()->exec('DROP DATABASE `' . $safe . '`');
        $this->nvx->log->server('Dropped MySQL database ' . $safe . '.');
    }

    private function connect(string $name): PDO
    {
        $file = $this->nvx->path('data/databases/' . $name . '.sqlite');
        $pdo = new PDO('sqlite:' . str_replace('\\', '/', $file));
        $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
        $pdo->exec('PRAGMA foreign_keys = ON');
        $pdo->exec('PRAGMA busy_timeout = 3000');
        return $pdo;
    }

    private function mysql(): PDO
    {
        $port = (int) $this->nvx->config->get('ports', 'mysql', '3307');
        $user = $this->nvx->config->get('mysql', 'user', 'root');
        $password = $this->nvx->config->get('mysql', 'password');
        $dsn = 'mysql:host=127.0.0.1;port=' . $port . ';charset=utf8mb4';
        return new PDO($dsn, $user, $password, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_TIMEOUT => 2,
        ]);
    }

    private function requireName(string $name): string
    {
        $name = trim($name);
        if (!$this->validName($name)) {
            throw new RuntimeException('Use a name that starts with a letter and uses only letters, numbers, dashes, or underscores.');
        }
        return $name;
    }

    private function validName(string $name): bool
    {
        return preg_match('/^[A-Za-z][A-Za-z0-9_-]{0,62}$/', $name) === 1;
    }

    private function requireIdentifier(string $name, string $label): string
    {
        $name = trim($name);
        if (preg_match('/^[A-Za-z_][A-Za-z0-9_]{0,62}$/', $name) !== 1) {
            throw new RuntimeException($label . ' must start with a letter or underscore and use only letters, numbers, or underscores.');
        }
        return $name;
    }
}
