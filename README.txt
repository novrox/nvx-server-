NVX Server
==========

NVX Server is a Windows x64 local development stack. Its desktop control
panel manages a built-in web server, PHP, SQLite databases, and optional
MariaDB/MySQL and Apache services. It does not use or require XAMPP.

The desktop panel and browser dashboard use localhost URLs. The default
address is http://localhost/. If the configured HTTP port is busy, the
desktop web server tries available fallback ports and displays the selected
localhost address.

Quick start
-----------

1. Unzip NVX-Server.zip and double-click nvx-setup.exe
   It installs NVX Server into your user profile and adds a Start Menu
   and Desktop shortcut. No administrator password is required.
   The package does not contain logs or databases from another computer.
2. Open NVX Server from the Start Menu or Desktop.
   The window starts the local web service, PHP, and MySQL.
3. Open the Status page and confirm Web is running.
4. Open Databases to create a local SQLite database, or Apps to open a project.

The browser dashboard is available at the localhost address shown in the
desktop window. It includes Overview, Services, Databases, Apps, Logs, and
Settings pages.

For interface descriptions, service behavior, configuration files, local
APIs, storage layout, security boundaries, and troubleshooting, see
resources\docs\technical-reference.md.

To build a clean release ZIP from source, run:

   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1

The ZIP is written to dist\NVX-Server.zip. It excludes local logs, databases,
runtime downloads, generated configuration, and old executable copies. The
first launch downloads PHP and MariaDB in the background so the control panel
opens without waiting for runtime setup.

You can also run nvx.exe from this folder without installing.
The first start downloads portable PHP and MariaDB into php\ and mysql\
if they are not already there.

The web service only accepts connections from this computer, on
http://localhost/ by default. It serves PHP and other files from nvxh.

PHP
---

NVX Server runs .php files with php-cgi.exe from php\ (or php\bin).
It does not look in C:\xampp. To install or refresh the runtime:

  scripts\fetch-runtimes.bat

Local databases
---------------

Databases you create in the NVX Server window are SQLite files in
data\databases. Open a database to add tables or run one SQL statement
at a time.

MySQL
-----

MySQL/MariaDB is part of NVX Server. mysqld.exe lives in mysql\bin.
It listens on 127.0.0.1 and uses port 3307 by default so it does not
collide with other database software on 3306.

Apache
------

Apache is optional. Place httpd.exe in apache\bin if you want it.
The built-in Web service already serves PHP without Apache. When its
configured HTTP port is occupied, it tries ports 8080, 8081, 8000, 8888,
and 8088, then asks Windows for an available port. The selected port is
saved in config\ports.sys and is shown in the localhost URL.

Apps
----

Create a folder nvxh\apps\your-app and put an index.php or index.html file
in it. While the web service is running they are available at
http://localhost/apps/your-app/

Scripts
-------

scripts\start.bat           Open the NVX Server window
scripts\stop.bat            Close NVX Server
scripts\backup.bat          Back up config and data
scripts\fetch-runtimes.bat  Download portable PHP and MySQL into this folder
scripts\build-app.bat       Rebuild nvx.exe from resources\src\NvxApp

nvx.exe                The NVX Server window
nvx-setup.exe          Installs NVX Server for this Windows user

Settings files (config\*.sys and services\*.sys) use key = "value" lines.
Saving settings from the window rewrites the files it saves.

Security
--------

The web service binds to 127.0.0.1. Other computers cannot connect.
There is no sign-in. Keep bind_localhost_only = "1" and allow_remote = "0";
enabling remote access exposes server and database controls to reachable
clients. SQL is limited to one statement, and ATTACH is blocked. Do not put
secrets in apps you do not want available to every account on this PC.

Uninstall
---------

Close NVX Server, then delete this folder, or delete
%LocalAppData%\NVX Server if you installed it. Also delete the
Desktop and Start Menu shortcuts named NVX Server.

Logs
----

logs\server.log    Start, stop, and settings events
logs\error.log     Errors
logs\access.log    Files requested from the web service
