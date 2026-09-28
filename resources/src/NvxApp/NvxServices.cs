using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

sealed class NvxServices
{
    readonly string _root;
    readonly NvxConfig _config;
    readonly NvxLog _log;
    TcpListener _web;
    Thread _thread;
    DateTime _started;
    volatile bool _listening;
    readonly object _gate = new object();

    public NvxServices(string root, NvxConfig config, NvxLog log)
    {
        _root = root;
        _config = config;
        _log = log;
    }

    public bool WebRunning
    {
        get { return _listening && _web != null; }
    }

    public string Url()
    {
        int port = WebRunning ? ((IPEndPoint)_web.LocalEndpoint).Port : _config.Port("http", 80);
        if (port == 80) return "http://localhost/";
        return "http://localhost:" + port + "/";
    }

    public string Uptime()
    {
        if (!WebRunning) return "Stopped";
        TimeSpan span = DateTime.UtcNow - _started;
        if (span.TotalHours >= 1) return ((int)span.TotalHours) + "h " + span.Minutes + "m";
        if (span.TotalMinutes >= 1) return ((int)span.TotalMinutes) + "m " + span.Seconds + "s";
        return span.Seconds + "s";
    }

    public void StartWeb()
    {
        lock (_gate)
        {
            if (WebRunning) return;
            if (Status("apache") == "Running") StopComponent("apache");
            int requestedPort = _config.Port("http", 80);
            TcpListener listener = BindWebListener(requestedPort);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                if (port != requestedPort)
                {
                    Dictionary<string, string> ports = new Dictionary<string, string>();
                    ports["http"] = port.ToString();
                    _config.Update("ports", ports);
                    _log.Server("HTTP port " + requestedPort + " is in use; NVX selected port " + port + ".");
                }
            }
            catch
            {
                listener.Stop();
                throw;
            }
            _web = listener;
            _listening = true;
            _started = DateTime.UtcNow;
            _thread = new Thread(AcceptLoop);
            _thread.IsBackground = true;
            _thread.Start();
            _log.Server("NVX web service started at " + Url());
        }
    }

    static TcpListener BindWebListener(int requestedPort)
    {
        int[] preferredPorts = new int[] { requestedPort, 8080, 8081, 8000, 8888, 8088 };
        List<int> attempted = new List<int>();
        for (int i = 0; i < preferredPorts.Length; i++)
        {
            int port = preferredPorts[i];
            if (attempted.Contains(port)) continue;
            attempted.Add(port);
            TcpListener listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException)
            {
                listener.Stop();
            }
        }

        TcpListener dynamicListener = new TcpListener(IPAddress.Loopback, 0);
        dynamicListener.Start();
        return dynamicListener;
    }

    public void StopWeb()
    {
        lock (_gate)
        {
            if (_web == null) return;
            _listening = false;
            try { _web.Stop(); } catch (Exception) { }
            _web = null;
            _log.Server("NVX web service stopped.");
        }
    }

    public string StartComponent(string name)
    {
        if (name == "web")
        {
            StartWeb();
            return "Web service is running at " + Url();
        }
        if (name == "apache") return StartApache();
        if (name == "mysql") return StartMysql();
        throw new InvalidOperationException("Unknown service " + name);
    }

    public string StopComponent(string name)
    {
        if (name == "web")
        {
            StopWeb();
            return "Web service stopped.";
        }
        string pidFile = Path.Combine(_root, "tmp\\pids\\" + name + ".pid");
        if (!File.Exists(pidFile)) return name + " is not running.";
        int pid;
        if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out pid)) return name + " is not running.";
        try
        {
            Process process = Process.GetProcessById(pid);
            process.Kill();
            process.WaitForExit(3000);
        }
        catch (Exception) { }
        try { File.Delete(pidFile); } catch (Exception) { }
        _log.Server(name + " stopped.");
        return name + " stopped.";
    }

    public string Status(string name)
    {
        if (name == "web") return WebRunning ? "Running" : "Stopped";
        string pidFile = Path.Combine(_root, "tmp\\pids\\" + name + ".pid");
        if (!File.Exists(pidFile)) return "Stopped";
        int pid;
        if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out pid)) return "Stopped";
        try
        {
            Process process = Process.GetProcessById(pid);
            if (process.HasExited) return "Stopped";
            return "Running";
        }
        catch (Exception)
        {
            return "Stopped";
        }
    }

    public bool ComponentInstalled(string name)
    {
        if (name == "apache") return NvxPrograms.Apache(_root).Length > 0;
        if (name == "mysql") return NvxPrograms.Mysql(_root).Length > 0;
        return true;
    }

    string StartApache()
    {
        string exe = NvxPrograms.Apache(_root);
        if (exe.Length == 0) return "Apache is not installed.";
        if (Status("apache") == "Running") return "Apache is already running.";
        StopWeb();
        Thread.Sleep(300);
        string conf = WriteApacheConfig();
        string apacheDir = Path.GetDirectoryName(exe);
        string work = Directory.Exists(apacheDir) ? apacheDir : _root;
        string message = Launch(exe, work, "-d \"" + NvxPrograms.Home(exe) + "\" -f \"" + conf + "\"", "apache");
        if (Status("apache") != "Running")
        {
            try { StartWeb(); } catch (Exception) { }
            throw new InvalidOperationException(message);
        }
        _log.Server("Apache is serving " + Url());
        return "Apache started.";
    }

    string StartMysql()
    {
        if (NvxPrograms.Mysql(_root).Length == 0) NvxRuntimes.EnsureMysql(_root, _log);
        string exe = NvxPrograms.Mysql(_root);
        if (exe.Length == 0) return "MySQL is not installed. NVX Server could not download mysqld.exe into mysql\\bin.";
        if (Status("mysql") == "Running") return "MySQL is already running.";
        int port = _config.Port("mysql", 3307);
        if (PortOpen(port))
        {
            return "MySQL port " + port + " is already in use.";
        }
        string ini = WriteMysqlConfig();
        string datadir = Path.Combine(_root, "mysql\\data");
        Directory.CreateDirectory(datadir);
        Directory.CreateDirectory(Path.Combine(_root, "mysql\\logs"));
        if (!Directory.Exists(Path.Combine(datadir, "mysql")))
        {
            _log.Server("Initializing the MySQL data directory.");
            string readme = Path.Combine(datadir, "README.txt");
            string readmeHold = Path.Combine(_root, "tmp\\mysql-data-readme.txt");
            if (File.Exists(readme))
            {
                Directory.CreateDirectory(Path.Combine(_root, "tmp"));
                if (File.Exists(readmeHold)) File.Delete(readmeHold);
                File.Move(readme, readmeHold);
            }
            string binDir = Path.GetDirectoryName(exe);
            string installDb = Path.Combine(binDir, "mariadb-install-db.exe");
            if (!File.Exists(installDb)) installDb = Path.Combine(binDir, "mysql_install_db.exe");
            string initMessage;
            if (File.Exists(installDb))
            {
                initMessage = RunWait(installDb, "--datadir=\"" + datadir + "\" --port=" + port + " --password= --config=\"" + ini + "\"");
            }
            else
            {
                initMessage = RunWait(exe, "--defaults-file=\"" + ini + "\" --initialize-insecure");
            }
            if (File.Exists(readmeHold) && !File.Exists(readme)) File.Move(readmeHold, readme);
            if (initMessage.Length > 0) _log.Server(initMessage);
        }
        string home = NvxPrograms.Home(exe);
        string work = Directory.Exists(home) ? home : Path.GetDirectoryName(exe);
        string message = Launch(exe, work, "--defaults-file=\"" + ini + "\" --console", "mysql", 8000);
        if (Status("mysql") != "Running") throw new InvalidOperationException(message);
        _log.Server("MySQL started on 127.0.0.1:" + port + ".");
        return "MySQL started on 127.0.0.1:" + port + ".";
    }

    string Launch(string exe, string work, string arguments, string name)
    {
        return Launch(exe, work, arguments, name, 2000);
    }

    string Launch(string exe, string work, string arguments, string name, int waitMs)
    {
        if (work == null || work.Length == 0 || !Directory.Exists(work)) work = _root;
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = exe;
        info.WorkingDirectory = work;
        info.Arguments = arguments;
        info.CreateNoWindow = true;
        info.UseShellExecute = false;
        Process process = Process.Start(info);
        Directory.CreateDirectory(Path.Combine(_root, "tmp\\pids"));
        File.WriteAllText(Path.Combine(_root, "tmp\\pids\\" + name + ".pid"), process.Id.ToString());
        int waited = 0;
        while (waited < waitMs && !process.HasExited)
        {
            Thread.Sleep(200);
            waited += 200;
        }
        if (process.HasExited)
        {
            try { File.Delete(Path.Combine(_root, "tmp\\pids\\" + name + ".pid")); } catch (Exception) { }
            string log = name == "apache"
                ? Path.Combine(_root, "apache\\logs\\error.log")
                : MysqlLog(exe);
            string detail = File.Exists(log) ? TailFile(log) : "exit " + process.ExitCode;
            return name + " did not stay running. " + detail;
        }
        return name + " started.";
    }

    string RunWait(string exe, string arguments)
    {
        string work = NvxPrograms.Home(exe);
        if (work.Length == 0 || !Directory.Exists(work)) work = _root;
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = exe;
        info.WorkingDirectory = work;
        info.Arguments = arguments;
        info.CreateNoWindow = true;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        Process process = Process.Start(info);
        string output = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        if (!process.HasExited)
        {
            try { process.Kill(); } catch (Exception) { }
            return "Timed out.";
        }
        return (output + "\r\n" + errors).Trim();
    }

    string WriteMysqlConfig()
    {
        Directory.CreateDirectory(Path.Combine(_root, "mysql\\conf"));
        Directory.CreateDirectory(Path.Combine(_root, "mysql\\logs"));
        Directory.CreateDirectory(Path.Combine(_root, "mysql\\data"));
        string template = Path.Combine(_root, "mysql\\conf\\my.ini.template");
        string root = _root.Replace('\\', '/');
        int port = _config.Port("mysql", 3307);
        string text;
        if (File.Exists(template))
        {
            text = File.ReadAllText(template)
                .Replace("__ROOT__", root)
                .Replace("__BIND__", "127.0.0.1")
                .Replace("__PORT__", port.ToString());
        }
        else
        {
            text = "[mysqld]\r\n"
                + "basedir=" + root + "/mysql\r\n"
                + "datadir=" + root + "/mysql/data\r\n"
                + "port=" + port + "\r\n"
                + "bind-address=127.0.0.1\r\n"
                + "character-set-server=utf8mb4\r\n"
                + "collation-server=utf8mb4_unicode_ci\r\n"
                + "log-error=" + root + "/mysql/logs/error.log\r\n"
                + "pid-file=" + root + "/mysql/data/mysql.pid\r\n"
                + "\r\n[client]\r\nport=" + port + "\r\nhost=127.0.0.1\r\n";
        }
        string path = Path.Combine(_root, "mysql\\conf\\my.ini");
        File.WriteAllText(path, text);
        return path;
    }

    static string MysqlLog(string exe)
    {
        string home = NvxPrograms.Home(exe);
        if (home.Length == 0) return "";
        string log = Path.Combine(home, "data\\mysql_error.log");
        if (File.Exists(log)) return log;
        string data = Path.Combine(home, "data");
        if (!Directory.Exists(data)) return "";
        string[] errors = Directory.GetFiles(data, "*.err");
        if (errors.Length == 0) return "";
        return errors[0];
    }

    string WriteApacheConfig()
    {
        Directory.CreateDirectory(Path.Combine(_root, "apache\\conf"));
        Directory.CreateDirectory(Path.Combine(_root, "apache\\logs"));
        string httpd = NvxPrograms.Apache(_root);
        string apacheHome = NvxPrograms.Home(httpd).Replace('\\', '/');
        string php = NvxPrograms.PhpCgi(_root);
        string phpDir = php.Length == 0 ? "" : Path.GetDirectoryName(php);
        string module = NvxPrograms.PhpModule(phpDir);
        if (module.Length == 0 && phpDir.Length > 0)
        {
            string phpHome = NvxPrograms.Home(php);
            module = NvxPrograms.PhpModule(phpHome);
            if (module.Length > 0) phpDir = phpHome;
        }
        string root = _root.Replace('\\', '/');
        int port = _config.Port("http", 80);
        if (apacheHome.Length == 0) apacheHome = root + "/apache";
        string text = ""
            + "ServerRoot \"" + apacheHome + "\"\r\n"
            + "Listen 127.0.0.1:" + port + "\r\n"
            + "ServerName localhost:" + port + "\r\n"
            + "PidFile \"" + root + "/apache/logs/httpd.pid\"\r\n"
            + "ErrorLog \"" + root + "/apache/logs/error.log\"\r\n"
            + "LogLevel warn\r\n"
            + "TypesConfig conf/mime.types\r\n"
            + "LoadModule authz_core_module modules/mod_authz_core.so\r\n"
            + "LoadModule authz_host_module modules/mod_authz_host.so\r\n"
            + "LoadModule dir_module modules/mod_dir.so\r\n"
            + "LoadModule mime_module modules/mod_mime.so\r\n"
            + "LoadModule log_config_module modules/mod_log_config.so\r\n";
        if (module.Length > 0)
        {
            text += "LoadModule php_module \"" + module.Replace('\\', '/') + "\"\r\n"
                + "PHPINIDir \"" + phpDir.Replace('\\', '/') + "\"\r\n"
                + "AddType application/x-httpd-php .php\r\n";
        }
        text += "DirectoryIndex index.php index.html\r\n"
            + "DocumentRoot \"" + root + "/nvxh\"\r\n"
            + "<Directory \"" + root + "/nvxh\">\r\n"
            + "    Options Indexes FollowSymLinks\r\n"
            + "    AllowOverride None\r\n"
            + "    Require local\r\n"
            + "    DirectoryIndex index.php index.html\r\n"
            + "</Directory>\r\n";
        string path = Path.Combine(_root, "apache\\conf\\httpd.conf");
        File.WriteAllText(path, text);
        return path;
    }

    static string TailFile(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0) return "";
        return lines[lines.Length - 1];
    }

    void AcceptLoop()
    {
        while (_listening && _web != null)
        {
            TcpClient client;
            try { client = _web.AcceptTcpClient(); }
            catch (Exception) { return; }
            ThreadPool.QueueUserWorkItem(delegate(object state) { Serve((TcpClient)state); }, client);
        }
    }

    void Serve(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 15000;
            NetworkStream stream = client.GetStream();
            byte[] leftover;
            string header = ReadHeader(stream, out leftover);
            int lineEnd = header.IndexOf("\r\n");
            string line = lineEnd >= 0 ? header.Substring(0, lineEnd) : header;
            string[] parts = line.Split(' ');
            if (parts.Length < 2)
            {
                WriteBytes(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad request"));
                return;
            }
            string method = parts[0].ToUpperInvariant();
            if (method != "GET" && method != "HEAD" && method != "POST" && method != "PUT" && method != "DELETE")
            {
                WriteBytes(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed"));
                return;
            }
            string raw = parts[1];
            string query = "";
            int queryAt = raw.IndexOf('?');
            if (queryAt >= 0)
            {
                query = raw.Substring(queryAt + 1);
                raw = raw.Substring(0, queryAt);
            }
            Dictionary<string, string> headers = ParseHeaders(header);
            int contentLength = 0;
            string lengthValue;
            if (headers.TryGetValue("CONTENT_LENGTH", out lengthValue)) int.TryParse(lengthValue, out contentLength);
            if (contentLength < 0) contentLength = 0;
            if (contentLength > 32 * 1024 * 1024) contentLength = 32 * 1024 * 1024;
            byte[] requestBody = ReadBody(stream, leftover, contentLength);
            byte[] body;
            string type;
            int status = Resolve(raw, query, method, headers, requestBody, out body, out type);
            if (method == "HEAD") WriteBytes(stream, status, type, new byte[0]);
            else WriteBytes(stream, status, type, body);
            if (status == 200) _log.Access(method + " " + raw + " " + status);
        }
        catch (IOException) { }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
        }
        finally
        {
            try { client.Close(); } catch (Exception) { }
        }
    }

    int Resolve(string raw, string query, string method, Dictionary<string, string> headers, byte[] requestBody, out byte[] body, out string type)
    {
        body = Encoding.UTF8.GetBytes("Not found");
        type = "text/plain; charset=utf-8";
        string path;
        try { path = Uri.UnescapeDataString(raw); }
        catch (Exception) { body = Encoding.UTF8.GetBytes("Bad request"); return 400; }
        path = path.Replace('/', '\\');
        if (path.IndexOf("..") >= 0) { body = Encoding.UTF8.GetBytes("Blocked"); return 403; }
        foreach (string segment in path.Split('\\'))
        {
            if (segment.StartsWith(".", StringComparison.Ordinal) && !segment.Equals(".well-known", StringComparison.OrdinalIgnoreCase))
            {
                body = Encoding.UTF8.GetBytes("Blocked");
                return 403;
            }
        }
        string root = Path.GetFullPath(Path.Combine(_root, "nvxh"));
        if (path == "\\" || path.Length == 0) path = "\\";
        if (!path.StartsWith("\\")) path = "\\" + path;
        string full = Path.GetFullPath(root + path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { body = Encoding.UTF8.GetBytes("Blocked"); return 403; }
        string scriptName = raw;
        if (!scriptName.StartsWith("/")) scriptName = "/" + scriptName;
        if (Directory.Exists(full) || path == "\\")
        {
            if (!Directory.Exists(full)) full = root;
            string php = Path.Combine(full, "index.php");
            string html = Path.Combine(full, "index.html");
            if (File.Exists(php))
            {
                full = php;
                if (!scriptName.EndsWith("/")) scriptName += "/";
                if (scriptName == "//") scriptName = "/";
                scriptName += "index.php";
                if (scriptName.StartsWith("//")) scriptName = scriptName.Substring(1);
            }
            else if (File.Exists(html)) full = html;
            else
            {
                body = Encoding.UTF8.GetBytes(Welcome());
                type = "text/html; charset=utf-8";
                return 200;
            }
        }
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext == ".sys" || ext == ".sqlite" || ext == ".sqlite-wal" || ext == ".sqlite-shm" || ext == ".sqlite-journal" || ext == ".db" || ext == ".dll" || ext == ".ini" || ext == ".log" || ext == ".ps1" || ext == ".bat")
        {
            body = Encoding.UTF8.GetBytes("Blocked");
            return 403;
        }
        if (ext == ".php") return RunPhp(full, scriptName, query, method, headers, requestBody, out body, out type);
        if (!File.Exists(full)) return 404;
        body = File.ReadAllBytes(full);
        type = Mime(ext);
        return 200;
    }

    int RunPhp(string scriptFile, string scriptName, string query, string method, Dictionary<string, string> headers, byte[] requestBody, out byte[] body, out string type)
    {
        type = "text/html; charset=utf-8";
        if (NvxPrograms.PhpCgi(_root).Length == 0) NvxRuntimes.EnsurePhp(_root, _log);
        string php = NvxPrograms.PhpCgi(_root);
        if (php.Length == 0)
        {
            body = Encoding.UTF8.GetBytes(PhpMissing());
            _log.Error("php-cgi.exe was not found, so the PHP file could not open.");
            return 503;
        }
        string phpDir = Path.GetDirectoryName(php);
        string ini = WritePhpIni(php);
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = php;
        info.Arguments = "-c \"" + ini + "\"";
        info.WorkingDirectory = Directory.Exists(phpDir) ? phpDir : _root;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        info.EnvironmentVariables["PATH"] = phpDir + ";" + pathEnv;
        info.EnvironmentVariables["PHPRC"] = phpDir;
        info.EnvironmentVariables["PHP_INI_SCAN_DIR"] = "";
        int port = _config.Port("http", 80);
        string root = Path.GetFullPath(Path.Combine(_root, "nvxh"));
        string name = scriptName.Replace('\\', '/').Replace("\r", "").Replace("\n", "");
        query = query.Replace("\r", "").Replace("\n", "");
        if (query.Length > 2048) query = query.Substring(0, 2048);
        if (requestBody == null) requestBody = new byte[0];
        info.EnvironmentVariables["REDIRECT_STATUS"] = "200";
        info.EnvironmentVariables["SCRIPT_FILENAME"] = scriptFile;
        info.EnvironmentVariables["SCRIPT_NAME"] = name;
        info.EnvironmentVariables["PATH_INFO"] = "";
        info.EnvironmentVariables["PATH_TRANSLATED"] = scriptFile;
        info.EnvironmentVariables["REQUEST_URI"] = name + (query.Length > 0 ? "?" + query : "");
        info.EnvironmentVariables["DOCUMENT_ROOT"] = root;
        info.EnvironmentVariables["REQUEST_METHOD"] = method.Length == 0 ? "GET" : method;
        info.EnvironmentVariables["QUERY_STRING"] = query;
        info.EnvironmentVariables["SERVER_NAME"] = "localhost";
        info.EnvironmentVariables["SERVER_PORT"] = port.ToString();
        info.EnvironmentVariables["SERVER_SOFTWARE"] = "NVX-Server/1.0";
        info.EnvironmentVariables["HTTP_HOST"] = port == 80 ? "localhost" : "localhost:" + port;
        info.EnvironmentVariables["SERVER_PROTOCOL"] = "HTTP/1.1";
        info.EnvironmentVariables["GATEWAY_INTERFACE"] = "CGI/1.1";
        info.EnvironmentVariables["REMOTE_ADDR"] = "127.0.0.1";
        info.EnvironmentVariables["CONTENT_LENGTH"] = requestBody.Length.ToString();
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> pair in headers)
            {
                if (pair.Key == "CONTENT_LENGTH") continue;
                info.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }
        Process process = new Process();
        process.StartInfo = info;
        process.ErrorDataReceived += delegate { };
        process.Start();
        process.BeginErrorReadLine();
        if (requestBody.Length > 0)
        {
            process.StandardInput.BaseStream.Write(requestBody, 0, requestBody.Length);
        }
        process.StandardInput.Close();
        byte[] output = ReadStream(process.StandardOutput.BaseStream);
        if (!process.WaitForExit(20000))
        {
            try { process.Kill(); } catch (Exception) { }
            body = Encoding.UTF8.GetBytes("The PHP file took too long to open.");
            _log.Error("PHP timed out for " + scriptName);
            return 500;
        }
        return SplitCgi(output, out body, out type);
    }

    string WritePhpIni(string phpCgi)
    {
        string phpDir = Path.GetDirectoryName(phpCgi);
        string ext = NvxPrograms.ExtensionDir(phpCgi);
        StringBuilder builder = new StringBuilder();
        string preferences = Path.Combine(_root, "php\\conf\\php.ini");
        if (File.Exists(preferences)) builder.AppendLine(File.ReadAllText(preferences).TrimEnd());
        builder.AppendLine("cgi.force_redirect = 0");
        builder.AppendLine("cgi.fix_pathinfo = 1");
        builder.AppendLine("extension_dir = \"" + ext.Replace('\\', '/') + "\"");
        string[] names = new string[] { "pdo_sqlite", "sqlite3", "pdo_mysql", "mysqli", "openssl", "mbstring", "zip", "curl", "fileinfo", "gd" };
        for (int i = 0; i < names.Length; i++)
        {
            if (File.Exists(Path.Combine(ext, "php_" + names[i] + ".dll"))) builder.AppendLine("extension=" + names[i]);
        }
        builder.AppendLine("log_errors = On");
        builder.AppendLine("error_log = \"" + Path.Combine(_root, "php\\logs\\error.log").Replace('\\', '/') + "\"");
        builder.AppendLine("session.save_path = \"" + Path.Combine(_root, "tmp\\sessions").Replace('\\', '/') + "\"");
        builder.AppendLine("sys_temp_dir = \"" + Path.Combine(_root, "tmp").Replace('\\', '/') + "\"");
        builder.AppendLine("upload_tmp_dir = \"" + Path.Combine(_root, "tmp").Replace('\\', '/') + "\"");
        Directory.CreateDirectory(Path.Combine(_root, "tmp\\sessions"));
        Directory.CreateDirectory(Path.Combine(_root, "php\\logs"));
        string path = Path.Combine(_root, "tmp\\php-server.ini");
        File.WriteAllText(path, builder.ToString());
        if (phpDir != null && Directory.Exists(phpDir))
        {
            try { File.WriteAllText(Path.Combine(phpDir, "php.ini"), builder.ToString()); } catch (Exception) { }
        }
        return path;
    }

    static byte[] ReadStream(Stream stream)
    {
        MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int count = stream.Read(chunk, 0, chunk.Length);
            if (count <= 0) break;
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    static int SplitCgi(byte[] output, out byte[] body, out string type)
    {
        type = "text/html; charset=utf-8";
        int status = 200;
        int split = -1;
        for (int i = 0; i + 3 < output.Length; i++)
        {
            if (output[i] == 13 && output[i + 1] == 10 && output[i + 2] == 13 && output[i + 3] == 10)
            {
                split = i;
                break;
            }
        }
        if (split < 0)
        {
            body = output;
            return status;
        }
        string headers = Encoding.ASCII.GetString(output, 0, split);
        body = new byte[output.Length - split - 4];
        Buffer.BlockCopy(output, split + 4, body, 0, body.Length);
        string[] lines = headers.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string key = lines[i].Substring(0, colon).Trim();
            string value = lines[i].Substring(colon + 1).Trim();
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) type = value;
            if (key.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                string code = value.Split(' ')[0];
                int parsed;
                if (int.TryParse(code, out parsed)) status = parsed;
            }
        }
        return status;
    }

    static string ReadHeader(NetworkStream stream, out byte[] leftover)
    {
        leftover = new byte[0];
        MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[512];
        while (buffer.Length < 65536)
        {
            int count = stream.Read(chunk, 0, chunk.Length);
            if (count <= 0) break;
            buffer.Write(chunk, 0, count);
            byte[] data = buffer.ToArray();
            for (int i = 0; i + 3 < data.Length; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                {
                    leftover = new byte[data.Length - i - 4];
                    if (leftover.Length > 0) Buffer.BlockCopy(data, i + 4, leftover, 0, leftover.Length);
                    return Encoding.ASCII.GetString(data, 0, i);
                }
            }
        }
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    static Dictionary<string, string> ParseHeaders(string header)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = header.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string key = lines[i].Substring(0, colon).Trim();
            string value = lines[i].Substring(colon + 1).Trim().Replace("\r", "").Replace("\n", "");
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) values["CONTENT_TYPE"] = value;
            else if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) values["CONTENT_LENGTH"] = value;
            else values["HTTP_" + key.ToUpperInvariant().Replace('-', '_')] = value;
        }
        return values;
    }

    static byte[] ReadBody(NetworkStream stream, byte[] leftover, int length)
    {
        if (length <= 0) return new byte[0];
        byte[] body = new byte[length];
        int copied = 0;
        if (leftover != null && leftover.Length > 0)
        {
            copied = leftover.Length < length ? leftover.Length : length;
            Buffer.BlockCopy(leftover, 0, body, 0, copied);
        }
        while (copied < length)
        {
            int count = stream.Read(body, copied, length - copied);
            if (count <= 0) break;
            copied += count;
        }
        if (copied == length) return body;
        byte[] trimmed = new byte[copied];
        Buffer.BlockCopy(body, 0, trimmed, 0, copied);
        return trimmed;
    }

    static void WriteBytes(NetworkStream stream, int status, string type, byte[] body)
    {
        string reason = "OK";
        if (status == 400) reason = "Bad Request";
        else if (status == 403) reason = "Forbidden";
        else if (status == 404) reason = "Not Found";
        else if (status != 200) reason = "Error";
        string head = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
        byte[] prefix = Encoding.ASCII.GetBytes(head);
        stream.Write(prefix, 0, prefix.Length);
        if (body.Length > 0) stream.Write(body, 0, body.Length);
    }

    string Welcome()
    {
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>NVX Server</title></head><body style=\"margin:0;background:#0c0c0e;color:#f4f4f6;font-family:Segoe UI,sans-serif\"><main style=\"max-width:640px;margin:12vh auto;padding:32px\"><p style=\"color:#39e58c;letter-spacing:.16em;font-size:12px\">NVX SERVER</p><h1>This computer is serving local files.</h1><p>Open the NVX Server window to turn services on and off.</p></main></body></html>";
    }

    string PhpMissing()
    {
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>NVX Server</title></head><body style=\"margin:0;background:#0c0c0e;color:#f4f4f6;font-family:Segoe UI,sans-serif\"><main style=\"max-width:640px;margin:12vh auto;padding:32px\"><p style=\"color:#39e58c;letter-spacing:.16em;font-size:12px\">NVX SERVER</p><h1>PHP is not installed in this folder.</h1><p>NVX Server runs PHP itself. It does not use XAMPP. Run scripts\\fetch-runtimes.ps1 or place php-cgi.exe in php\\, then restart NVX Server.</p></main></body></html>";
    }

    static string Mime(string ext)
    {
        if (ext == ".html" || ext == ".htm") return "text/html; charset=utf-8";
        if (ext == ".css") return "text/css; charset=utf-8";
        if (ext == ".js") return "application/javascript; charset=utf-8";
        if (ext == ".svg") return "image/svg+xml";
        if (ext == ".png") return "image/png";
        if (ext == ".jpg" || ext == ".jpeg") return "image/jpeg";
        if (ext == ".gif") return "image/gif";
        if (ext == ".json") return "application/json; charset=utf-8";
        if (ext == ".txt") return "text/plain; charset=utf-8";
        return "application/octet-stream";
    }

    public static bool PortOpen(int port)
    {
        IPEndPoint[] listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i].Port == port) return true;
        }
        return false;
    }
}
