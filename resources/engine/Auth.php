<?php
declare(strict_types=1);

final class Auth
{
    public function __construct(private NvxContext $nvx)
    {
    }

    public function isLocal(): bool
    {
        $address = $_SERVER['REMOTE_ADDR'] ?? '';
        return $address === '127.0.0.1' || $address === '::1';
    }

    public function assertLocalPage(): void
    {
        if ($this->isLocal() || $this->nvx->config->get('security', 'bind_localhost_only') === '0') {
            return;
        }
        http_response_code(403);
        echo 'NVX Server accepts connections from this computer only.';
        exit;
    }

    public function startSession(): void
    {
        if (session_status() === PHP_SESSION_ACTIVE) {
            return;
        }
        $path = $this->nvx->path('tmp/sessions');
        if (is_dir($path)) {
            session_save_path($path);
        }
        $name = $this->nvx->config->get('security', 'session_name', 'NVXSESSID');
        if (preg_match('/^[A-Za-z0-9_-]{1,64}$/', $name) === 1) {
            session_name($name);
        }
        session_set_cookie_params([
            'lifetime' => 0,
            'path' => '/',
            'httponly' => true,
            'samesite' => 'Strict',
        ]);
        session_start();
        if (!isset($_SESSION['csrf']) || !is_string($_SESSION['csrf'])) {
            $_SESSION['csrf'] = bin2hex(random_bytes(16));
        }
    }

    public function csrf(): string
    {
        $token = $_SESSION['csrf'] ?? '';
        return is_string($token) ? $token : '';
    }

    public function verifyCsrf(string $token): bool
    {
        $known = $this->csrf();
        return $known !== '' && $token !== '' && hash_equals($known, $token);
    }

    public function check(): bool
    {
        $sessionUser = $_SESSION['user'] ?? '';
        $admin = $this->nvx->config->get('security', 'admin_user');
        $hash = $this->nvx->config->get('security', 'admin_password_hash');
        return is_string($sessionUser)
            && $sessionUser !== ''
            && $admin !== ''
            && $hash !== ''
            && hash_equals($admin, $sessionUser);
    }

    public function attempt(string $user, string $password): bool
    {
        if ($this->locked()) {
            return false;
        }
        $admin = $this->nvx->config->get('security', 'admin_user');
        $hash = $this->nvx->config->get('security', 'admin_password_hash');
        $userOk = $admin !== '' && hash_equals($admin, $user);
        $passOk = $hash !== '' && password_verify($password, $hash);
        if (!$userOk || !$passOk) {
            $this->fail();
            $this->nvx->log->server('Rejected a control panel sign-in.');
            return false;
        }
        $this->clearFailures();
        if (password_needs_rehash($hash, PASSWORD_DEFAULT)) {
            $this->nvx->config->update('security', [
                'admin_password_hash' => password_hash($password, PASSWORD_DEFAULT),
            ]);
        }
        $this->login();
        return true;
    }

    public function login(): void
    {
        session_regenerate_id(true);
        $_SESSION['user'] = $this->nvx->config->get('security', 'admin_user');
        $_SESSION['since'] = time();
        $_SESSION['csrf'] = bin2hex(random_bytes(16));
    }

    public function logout(): void
    {
        $_SESSION = [];
        if (ini_get('session.use_cookies')) {
            $params = session_get_cookie_params();
            setcookie(session_name(), '', time() - 42000, $params['path'], $params['domain'], $params['secure'], $params['httponly']);
        }
        session_destroy();
    }

    public function locked(): bool
    {
        $state = $this->throttle();
        return ($state['until'] ?? 0) > time();
    }

    public function flash(string $type, string $message): void
    {
        $_SESSION['flash'] = ['type' => $type === 'ok' ? 'ok' : 'err', 'message' => $message];
    }

    public function takeFlash(): ?array
    {
        if (!isset($_SESSION['flash']) || !is_array($_SESSION['flash'])) {
            return null;
        }
        $flash = $_SESSION['flash'];
        unset($_SESSION['flash']);
        return $flash;
    }

    private function fail(): void
    {
        $state = $this->throttle();
        $fails = (int) ($state['fails'] ?? 0) + 1;
        $until = $fails >= 5 ? time() + 60 : 0;
        if ($until > 0) {
            $fails = 0;
        }
        $this->saveThrottle(['fails' => $fails, 'until' => $until]);
    }

    private function clearFailures(): void
    {
        $file = $this->throttleFile();
        if (is_file($file)) {
            unlink($file);
        }
    }

    private function throttle(): array
    {
        $file = $this->throttleFile();
        if (!is_file($file)) {
            return ['fails' => 0, 'until' => 0];
        }
        $data = json_decode((string) file_get_contents($file), true);
        return is_array($data) ? $data : ['fails' => 0, 'until' => 0];
    }

    private function saveThrottle(array $state): void
    {
        file_put_contents($this->throttleFile(), json_encode($state));
    }

    private function throttleFile(): string
    {
        return $this->nvx->path('tmp/login-throttle.json');
    }
}
