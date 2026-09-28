# NVX Server

**NVX Server** is a lightweight Windows x64 local development environment designed for modern web and application development. It provides an integrated desktop control panel, built-in web server, PHP runtime, SQLite database management, and optional MariaDB/MySQL and Apache support.

Unlike traditional development stacks, NVX Server is fully self-contained and does **not require XAMPP, WAMP, IIS, or administrator privileges**.

---

## Features

* Native Windows x64 application
* Built-in localhost web server
* PHP runtime support (CGI)
* SQLite database management
* Optional MariaDB/MySQL support
* Optional Apache integration
* Automatic port conflict detection
* Local-only security model
* Portable deployment option
* Desktop Control Panel
* Browser-based Dashboard
* Automatic runtime downloads
* Project and application management
* Backup and restore utilities

---

## System Requirements

| Component            | Requirement                                |
| -------------------- | ------------------------------------------ |
| Operating System     | Windows 10 / Windows 11                    |
| Architecture         | 64-bit (x64)                               |
| RAM                  | 4 GB minimum                               |
| Storage              | 500 MB available space                     |
| Network              | Localhost only (127.0.0.1)                 |
| Administrator Rights | Not Required                               |
| Internet Connection  | Required only for initial runtime download |

---

## Architecture

```text
NVX Server
│
├── Desktop Control Panel (nvx.exe)
├── Built-in Web Server
├── PHP Runtime
├── SQLite Engine
├── MariaDB/MySQL (Optional)
├── Apache (Optional)
│
└── Browser Dashboard
     ├── Overview
     ├── Services
     ├── Databases
     ├── Applications
     ├── Logs
     └── Settings
```

---

## Installation

### Method 1 — Installer

1. Download `NVX-Server.zip`
2. Extract the archive
3. Run:

```powershell
nvx-setup.exe
```

4. Launch **NVX Server** from:

* Start Menu
* Desktop Shortcut

No administrator password is required.

---

### Method 2 — Portable Mode

Run directly from the extracted folder:

```powershell
nvx.exe
```

Portable mode requires no installation.

---

## First Launch

When launched for the first time, NVX Server automatically downloads:

* Portable PHP Runtime
* Portable MariaDB Runtime

Downloaded components are stored in:

```text
php\
mysql\
```

This process runs in the background so the application opens immediately.

---

## Default Local Address

```text
http://localhost/
```

If the default port is unavailable, NVX Server automatically selects an available port.

### Fallback Ports

```text
8080
8081
8000
8888
8088
```

If all predefined ports are occupied, Windows assigns an available port automatically.

The selected port is stored in:

```text
config\ports.sys
```

Example:

```text
http://localhost:8080/
```

---

## Dashboard Modules

### Overview

Displays:

* Service Status
* Active Port
* Runtime Information
* Resource Usage

### Services

Manage:

* Web Service
* PHP Runtime
* MariaDB/MySQL
* Apache (Optional)

### Databases

Create and manage:

* SQLite Databases
* Tables
* Queries

### Applications

Launch local development projects.

### Logs

Access:

* Access Logs
* Error Logs
* Server Events

### Settings

Configure:

* Ports
* Runtime Options
* Security Parameters

---

## PHP Support

NVX Server executes PHP files using:

```text
php-cgi.exe
```

Supported locations:

```text
php\
php\bin\
```

NVX Server does not depend on:

```text
C:\xampp
```

### Refresh Runtime

```powershell
scripts\fetch-runtimes.bat
```

---

## SQLite Database Engine

SQLite databases are stored in:

```text
data\databases\
```

Features:

* Create databases
* Create tables
* Execute SQL commands
* Local storage
* Zero configuration

---

## MariaDB / MySQL

Integrated MariaDB support.

### Default Configuration

| Setting | Value      |
| ------- | ---------- |
| Host    | 127.0.0.1  |
| Port    | 3307       |
| Access  | Local Only |

Executable:

```text
mysql\bin\mysqld.exe
```

Port 3307 is used to avoid conflicts with standard MySQL installations.

---

## Apache Integration

Apache is optional.

Place:

```text
apache\bin\httpd.exe
```

inside:

```text
apache\bin\
```

to enable Apache support.

The built-in NVX Server web service can operate independently without Apache.

---

## Creating Applications

Create a new application:

```text
nvxh\apps\my-app\
```

Add:

```text
index.php
```

or

```text
index.html
```

Access through:

```text
http://localhost/apps/my-app/
```

---

## Directory Structure

```text
NVX Server
│
├── apache\
├── config\
├── data\
│   └── databases\
├── logs\
├── mysql\
├── nvxh\
│   └── apps\
├── php\
├── resources\
├── scripts\
├── services\
│
├── nvx.exe
└── nvx-setup.exe
```

---

## Scripts

| Script             | Description                   |
| ------------------ | ----------------------------- |
| start.bat          | Launch NVX Server             |
| stop.bat           | Stop NVX Server               |
| backup.bat         | Backup data and configuration |
| fetch-runtimes.bat | Download PHP and MariaDB      |
| build-app.bat      | Rebuild NVX application       |

---

## Configuration

Configuration files:

```text
config\*.sys
services\*.sys
```

Format:

```text
key = "value"
```

Example:

```text
http_port = "8080"
bind_localhost_only = "1"
allow_remote = "0"
```

---

## Security Specifications

### Default Security Model

* Localhost only
* No external network exposure
* No remote database access
* Single-statement SQL execution
* ATTACH statements blocked
* No user authentication system

### Recommended Settings

```text
bind_localhost_only = "1"
allow_remote = "0"
```

### Security Notes

* Keep applications free of sensitive credentials.
* Do not enable remote access unless necessary.
* Every Windows user account on the machine can access locally hosted applications.

---

## Logging

### Server Log

```text
logs\server.log
```

Records:

* Startup events
* Shutdown events
* Configuration changes

### Error Log

```text
logs\error.log
```

Records:

* Runtime errors
* Service failures

### Access Log

```text
logs\access.log
```

Records:

* HTTP requests
* Resource access

---

## Build Release Package

Generate a clean release package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1
```

Output:

```text
dist\NVX-Server.zip
```

The package excludes:

* Logs
* Databases
* Runtime downloads
* Generated configuration
* Previous executable builds

---

## Uninstall

1. Close NVX Server
2. Delete:

```text
%LocalAppData%\NVX Server
```

or remove the portable folder.

3. Delete shortcuts:

* Desktop Shortcut
* Start Menu Shortcut

---

## Documentation

Complete technical documentation:

```text
resources\docs\technical-reference.md
```

Includes:

* Internal APIs
* Service Architecture
* Configuration Details
* Runtime Behavior
* Security Model
* Troubleshooting

---

## Novrox Technologies

Developed by **NOVROX**

Website:

https://www.novrox.com

Location:

Sacramento, California, USA

NVX Server is part of the NOVROX technology ecosystem focused on software development, AI solutions, automation systems, data infrastructure, and emerging technologies.
