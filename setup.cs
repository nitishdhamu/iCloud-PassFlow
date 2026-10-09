using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace iCloudPassFlowSetup
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool createdNew = false;
            using (Mutex mutex = new Mutex(true, @"Local\iCloudPassFlow_Setup_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                bool isSilent = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].Trim().ToLowerInvariant();
                    if (a == "--silent" || a == "/silent" || a == "/s" || a == "-s")
                    {
                        isSilent = true;
                        break;
                    }
                }

                if (isSilent)
                {
                    try
                    {
                        Installer.Install(null);
                        return 0;
                    }
                    catch
                    {
                        return 1;
                    }
                }
                else
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (SetupForm form = new SetupForm())
                    {
                        Application.Run(form);
                        return form.ExitCode;
                    }
                }
            }
        }
    }

    public class SetupForm : Form
    {
        private Label lblTitle;
        private Label lblStatus;
        private ProgressBar progressBar;
        public int ExitCode { get; private set; }

        public SetupForm()
        {
            this.ExitCode = 0;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "iCloud PassFlow Setup";
            this.ClientSize = new Size(420, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = SystemColors.Window;
            this.Font = new Font("Segoe UI", 9f);

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            lblTitle = new Label();
            lblTitle.Text = "Installing iCloud PassFlow...";
            lblTitle.Font = new Font("Segoe UI", 11.25f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(30, 30, 30);
            lblTitle.Location = new Point(24, 20);
            lblTitle.AutoSize = true;

            lblStatus = new Label();
            lblStatus.Text = "Preparing installation...";
            lblStatus.Font = new Font("Segoe UI", 9f);
            lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
            lblStatus.Location = new Point(24, 48);
            lblStatus.AutoSize = true;

            progressBar = new ProgressBar();
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 25;
            progressBar.Location = new Point(24, 80);
            progressBar.Size = new Size(372, 22);

            this.Controls.Add(lblTitle);
            this.Controls.Add(lblStatus);
            this.Controls.Add(progressBar);

            this.Shown += SetupForm_Shown;
        }

        private void SetupForm_Shown(object sender, EventArgs e)
        {
            Thread t = new Thread(RunInstall);
            t.IsBackground = true;
            t.Start();
        }

        private void RunInstall()
        {
            try
            {
                Installer.Install(UpdateStatus);
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        lblTitle.Text = "Installation Complete!";
                        lblStatus.Text = "iCloud PassFlow is now running in the background.";
                        progressBar.Style = ProgressBarStyle.Blocks;
                        progressBar.Value = 100;
                    });
                }
                Thread.Sleep(1200);
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        this.Close();
                    });
                }
            }
            catch (Exception ex)
            {
                this.ExitCode = 1;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(this, "An error occurred during installation:\n\n" + ex.Message,
                            "Installation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                    });
                }
            }
        }

        private void UpdateStatus(string status)
        {
            if (!this.IsDisposed && this.IsHandleCreated)
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    lblStatus.Text = status;
                });
            }
        }
    }

    public static class Installer
    {
        public static void Install(Action<string> statusCallback)
        {
            Report(statusCallback, "Stopping existing instances...");
            KillExistingProcesses();

            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\iCloud-PassFlow");

            Report(statusCallback, "Extracting application files...");
            ExtractPayload(installDir);

            string exePath = Path.Combine(installDir, "iCloudPassFlow.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("iCloudPassFlow.exe was not found in " + installDir);
            }

            string uninstallPath = Path.Combine(installDir, "uninstall.exe");

            Report(statusCallback, "Configuring instant startup...");
            ConfigureStartup(installDir, exePath);

            Report(statusCallback, "Registering uninstaller in Windows Settings...");
            ConfigureUninstall(installDir, exePath, uninstallPath);

            Report(statusCallback, "Starting iCloud PassFlow...");
            StartApplication(installDir, exePath);
        }

        private static void Report(Action<string> callback, string message)
        {
            if (callback != null)
            {
                callback(message);
            }
        }

        private static void KillExistingProcesses()
        {
            string[] names = new string[] { "iCloudPassFlow", "iCloud-PassFlow" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(names[i]);
                    for (int j = 0; j < procs.Length; j++)
                    {
                        try
                        {
                            procs[j].Kill();
                            procs[j].WaitForExit(2000);
                        }
                        catch { }
                        finally
                        {
                            try { procs[j].Dispose(); } catch { }
                        }
                    }
                }
                catch { }
            }
            Thread.Sleep(250);
        }

        private static void ExtractPayload(string installDir)
        {
            if (Directory.Exists(installDir))
            {
                for (int retry = 0; retry < 5; retry++)
                {
                    try
                    {
                        Directory.Delete(installDir, true);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }

            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            Assembly asm = Assembly.GetExecutingAssembly();
            string resName = null;
            string[] names = asm.GetManifestResourceNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].IndexOf("Payload", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    resName = names[i];
                    break;
                }
            }

            if (string.IsNullOrEmpty(resName))
            {
                throw new InvalidOperationException("Embedded installer payload was not found.");
            }

            using (Stream s = asm.GetManifestResourceStream(resName))
            {
                if (s == null)
                {
                    throw new InvalidOperationException("Could not read embedded payload stream.");
                }

                using (ZipArchive archive = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    string targetBase = Path.GetFullPath(installDir);
                    if (!targetBase.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        targetBase += Path.DirectorySeparatorChar;
                    }

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
                        if (!fullPath.StartsWith(targetBase, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            if (!Directory.Exists(fullPath))
                            {
                                Directory.CreateDirectory(fullPath);
                            }
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(fullPath);
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            for (int retry = 0; retry < 5; retry++)
                            {
                                try
                                {
                                    entry.ExtractToFile(fullPath, true);
                                    break;
                                }
                                catch (Exception)
                                {
                                    if (retry == 4) throw;
                                    Thread.Sleep(200);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void ConfigureStartup(string installDir, string exePath)
        {
            string[] runSubKeys = new string[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
            };
            for (int i = 0; i < runSubKeys.Length; i++)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runSubKeys[i], true))
                    {
                        if (key != null)
                        {
                            try { key.DeleteValue("iCloud-PassFlow", false); } catch { }
                            try { key.DeleteValue("iCloudPassFlow", false); } catch { }
                        }
                    }
                }
                catch { }
            }

            string[] saSubKeys = new string[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
            };
            for (int i = 0; i < saSubKeys.Length; i++)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(saSubKeys[i], true))
                    {
                        if (key != null)
                        {
                            try { key.DeleteValue("iCloud-PassFlow", false); } catch { }
                            try { key.DeleteValue("iCloud-PassFlow.lnk", false); } catch { }
                            try { key.DeleteValue("iCloudPassFlow", false); } catch { }
                            try { key.DeleteValue("iCloudPassFlow.lnk", false); } catch { }
                        }
                    }
                }
                catch { }
            }

            // Fastest zero-admin startup method: Task Scheduler ONLOGON scoped to current user
            bool taskRegistered = false;
            try
            {
                Type schedType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedType != null)
                {
                    dynamic service = Activator.CreateInstance(schedType);
                    service.Connect();
                    dynamic root = service.GetFolder("\\");
                    dynamic taskDef = service.NewTask(0);

                    taskDef.Settings.DisallowStartIfOnBatteries = false;
                    taskDef.Settings.StopIfGoingOnBatteries = false;
                    taskDef.Settings.ExecutionTimeLimit = "PT0S";
                    taskDef.Settings.Priority = 4;

                    dynamic trigger = taskDef.Triggers.Create(9); // TASK_TRIGGER_LOGON
                    trigger.UserId = WindowsIdentity.GetCurrent().Name;

                    dynamic action = taskDef.Actions.Create(0); // TASK_ACTION_EXEC
                    action.Path = exePath;
                    action.WorkingDirectory = installDir;

                    root.RegisterTaskDefinition("iCloud-PassFlow", taskDef, 6, null, null, 3);
                    taskRegistered = true;
                }
            }
            catch { }

            // Fallback to Registry Run if Task Scheduler service is disabled
            if (!taskRegistered)
            {
                using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (runKey != null)
                    {
                        runKey.SetValue("iCloud-PassFlow", "\"" + exePath + "\"");
                    }
                }
            }
        }

        private static void ConfigureUninstall(string installDir, string exePath, string uninstallPath)
        {
            string uninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloud-PassFlow";
            using (RegistryKey uKey = Registry.CurrentUser.CreateSubKey(uninstallKeyPath))
            {
                if (uKey != null)
                {
                    uKey.SetValue("DisplayName", "iCloud PassFlow");
                    uKey.SetValue("DisplayVersion", "1.0.0");
                    uKey.SetValue("Publisher", "Nitish Dhamu");
                    uKey.SetValue("DisplayIcon", exePath + ",0");
                    uKey.SetValue("InstallLocation", installDir);
                    uKey.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                    long totalBytes = 0;
                    try
                    {
                        string[] files = Directory.GetFiles(installDir, "*.*", SearchOption.AllDirectories);
                        for (int i = 0; i < files.Length; i++)
                        {
                            totalBytes += new FileInfo(files[i]).Length;
                        }
                    }
                    catch { }

                    int sizeKB = (int)(totalBytes / 1024);
                    if (sizeKB > 0)
                    {
                        uKey.SetValue("EstimatedSize", sizeKB, RegistryValueKind.DWord);
                    }

                    uKey.SetValue("UninstallString", "\"" + uninstallPath + "\"");
                    uKey.SetValue("QuietUninstallString", "\"" + uninstallPath + "\"");
                    uKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
        }

        private static void StartApplication(string installDir, string exePath)
        {
            bool started = false;
            try
            {
                using (ManagementClass processClass = new ManagementClass("Win32_Process"))
                {
                    using (ManagementBaseObject inParams = processClass.GetMethodParameters("Create"))
                    {
                        inParams["CommandLine"] = "\"" + exePath + "\"";
                        inParams["CurrentDirectory"] = installDir;
                        using (ManagementBaseObject outParams = processClass.InvokeMethod("Create", inParams, null))
                        {
                            if (outParams != null && Convert.ToUInt32(outParams["returnValue"]) == 0)
                            {
                                started = true;
                            }
                        }
                    }
                }
            }
            catch { }

            if (!started)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exePath);
                    psi.WorkingDirectory = installDir;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { }
            }
        }
    }
}
