using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test") return SelfTest();
        bool created;
        Mutex mutex = new Mutex(true, "Local\\NVX.Server.SingleInstance", out created);
        if (!created)
        {
            FocusExisting();
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
        return 0;
    }

    static void FocusExisting()
    {
        Process current = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName(current.ProcessName);
        for (int i = 0; i < processes.Length; i++)
        {
            if (processes[i].Id == current.Id) continue;
            if (processes[i].MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(processes[i].MainWindowHandle, 9);
            SetForegroundWindow(processes[i].MainWindowHandle);
            return;
        }
    }

    static int SelfTest()
    {
        string root = NvxPaths.Root();
        string report = Path.Combine(root, "tmp\\self-test.txt");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tmp"));
            NvxDatabase databases = new NvxDatabase(root);
            if (File.Exists(databases.PathOf("selftest"))) databases.Drop("selftest", "selftest");
            databases.Create("selftest");
            List<string[]> columns = new List<string[]>();
            columns.Add(new string[] { "title", "TEXT" });
            databases.CreateTable("selftest", "items", columns);
            databases.Execute("selftest", "INSERT INTO items (title) VALUES ('hello')");
            NvxRowSet rows = databases.Query("selftest", "SELECT title FROM items");
            if (rows.Rows.Count != 1 || rows.Rows[0][0] != "hello") throw new InvalidOperationException("Database check failed.");
            databases.Drop("selftest", "selftest");
            TestWebPortFallback();
            File.WriteAllText(report, "OK");
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(report, ex.ToString()); } catch (Exception) { }
            return 1;
        }
    }

    static void TestWebPortFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), "nvx-web-selftest-" + Guid.NewGuid().ToString("N"));
        TcpListener occupied = null;
        NvxServices web = null;
        try
        {
            Directory.CreateDirectory(root);
            NvxConfig config = new NvxConfig(root);
            occupied = new TcpListener(IPAddress.Loopback, 0);
            occupied.Start();
            int busyPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
            Dictionary<string, string> ports = new Dictionary<string, string>();
            ports["http"] = busyPort.ToString();
            config.Update("ports", ports);

            web = new NvxServices(root, config, new NvxLog(root));
            web.StartWeb();
            int selectedPort = config.Port("http", 80);
            if (!web.WebRunning || selectedPort == busyPort || !NvxServices.PortOpen(selectedPort))
            {
                throw new InvalidOperationException("Web port fallback check failed.");
            }
        }
        finally
        {
            if (web != null) web.StopWeb();
            if (occupied != null) occupied.Stop();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
        }
    }
}
