using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OverFieldLauncher {
    internal static class Program {
        [STAThread] internal static int Main(string[] args) {
            if (args.Length == 3 && args[0] == "--stop") {
                try { ConsoleStop.Stop(AppDomain.CurrentDomain.BaseDirectory, args[1]); File.WriteAllText(args[2], "Stopped after normal Ctrl+C."); return 0; }
                catch (Exception ex) { File.WriteAllText(args[2], ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool smoke = args.Contains("--smoke-test");
            try {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                if (!File.Exists(Path.Combine(root, "Start-Server.ps1")))
                    throw new Exception("请将启动器放在 private-server 目录，与 Start-Server.ps1 放在一起。");
                using (var mutex = new Mutex(false, "Local\\OverFieldServerLauncher")) {
                    if (!mutex.WaitOne(0)) { MessageBox.Show("启动器已经打开，请查看任务栏。", "开放空间服务器"); return 1; }
                    try {
                        using (var form = new LauncherForm(root)) {
                            if (smoke) {
                                var timer = new System.Windows.Forms.Timer { Interval = 500 };
                                timer.Tick += delegate { timer.Stop(); form.Close(); timer.Dispose(); };
                                form.Shown += delegate { timer.Start(); };
                            }
                            Application.Run(form);
                        }
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                if (smoke) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher-smoke-error.log"), ex.ToString()); }
                else MessageBox.Show(ex.Message, "启动器错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class ConsoleStop {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)] static extern bool FreeConsole();
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)] static extern bool AttachConsole(uint pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] ids, uint size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint evt, uint group);
        internal static void Stop(string root, string service) {
            if (service != "Server" && service != "Bridge") throw new Exception("Invalid service.");
            string exe = Path.GetFullPath(Path.Combine(root, service == "Server" ? "Lolo/bin/Lolo_windows_amd64.exe" : "TcpBridge/bin/OverField.TcpBridge.exe"));
            var matches = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Where(p => String.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return;
            if (matches.Length != 1) throw new Exception("Multiple matching processes; no signal sent.");
            using (var target = matches[0]) {
                var allowed = new System.Collections.Generic.HashSet<uint> { (uint)Process.GetCurrentProcess().Id, (uint)target.Id };
                uint current = (uint)target.Id;
                for (int depth=0; depth<8; depth++) {
                    using (var search = new System.Management.ManagementObjectSearcher("SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + current)) {
                        using (var results = search.Get()) {
                            var row = results.Cast<System.Management.ManagementObject>().FirstOrDefault();
                            if (row == null) break;
                            using (row) { current = (uint)row["ParentProcessId"]; }
                        }
                    }
                    try { using (var parent = Process.GetProcessById((int)current)) {
                        if (parent.ProcessName != "powershell" && parent.ProcessName != "pwsh") break;
                        allowed.Add(current);
                    } } catch (ArgumentException) { break; }
                }
                FreeConsole();
                if (!AttachConsole((uint)target.Id)) throw new Exception("Cannot attach server console; use Ctrl+C in its console.");
                try {
                    uint[] ids = new uint[64]; uint count = GetConsoleProcessList(ids,64);
                    if (count == 0 || count > 64 || ids.Take((int)count).Any(id => !allowed.Contains(id))) throw new Exception("Unrelated processes share the console; no signal sent. Use Ctrl+C in the server console.");
                    SetConsoleCtrlHandler(IntPtr.Zero,true);
                    if (!GenerateConsoleCtrlEvent(0,0)) throw new Exception("Ctrl+C signal failed.");
                } finally { FreeConsole(); }
                if (!target.WaitForExit(15000)) throw new Exception("Still waiting for shutdown; no force-kill performed. Check server console.");
            }
        }
    }

    internal sealed class LauncherForm : Form {
        readonly string root;
        readonly Label serverStatus = new Label(), bridgeStatus = new Label();
        readonly TextBox log = new TextBox();
        readonly Button local = new Button(), online = new Button(), stop = new Button();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool busy;
        string ServerExe { get { return Path.GetFullPath(Path.Combine(root, "Lolo/bin/Lolo_windows_amd64.exe")); } }
        string BridgeExe { get { return Path.GetFullPath(Path.Combine(root, "TcpBridge/bin/OverField.TcpBridge.exe")); } }

        internal LauncherForm(string directory) {
            root = directory;
            Text = "开放空间 · 服务器启动器";
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(700, 510);
            MinimumSize = MaximumSize = Size;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            var title = new Label { Text = "开放空间服务器", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true, Location = new Point(26, 22) };
            Controls.Add(title);
            AddLabel("本地游玩直接启动；朋友联机同时启动加密桥接。", 28, 72, 640);
            serverStatus.SetBounds(28, 110, 640, 26); Controls.Add(serverStatus);
            bridgeStatus.SetBounds(28, 142, 640, 26); Controls.Add(bridgeStatus);
            ConfigureButton(local, "启动本地服务器", 28, 192, 200);
            ConfigureButton(online, "启动服务器 + 联机桥接", 240, 192, 250);
            ConfigureButton(stop, "正常关闭全部", 502, 192, 168);
            local.Click += async delegate { await Operate(false, false); };
            online.Click += async delegate { await Operate(true, false); };
            stop.Click += async delegate { await Operate(false, true); };
            var folder = new Button(); ConfigureButton(folder, "打开服务目录", 28, 246, 150);
            folder.Click += delegate { Process.Start("explorer.exe", Quote(root)); };
            AddLabel("关闭此窗口会保留服务器运行。关服请点击“正常关闭全部”。", 194, 252, 485);
            log.SetBounds(28, 302, 642, 170);
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.White; log.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(log);
            AddLabel("服务日志显示在独立控制台；需要联机时请先启动你自己的 FRP 映射。", 28, 481, 655);
            timer.Interval = 2000;
            timer.Tick += delegate { if (!busy) RefreshStatus(); };
            Shown += delegate { RefreshStatus(); timer.Start(); Append("已就绪。启动器不会自动更改配置或存档。"); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; Append("正在操作，请等待完成后再关闭窗口。"); } };
            FormClosed += delegate { timer.Dispose(); };
        }
        void AddLabel(string text, int x, int y, int width) {
            Controls.Add(new Label { Text = text, Location = new Point(x,y), Size = new Size(width, 25), ForeColor = Color.FromArgb(85,95,110) });
        }
        void ConfigureButton(Button b, string text, int x, int y, int width) {
            b.Text = text; b.SetBounds(x,y,width,42); b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.White; Controls.Add(b);
        }
        void Append(string message) { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }
        static string Quote(string value) { return "\"" + value + "\""; }
        static int? FindProcess(string path) {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path))) {
                using (process) {
                    // If the path cannot be verified, fail rather than risk stopping another service.
                    if (String.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) return process.Id;
                }
            }
            return null;
        }
        int[] Ports() {
            var json = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(root, "Lolo/config.json"))) as System.Collections.Generic.Dictionary<string, object>;
            if (json == null) throw new Exception("Lolo/config.json 格式无效。");
            var ports = new[] { ReadPort(json, "HttpNet", "InnerPort"), ReadPort(json, "GateWay", "OuterPort"), ReadPort(json, "LogServer", "OuterPort") };
            if (ports.Any(p => p < 1 || p > 65535) || ports.Distinct().Count() != 3) throw new Exception("服务端口配置无效或重复。");
            return ports;
        }
        static int ReadPort(System.Collections.Generic.Dictionary<string,object> json, string section, string key) {
            return Convert.ToInt32(((System.Collections.Generic.Dictionary<string,object>)json[section])[key]);
        }
        static bool Listening(int port) { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port); }
        void RefreshStatus() {
            try {
                var pid = FindProcess(ServerExe);
                var ready = pid.HasValue && Ports().All(Listening);
                serverStatus.Text = "本地服务器：" + (pid.HasValue ? (ready ? "运行中" : "初始化中 / 端口未就绪") + "  · PID " + pid : "未启动");
                serverStatus.ForeColor = ready ? Color.DarkGreen : Color.FromArgb(85,95,110);
                var bridge = FindProcess(BridgeExe);
                bridgeStatus.Text = "联机桥接：" + (bridge.HasValue ? (Listening(10086) ? "运行中" : "端口未就绪") + "  · PID " + bridge : "未启动");
                bridgeStatus.ForeColor = bridge.HasValue && Listening(10086) ? Color.DarkGreen : Color.FromArgb(85,95,110);
            } catch (Exception ex) { serverStatus.Text = "状态检查失败：" + ex.Message; }
        }
        void RequireFile(string path) { if (!File.Exists(path)) throw new Exception("缺少文件：" + path); }
        void StartScript(string name) {
            string script = Path.Combine(root, name); RequireFile(script);
            string runner = Path.Combine(root, "Launcher/Run-Console.ps1"); RequireFile(runner);
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Quote(runner) + " -ScriptName " + Quote(name)) { WorkingDirectory = root, UseShellExecute = true });
        }
        async Task WaitReady(string exe, int[] ports) {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            bool observed = false;
            while (DateTime.UtcNow < deadline) {
                bool running = FindProcess(exe).HasValue; observed |= running;
                if (running && ports.All(Listening)) return;
                if (observed && !running) throw new Exception("进程启动后退出，请查看服务控制台中的错误。");
                await Task.Delay(500);
            }
            throw new Exception("等待启动超过60秒，请查看服务控制台。启动器未终止进程。");
        }
        async Task StopService(string service, string exe) {
            if (!FindProcess(exe).HasValue) return;
            string resultPath = Path.Combine(root, "launcher-stop-" + Guid.NewGuid().ToString("N") + ".log");
            string result = await Task.Run(delegate {
                using (var p = new Process()) {
                    p.StartInfo = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--stop " + service + " " + Quote(resultPath)) {
                        WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
                    };
                    p.Start(); p.WaitForExit();
                    string text = File.Exists(resultPath) ? File.ReadAllText(resultPath) : "关服辅助程序未返回结果。";
                    if (p.ExitCode != 0 || !File.Exists(resultPath)) throw new Exception(text);
                    return text.Trim();
                }
            });
            Append(result);
        }
        async Task Operate(bool withBridge, bool stopping) {
            if (busy) return;
            busy = true; local.Enabled = online.Enabled = stop.Enabled = false;
            try {
                if (stopping) {
                    Append("正在发送正常关服信号，等待服务端完成存档……");
                    await StopService("Bridge", BridgeExe);
                    await StopService("Server", ServerExe);
                    Append("服务已停止。服务端控制台应显示退出存档完成。");
                } else {
                    RequireFile(ServerExe); RequireFile(Path.Combine(root,"Lolo/config.json"));
                    int[] ports = Ports();
                    if (withBridge) {
                        RequireFile(BridgeExe); RequireFile(Path.Combine(root, "TcpBridge/server.json"));
                        if (!ports.SequenceEqual(new[] {18080,11000,12000})) throw new Exception("当前桥接要求服务端端口为18080、11000、12000，请检查配置。");
                        if (!FindProcess(BridgeExe).HasValue && Listening(10086)) throw new Exception("桥接端口10086已被其他程序占用。");
                    }
                    if (!FindProcess(ServerExe).HasValue) {
                        if (ports.Any(Listening)) throw new Exception("服务端口已被其他程序占用，请先关闭占用程序。");
                        Append("正在启动本地服务器……"); StartScript("Start-Server.ps1");
                    }
                    await WaitReady(ServerExe, ports); Append("本地服务器已就绪。");
                    if (withBridge) {
                        if (!FindProcess(BridgeExe).HasValue) { Append("正在启动联机桥接……"); StartScript("Start-PublicBridge.ps1"); }
                        await WaitReady(BridgeExe, new[] {10086}); Append("桥接已就绪。朋友连接还需要 FRP 映射正常运行。");
                    }
                }
            } catch (Exception ex) { Append("操作失败：" + ex.Message); }
            finally { busy = false; local.Enabled = online.Enabled = stop.Enabled = true; RefreshStatus(); }
        }
    }
}
