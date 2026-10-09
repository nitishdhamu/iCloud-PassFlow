using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace iCloudPassFlow
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private const int ATTACH_PARENT_PROCESS = -1;

        static void Main(string[] args)
        {
            bool createdNew = false;
            using (Mutex mutex = new Mutex(true, @"Local\iCloudPassFlow_Uninstall_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                string currentExe = Process.GetCurrentProcess().MainModule.FileName;

                // Attach to console if started from terminal so output is visible
                bool consoleAttached = AttachConsole(ATTACH_PARENT_PROCESS);
                if (consoleAttached)
                {
                    try
                    {
                        StreamWriter stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        StreamWriter stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }
                }

                Log(consoleAttached, "Uninstalling iCloud PassFlow...");

                // 1. Stop running process
                Log(consoleAttached, "Stopping iCloud PassFlow process...");
                string[] procNames = new string[] { "iCloudPassFlow", "iCloud-PassFlow" };
                for (int i = 0; i < procNames.Length; i++)
                {
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(procNames[i]);
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

                Thread.Sleep(300);

                // 2. Remove Scheduled Task & Startup Registry Keys
                Log(consoleAttached, "Removing startup entries...");
                try
                {
                    Type schedType = Type.GetTypeFromProgID("Schedule.Service");
                    if (schedType != null)
                    {
                        dynamic service = Activator.CreateInstance(schedType);
                        service.Connect();
                        dynamic root = service.GetFolder("\\");
                        try { root.DeleteTask("iCloud-PassFlow", 0); } catch { }
                        try { root.DeleteTask("iCloudPassFlow", 0); } catch { }
                    }
                }
                catch { }

                string[] runSubKeys = new string[]
                {
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
                };

                foreach (string subKey in runSubKeys)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
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

                // 3. Remove StartupApproved Registry Entries
                string[] saSubKeys = new string[]
                {
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
                };

                foreach (string subKey in saSubKeys)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
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

                // 4. Remove Startup & Start Menu Shortcuts
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] shortcutFolders = new string[]
                {
                    Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Startup"),
                    Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs")
                };

                foreach (string folder in shortcutFolders)
                {
                    if (Directory.Exists(folder))
                    {
                        try
                        {
                            foreach (string file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                            {
                                string fileName = Path.GetFileName(file);
                                if (fileName.IndexOf("iCloud-PassFlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    fileName.IndexOf("iCloudPassFlow", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    try { File.Delete(file); } catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 5. Remove Tray Notification Cache
                try
                {
                    using (RegistryKey notifyKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                    {
                        if (notifyKey != null)
                        {
                            foreach (string subKeyName in notifyKey.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey sub = notifyKey.OpenSubKey(subKeyName))
                                    {
                                        if (sub != null)
                                        {
                                            string execPath = sub.GetValue("ExecutablePath") as string;
                                            if (!string.IsNullOrEmpty(execPath) &&
                                                (execPath.IndexOf("iCloud-PassFlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 execPath.IndexOf("iCloudPassFlow", StringComparison.OrdinalIgnoreCase) >= 0))
                                            {
                                                notifyKey.DeleteSubKeyTree(subKeyName, false);
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }

                // 6. Remove Windows Settings & Control Panel Uninstall Registration
                Log(consoleAttached, "Removing Windows Settings registration...");
                using (RegistryKey parent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    if (parent != null)
                    {
                        try { parent.DeleteSubKeyTree("iCloud-PassFlow", false); } catch { }
                        try { parent.DeleteSubKeyTree("iCloudPassFlow", false); } catch { }
                    }
                }

                // 7. Remove Installation Directory contents (except running uninstaller)
                Log(consoleAttached, "Removing program files...");
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\iCloud-PassFlow");
                if (Directory.Exists(installDir))
                {
                    for (int retry = 0; retry < 5; retry++)
                    {
                        try
                        {
                            foreach (string subDir in Directory.GetDirectories(installDir))
                            {
                                try { Directory.Delete(subDir, true); } catch { }
                            }
                            foreach (string file in Directory.GetFiles(installDir))
                            {
                                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
                                {
                                    try { File.Delete(file); } catch { }
                                }
                            }
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(200);
                        }
                    }
                }

                Log(consoleAttached, "======================================================");
                Log(consoleAttached, "   UNINSTALLATION COMPLETE!                           ");
                Log(consoleAttached, "======================================================");
                Log(consoleAttached, "iCloud PassFlow has been completely removed from your system.");

                if (consoleAttached)
                {
                    try { Console.Out.Flush(); } catch { }
                    try { FreeConsole(); } catch { }
                }

                // 8. Remove remaining uninstaller binary & folder immediately after exit
                if (Directory.Exists(installDir))
                {
                    try
                    {
                        string cmdArgs = "/c timeout /t 1 /nobreak >nul & rmdir /s /q \"" + installDir + "\"";
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmdArgs)
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);
                    }
                    catch { }
                }
            }
        }

        private static void Log(bool attached, string message)
        {
            if (attached)
            {
                try { Console.WriteLine(message); } catch { }
            }
        }
    }
}
