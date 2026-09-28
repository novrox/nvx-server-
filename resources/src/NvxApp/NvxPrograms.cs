using System;
using System.IO;

static class NvxPrograms
{
    public static string Apache(string root)
    {
        return Existing(
            Path.Combine(root, "apache\\bin\\httpd.exe"));
    }

    public static string Mysql(string root)
    {
        return Existing(
            Path.Combine(root, "mysql\\bin\\mysqld.exe"));
    }

    public static string PhpCgi(string root)
    {
        string configured = ConfiguredPhp(root);
        if (configured.Length > 0)
        {
            string directory = Path.GetDirectoryName(configured);
            if (directory != null)
            {
                string beside = Path.Combine(directory, "php-cgi.exe");
                if (File.Exists(beside)) return beside;
            }
            if (configured.EndsWith("php-cgi.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(configured))
            {
                return configured;
            }
        }
        return Existing(
            Path.Combine(root, "php\\php-cgi.exe"),
            Path.Combine(root, "php\\bin\\php-cgi.exe"));
    }

    public static string PhpCli(string root)
    {
        string configured = ConfiguredPhp(root);
        if (configured.Length > 0 && File.Exists(configured)) return configured;
        return Existing(
            Path.Combine(root, "php\\php.exe"),
            Path.Combine(root, "php\\bin\\php.exe"));
    }

    public static string PhpModule(string phpDirectory)
    {
        if (phpDirectory == null || phpDirectory.Length == 0 || !Directory.Exists(phpDirectory)) return "";
        string[] files = Directory.GetFiles(phpDirectory, "php*apache*.dll");
        if (files.Length == 0) return "";
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files[files.Length - 1];
    }

    public static string Home(string executable)
    {
        if (executable == null || executable.Length == 0) return "";
        string directory = Path.GetDirectoryName(executable);
        if (directory == null || directory.Length == 0) return "";
        string name = Path.GetFileName(directory);
        if (name.Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            string parent = Path.GetDirectoryName(directory);
            return parent ?? directory;
        }
        return directory;
    }

    public static string ExtensionDir(string phpFile)
    {
        if (phpFile == null || phpFile.Length == 0) return "";
        string directory = Path.GetDirectoryName(phpFile);
        string[] candidates = new string[]
        {
            directory == null ? "" : Path.Combine(directory, "ext"),
            directory == null ? "" : Path.Combine(Directory.GetParent(directory) == null ? directory : Directory.GetParent(directory).FullName, "ext")
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Length > 0 && Directory.Exists(candidates[i])) return candidates[i];
        }
        return directory == null ? "" : Path.Combine(directory, "ext");
    }

    static string ConfiguredPhp(string root)
    {
        string paths = Path.Combine(root, "config\\paths.sys");
        if (!File.Exists(paths)) return "";
        string[] lines = File.ReadAllLines(paths);
        string configured = "";
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("php_executable")) continue;
            int split = line.IndexOf('=');
            if (split < 0) continue;
            configured = line.Substring(split + 1).Trim().Trim('"');
        }
        if (configured.Length == 0) return "";
        if (!Path.IsPathRooted(configured)) configured = Path.Combine(root, configured.Replace('/', '\\'));
        return configured;
    }

    static string Existing(params string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] != null && paths[i].Length > 0 && File.Exists(paths[i])) return paths[i];
        }
        return "";
    }
}
