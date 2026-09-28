using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class NvxPaths
{
    public static string Root()
    {
        return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
    }

    public static string Combine(string root, string relative)
    {
        string path = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, path);
    }
}

sealed class NvxConfig
{
    readonly string _root;
    readonly Dictionary<string, Dictionary<string, string>> _files = new Dictionary<string, Dictionary<string, string>>();

    static readonly Dictionary<string, string> Map = new Dictionary<string, string>();

    static NvxConfig()
    {
        Map["server"] = "config\\server.sys";
        Map["ports"] = "config\\ports.sys";
        Map["paths"] = "config\\paths.sys";
        Map["services"] = "config\\services.sys";
        Map["security"] = "config\\security.sys";
        Map["apache"] = "services\\apache.sys";
        Map["mysql"] = "services\\mysql.sys";
    }

    public NvxConfig(string root)
    {
        _root = root;
        EnsureDefaults();
    }

    public string Get(string file, string key, string fallback)
    {
        Dictionary<string, string> values = Read(file);
        string value;
        if (values.TryGetValue(key, out value)) return value;
        return fallback;
    }

    public void Update(string file, Dictionary<string, string> changes)
    {
        Dictionary<string, string> values = Read(file);
        foreach (KeyValuePair<string, string> pair in changes)
        {
            values[pair.Key] = pair.Value.Replace("\r", "").Replace("\n", "");
        }
        Write(file, values);
    }

    public int Port(string key, int fallback)
    {
        string raw = Get("ports", key, fallback.ToString());
        int port;
        if (!int.TryParse(raw, out port) || port < 1 || port > 65535) return fallback;
        return port;
    }

    Dictionary<string, string> Read(string name)
    {
        Dictionary<string, string> cached;
        if (_files.TryGetValue(name, out cached)) return cached;
        Dictionary<string, string> values = new Dictionary<string, string>();
        string path = FilePath(name);
        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                string key = line.Substring(0, split).Trim();
                string value = line.Substring(split + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
                values[key] = value;
            }
        }
        _files[name] = values;
        return values;
    }

    void Write(string name, Dictionary<string, string> values)
    {
        string path = FilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        StringBuilder builder = new StringBuilder();
        builder.Append("; NVX Server ").Append(name).Append("\r\n");
        foreach (KeyValuePair<string, string> pair in values)
        {
            builder.Append(pair.Key).Append(" = \"").Append(pair.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"\r\n");
        }
        File.WriteAllText(path, builder.ToString());
        _files[name] = values;
    }

    string FilePath(string name)
    {
        string relative;
        if (!Map.TryGetValue(name, out relative)) throw new InvalidOperationException("Unknown configuration " + name);
        return Path.Combine(_root, relative);
    }

    void EnsureDefaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        Directory.CreateDirectory(Path.Combine(_root, "services"));
        Directory.CreateDirectory(Path.Combine(_root, "data\\databases"));
        Directory.CreateDirectory(Path.Combine(_root, "data\\uploads"));
        Directory.CreateDirectory(Path.Combine(_root, "data\\user-data"));
        Directory.CreateDirectory(Path.Combine(_root, "data\\backups"));
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
        Directory.CreateDirectory(Path.Combine(_root, "nvxh\\apps"));
        Directory.CreateDirectory(Path.Combine(_root, "tmp"));
        if (!File.Exists(FilePath("server")))
        {
            Dictionary<string, string> server = new Dictionary<string, string>();
            server["name"] = "NVX Server";
            server["version"] = "1.0.0";
            server["document_root"] = "nvxh";
            server["bind"] = "127.0.0.1";
            server["timezone"] = "UTC";
            Write("server", server);
        }
        if (!File.Exists(FilePath("ports")))
        {
            Dictionary<string, string> ports = new Dictionary<string, string>();
            ports["http"] = "80";
            ports["https"] = "8443";
            ports["mysql"] = "3307";
            ports["ftp"] = "21";
            ports["mail"] = "25";
            Write("ports", ports);
        }
    }
}

sealed class NvxLog
{
    readonly string _root;
    readonly object _gate = new object();

    public NvxLog(string root)
    {
        _root = root;
    }

    public void Server(string message) { Write("logs\\server.log", message); }
    public void Error(string message) { Write("logs\\error.log", message); }
    public void Access(string message) { Write("logs\\access.log", message); }

    public string Story(int maxLines)
    {
        lock (_gate)
        {
            List<string> lines = new List<string>();
            Collect(lines, "logs\\server.log", false);
            Collect(lines, "logs\\error.log", true);
            lines.Sort(StringComparer.Ordinal);
            int start = Math.Max(0, lines.Count - maxLines);
            StringBuilder builder = new StringBuilder();
            for (int i = start; i < lines.Count; i++)
            {
                if (builder.Length > 0) builder.Append("\r\n");
                builder.Append(lines[i]);
            }
            return builder.ToString();
        }
    }

    void Collect(List<string> lines, string relative, bool error)
    {
        string path = Path.Combine(_root, relative);
        if (!File.Exists(path)) return;
        string[] fileLines = File.ReadAllLines(path);
        for (int i = 0; i < fileLines.Length; i++)
        {
            string line = fileLines[i];
            if (line.Length == 0) continue;
            if (error)
            {
                int end = line.IndexOf(']');
                if (end > 0) line = line.Substring(0, end + 1) + " ERROR" + line.Substring(end + 1);
                else line = "ERROR " + line;
            }
            lines.Add(line);
        }
    }

    void Write(string relative, string message)
    {
        lock (_gate)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string line = "[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC] " + message.Replace("\r", " ").Replace("\n", " ") + "\r\n";
            File.AppendAllText(path, line);
        }
    }
}
