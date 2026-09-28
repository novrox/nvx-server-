using System;
using System.IO;
using System.Windows.Forms;

static class SetupProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        bool silent = args.Length > 0 && args[0] == "--silent";
        string source = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        string target = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\NVX Server";
        try
        {
            if (!File.Exists(Path.Combine(source, "nvx.exe")))
            {
                throw new InvalidOperationException("nvx.exe was not found next to the installer.");
            }
            Directory.CreateDirectory(target);
            CopyTree(source, target);
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), target);
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            Directory.CreateDirectory(menu);
            CreateShortcut(menu, target);
            if (!silent)
            {
                MessageBox.Show("NVX Server is installed.\r\n\r\nOpen it from the Start Menu or Desktop.", "NVX Server Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            File.WriteAllText(Path.Combine(target, "install-ok.txt"), "OK " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show(ex.Message, "NVX Server Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try { File.WriteAllText(Path.Combine(source, "tmp\\setup-error.txt"), ex.ToString()); } catch (Exception) { }
            return 1;
        }
    }

    static void CopyTree(string source, string target)
    {
        string[] skipDirs = new string[] { "tmp", "bin", "uninstall", ".git", "engine", "src" };
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(source.Length).TrimStart('\\');
            if (Skip(relative, skipDirs)) continue;
            string dest = Path.Combine(target, relative);
            bool overwrite = relative.Equals("nvx.exe", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("sqlite3.dll", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("nvx-setup.exe", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("README.txt", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(dest) && !overwrite) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(file, dest, true);
        }
    }

    static bool Skip(string relative, string[] skipDirs)
    {
        string lower = relative.ToLowerInvariant();
        if (lower.EndsWith(".log")) return true;
        if (lower.EndsWith(".sqlite")) return true;
        if (lower.EndsWith(".zip")) return true;
        string[] parts = lower.Split('\\');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            for (int s = 0; s < skipDirs.Length; s++)
            {
                if (parts[i] == skipDirs[s]) return true;
            }
        }
        return false;
    }

    static void CreateShortcut(string folder, string target)
    {
        string link = Path.Combine(folder, "NVX Server.lnk");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(target, "nvx.exe") });
        shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { target });
        shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "NVX Server" });
        shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
    }
}
