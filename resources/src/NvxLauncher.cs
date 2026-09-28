using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

class NvxLauncher
{
    static int Main(string[] args)
    {
        bool pause = true;
#if NVX_LAUNCH
        pause = false;
#endif
        var forwarded = new List<string>();
        foreach (string arg in args)
        {
            if (arg == "--no-pause")
            {
                pause = false;
                continue;
            }
            forwarded.Add(arg);
        }

        string command = BakedCommand();
        if (command == "uninstall" && !Contains(forwarded, "--yes"))
        {
            Console.WriteLine("This stops NVX Server, deletes local databases, and resets settings.");
            Console.WriteLine("Program files stay in this folder until you delete it.");
            Console.Write("Type YES to continue: ");
            string typed = Console.ReadLine();
            if (typed == null || typed.Trim() != "YES")
            {
                Console.WriteLine("Uninstall cancelled.");
                return Pause(pause, 1);
            }
            forwarded.Add("--yes");
        }

        string root = FindRoot();
        string php = FindPhp(root);
        if (php == null)
        {
            Console.Error.WriteLine("PHP runtime not found. Place php.exe in php\\bin.");
            return Pause(pause, 1);
        }

        string cli = Path.Combine(root, "resources", "engine", "cli.php");
        var start = new ProcessStartInfo();
        start.FileName = php;
        start.WorkingDirectory = root;
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;

        string arguments = Quote(cli);
        if (!string.IsNullOrEmpty(command))
        {
            arguments += " " + command;
        }
        else if (forwarded.Count == 0)
        {
            arguments += " status";
        }
        foreach (string arg in forwarded)
        {
            arguments += " " + Quote(arg);
        }
        start.Arguments = arguments;

        Process process = Process.Start(start);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        string output = stdout.ToString();
        string errors = stderr.ToString();
        if (output.Length > 0) Console.Write(output);
        if (errors.Length > 0) Console.Error.Write(errors);

        if (command == "control" && process.ExitCode == 0)
        {
            string url = ExtractUrl(output);
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    Process.Start(url);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Open this address in a browser: " + url);
                    Console.Error.WriteLine(ex.Message);
                }
            }
        }

        if (command == "control" && process.ExitCode != 0)
        {
            pause = true;
        }

        return Pause(pause, process.ExitCode);
    }

    static string BakedCommand()
    {
#if NVX_START
        return "start";
#elif NVX_STOP
        return "stop";
#elif NVX_RESTART
        return "restart";
#elif NVX_CONTROL
        return "control";
#elif NVX_LAUNCH
        return "control";
#elif NVX_UNINSTALL
        return "uninstall";
#else
        return null;
#endif
    }

    static string FindRoot()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var info = new DirectoryInfo(baseDir);
        string name = info.Name;
        if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(baseDir, ".."));
        }
        return Path.GetFullPath(baseDir);
    }

    static string FindPhp(string root)
    {
        string[] candidates = new string[]
        {
            Path.Combine(root, "php", "bin", "php.exe"),
            Path.Combine(root, "php", "php.exe")
        };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static string ExtractUrl(string stdout)
    {
        string[] lines = stdout.Split(new char[] { '\r', '\n' });
        const string prefix = "CONTROL_URL=";
        foreach (string line in lines)
        {
            if (line.StartsWith(prefix)) return line.Substring(prefix.Length).Trim();
        }
        return null;
    }

    static string Quote(string value)
    {
        if (value.IndexOf('"') >= 0) value = value.Replace("\"", "\\\"");
        if (value.IndexOf(' ') >= 0 || value.IndexOf('"') >= 0) return "\"" + value + "\"";
        return value;
    }

    static bool Contains(List<string> items, string value)
    {
        foreach (string item in items)
        {
            if (item == value) return true;
        }
        return false;
    }

    static int Pause(bool pause, int code)
    {
        if (pause)
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return code;
    }
}
