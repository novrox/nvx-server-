# NVX Server Technical Reference

Version 1.0.0

This document describes the Windows desktop control panel, browser dashboard, local web stack, service lifecycle, configuration, APIs, storage, security boundaries, and release process.

## 1. Product Overview

NVX Server is a local development stack for Windows x64. It combines a native Windows Forms control panel with a local HTTP server that serves files from `nvxh`, executes PHP through PHP-CGI, and manages local SQLite databases. MariaDB/MySQL is optional. Apache is optional and is not required to serve PHP applications.

The default security model is single-computer access. The native web listener binds to the IPv4 loopback interface, `127.0.0.1`. User-facing links use `localhost`:

- Port 80: `http://localhost/`
- Other ports: `http://localhost:<port>/`

The desktop application, browser panel, PHP runtime, database files, logs, and configuration remain on the user's computer.

## 2. Installation and Startup

### Install for the current Windows user

1. Extract `NVX-Server.zip`.
2. Run `nvx-setup.exe`.
3. The installer copies the application into `%LocalAppData%\NVX Server` and creates Start Menu and Desktop shortcuts.
4. Start NVX Server from either shortcut.

The installer does not require administrator access. Runtime components are downloaded into the installation folder when absent. Existing user data is kept in that folder and is not included in release ZIPs.

### Run from a folder

Run `nvx.exe` directly from the extracted folder. The application uses its executable directory as the NVX root, so keep `nvx.exe`, `sqlite3.dll`, `nvx-ico.png`, configuration, and the `nvxh`, `data`, `php`, and `mysql` folders together.

### Startup sequence

The native window opens before runtime preparation completes. Runtime preparation runs in the background. The application then starts the built-in Web listener and attempts to start MySQL when its executable is present. Status indicators and the log show progress and failures.

## 3. Native Desktop Interface

The desktop control panel has four tabs.

### Status

- Shows the current localhost address.
- Displays Web, Apache, and MySQL state and start/stop controls.
- Shows discovered projects from `nvxh\apps` and opens them in the default browser.
- Shows recent server and error log entries.
- Web start/stop work runs off the UI thread. The status reads `Starting...` or `Stopping...` while the operation completes.

### Databases

- Creates, lists, opens, and deletes local SQLite databases.
- Opens a database browser for table creation, row browsing, and SQL execution.
- Requires typing the database name to confirm deletion.
- Uses `data\databases\<name>.sqlite` for files.

### Apps

- Lists folders under `nvxh\apps` containing `index.php` or `index.html`.
- Opens a selected project in the browser.
- Opens the applications folder in Windows Explorer.

### Settings

- Sets the server display name, timezone, HTTP port, and MySQL port.
- Keeps the web server bound to this computer.
- A saved port change takes effect after Web restarts.

## 4. Browser Dashboard

The PHP dashboard is served from the same local web root. Its navigation pages are:

- **Overview**: address, Web uptime, database/app counts, service cards, recent log, and project/database links.
- **Services**: status, startup mode, port, and start/restart/stop actions for PHP, Apache, and MySQL.
- **Databases**: create or delete SQLite databases; inspect MySQL databases when MySQL is running.
- **Apps**: list project URLs and explain the `nvxh\apps` location.
- **Logs**: view recent Server, Errors, Access, PHP, Apache, and MySQL log entries.
- **Settings**: server name, timezone, HTTP and MySQL ports, bind address, and runtime path.

The dashboard polls `GET /api/status.php` every four seconds while open. A connection label shows whether status polling is connected. Service actions are submitted with a session CSRF token; the PHP service manager schedules actions so a stop/restart request can finish before the web process exits.

## 5. Web Server and Port Selection

The native Web service uses a `TcpListener` on `127.0.0.1`. It serves files from `nvxh`; PHP requests are passed to `php-cgi.exe`. Apache is not needed for this path.

The configured HTTP port is tried first. If it cannot be bound, the native server tries these ports in order:

1. `8080`
2. `8081`
3. `8000`
4. `8888`
5. `8088`
6. An OS-assigned available port

The selected port is persisted to `config\ports.sys`. The displayed URL, project links, and PHP CGI server-port value use the selected port. Port 80 is displayed without an explicit `:80`; other ports are included in the URL. If a fallback port is selected, the server log records the configured and selected ports.

A port can still fail if Windows denies binding or every candidate and dynamic allocation fail. Check `logs\error.log` and `logs\server.log` in that case. The application does not stop an unrelated process that owns a port.

Apache is an optional alternative HTTP server. If installed, it uses the configured HTTP port and cannot share that port with the built-in Web service. MySQL/MariaDB uses the configured MySQL port, `3307` by default.

## 6. PHP and Application Serving

PHP source files are executed with the bundled `php-cgi.exe`. The runtime can be found at `php\php-cgi.exe` or `php\bin\php-cgi.exe`. PHP configuration is generated under `tmp` and sets extension, log, session, and temporary paths relative to the NVX root.

To add a project, create a folder with an index file:

```text
nvxh\apps\my-app\index.php
```

or:

```text
nvxh\apps\my-app\index.html
```

With Web running, open it at `http://localhost:<selected-port>/apps/my-app/`. The port portion is omitted when the selected port is 80. Applications are local web content, not isolated sandboxes; PHP apps run with the permissions of the NVX process.

## 7. Databases and SQL

### SQLite

Each local database is a file in `data\databases`. Names start with a letter and contain letters, digits, hyphens, or underscores. Tables and columns use restricted identifiers. Browsing is limited to 100 rows per result.

The SQL editor accepts one statement at a time, limited to `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `DROP`, `ALTER`, or `WITH`. `ATTACH`, `DETACH`, and extension loading are blocked. SQL is still powerful and can modify or delete data; export or back up important files before destructive statements.

### MySQL/MariaDB

MariaDB is downloaded into `mysql` when needed and listens on `127.0.0.1`, using port `3307` by default. The browser Databases page shows MySQL databases when the service is available. System databases cannot be deleted from that page.

## 8. Configuration and Storage

Configuration files use `key = "value"` lines. The application rewrites the files it saves. Release packaging writes clean portable defaults rather than copying local configuration.

| File | Purpose |
| --- | --- |
| `config/server.sys` | Display name, version, document root, bind address, timezone |
| `config/ports.sys` | HTTP, HTTPS metadata, MySQL, FTP, and mail ports |
| `config/paths.sys` | Web, runtime, data, logs, and temporary paths |
| `config/services.sys` | PHP, Apache, and MySQL startup modes |
| `config/security.sys` | Local-only and remote-access flags, panel user/hash fields, session name |
| `services/*.sys` | Service executable, config template, role, and port mapping |

Key directories:

| Directory | Contents |
| --- | --- |
| `nvxh` | Web root and dashboard |
| `nvxh\apps` | User applications |
| `data\databases` | SQLite files |
| `data\uploads` | Upload storage |
| `data\user-data` | Application/user files |
| `data\backups` | Backups |
| `php`, `mysql`, `apache` | Optional or downloaded runtimes |
| `logs` | Server, access, error, and service logs |
| `tmp` | Runtime configuration, process markers, PHP sessions, and temporary files |

## 9. Local API

All API routes are under `/api`. The API returns JSON and uses `Cache-Control: no-store`.

| Route | Method | Behavior |
| --- | --- | --- |
| `/api/status.php` | GET | Returns server URL and PHP, Apache, and MySQL states, ports, modes, and details |
| `/api/services.php` | POST | Schedules a service action; fields: `action` (`start`, `stop`, `restart`) and `service` (`php`, `apache`, `mysql`, or `all`) |
| `/api/databases.php` | GET | Returns the local SQLite database list |
| `/api/databases.php` | POST | Creates or drops a local SQLite database; mutation requires a valid CSRF token |
| `/api/logs.php?channel=server` | GET | Returns the last 200 lines for a valid log channel |

POST requests require a valid session CSRF token. The UI obtains and submits this token automatically. The APIs are intended for the local dashboard, not as a public network service.

## 10. Security and Privacy

- Default service binding is loopback; browser links use `localhost`.
- The control panel has no sign-in. Keep `bind_localhost_only = "1"` and `allow_remote = "0"`; remote access exposes server and database controls to reachable clients.
- The application blocks hidden dotfiles and selected configuration, script, database, and SQLite sidecar files from web serving.
- Dashboard mutations require CSRF validation. Session cookies are HTTP-only and SameSite Strict.
- SQL identifiers are validated; SQL is limited to one statement and attachment/extension-loading operations are blocked.
- User apps execute with the current process privileges. Do not put secrets in content served from `nvxh`.
- The local HTTP interface does not provide TLS. Do not expose it directly to the Internet.
- Release ZIPs exclude local databases, user app contents, runtime downloads, logs, temporary data, and generated machine-specific configuration.

## 11. Logs and Troubleshooting

| File | Purpose |
| --- | --- |
| `logs\server.log` | Startup, stop, configuration, and service messages |
| `logs\error.log` | Application errors |
| `logs\access.log` | Local HTTP requests |
| `php\logs\error.log` | PHP runtime errors |
| `apache\logs\error.log` | Apache errors when Apache is installed |
| `mysql\logs` | MariaDB/MySQL logs |

Common checks:

- **Web does not open on port 80**: read the address shown in the desktop window; NVX may have selected a fallback port. The selection is also recorded in `logs\server.log` and `config\ports.sys`.
- **Project link reports an unavailable port**: confirm Web is running, then use the current address shown by NVX. Do not assume port 80 after a fallback.
- **PHP page returns an error**: confirm `php-cgi.exe` exists and inspect `php\logs\error.log` and `logs\error.log`.
- **MySQL is unavailable**: confirm `mysql\bin\mysqld.exe`, check the configured MySQL port, and inspect `mysql\logs`.
- **Runtime download fails**: retry with network access, or install/refresh the runtime with `scripts\fetch-runtimes.bat`.
- **A port change does not take effect**: restart the affected service. Apache and the PHP web process must be restarted after changing the HTTP port.

## 12. Build and Release

Build the clean installer archive from the project root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1
```

The packager stages a clean `NVX Server` folder, compiles `nvx.exe` and `nvx-setup.exe` with `nvx.ico`, writes portable default configuration, runs the SQLite and web-port fallback self-tests, scans for machine-specific paths, validates required files, and writes `dist\NVX-Server.zip`. The temporary package staging directory is removed on success or failure.

The build requires Windows PowerShell and the .NET Framework C# compiler (`csc.exe`). The final ZIP is intended for Windows x64.

## 13. Source Layout

| Path | Responsibility |
| --- | --- |
| `resources\src\NvxApp\MainForm.cs` | Native tabs, controls, and user interactions |
| `resources\src\NvxApp\NvxServices.cs` | Native HTTP listener, PHP-CGI dispatch, service startup, and port selection |
| `resources\src\NvxApp\NvxDatabase.cs` | Native SQLite database operations |
| `resources\engine` | PHP dashboard bootstrap, authentication/session handling, services, databases, and templates |
| `nvxh\` | Web dashboard and local application document root |
| `scripts\package.ps1` | Clean release build, validation, and ZIP creation |

Created by Novrox | hello@novrox.com
