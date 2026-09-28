using System;
using System.Diagnostics;
using System.IO;

static class NvxRuntimes
{
    public static void Ensure(string root, NvxLog log)
    {
        EnsurePhp(root, log);
        EnsureMysql(root, log);
    }

    public static bool EnsurePhp(string root, NvxLog log)
    {
        if (NvxPrograms.PhpCgi(root).Length > 0) return true;
        log.Server("PHP is not in php\\ yet. Downloading a portable runtime for NVX Server.");
        return RunFetch(root, log, "php");
    }

    public static bool EnsureMysql(string root, NvxLog log)
    {
        if (NvxPrograms.Mysql(root).Length > 0) return true;
        log.Server("MySQL is not in mysql\\bin yet. Downloading a portable MariaDB runtime for NVX Server.");
        return RunFetch(root, log, "mysql");
    }

    static bool RunFetch(string root, NvxLog log, string component)
    {
        string script = Path.Combine(root, "scripts\\fetch-runtimes.ps1");
        if (!File.Exists(script))
        {
            log.Error("Missing scripts\\fetch-runtimes.ps1. Place php-cgi.exe in php\\ and mysqld.exe in mysql\\bin.");
            return false;
        }
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = "powershell.exe";
        info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Component " + component;
        info.WorkingDirectory = root;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        Process process = Process.Start(info);
        string output = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string text = (output + "\r\n" + errors).Trim();
        if (text.Length > 0) log.Server(TrimForLog(text));
        if (process.ExitCode != 0)
        {
            log.Error("Could not install the " + component + " runtime. " + TrimForLog(text));
            return false;
        }
        if (component == "php") return NvxPrograms.PhpCgi(root).Length > 0;
        return NvxPrograms.Mysql(root).Length > 0;
    }

    static string TrimForLog(string text)
    {
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length > 400) text = text.Substring(text.Length - 400);
        return text;
    }
}
