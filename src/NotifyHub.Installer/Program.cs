using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NotifyHub.Installer
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isSilent = false;
            foreach (string arg in args)
            {
                if (arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    isSilent = true;
                }
            }

            if (isSilent)
            {
                RunSilentInstall();
                return;
            }

            Application.Run(new InstallerForm());
        }

        static void RunSilentInstall()
        {
            try
            {
                string targetDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "NotifyHub");

                InstallLogic.PerformInstall(targetDir, true, true, null);
            }
            catch (Exception ex)
            {
                InstallLogic.Log("Silent install exception: " + ex.ToString());
            }
        }
    }

    public class InstallerForm : Form
    {
        private TextBox txtPath;
        private Button btnBrowse;
        private CheckBox chkDesktop;
        private CheckBox chkStartMenu;
        private CheckBox chkLaunch;
        private ProgressBar prgBar;
        private Label lblStatus;
        private Button btnInstall;
        private Button btnCancel;
        private bool isFinished = false;

        public InstallerForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Notify Hub — Setup";
            this.Size = new Size(540, 390);
            try { this.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(245, 247, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // Header Panel
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 72;
            pnlHeader.BackColor = Color.FromArgb(30, 32, 48);

            Label lblTitle = new Label();
            lblTitle.Text = "NOTIFY HUB SETUP";
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblTitle.Location = new Point(20, 12);
            lblTitle.AutoSize = true;

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Native Windows Notification Manager Installation";
            lblSubtitle.ForeColor = Color.FromArgb(170, 180, 205);
            lblSubtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            lblSubtitle.Location = new Point(22, 42);
            lblSubtitle.AutoSize = true;

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);
            this.Controls.Add(pnlHeader);

            // Install Path Group
            Label lblPathHeading = new Label();
            lblPathHeading.Text = "Destination Folder:";
            lblPathHeading.Location = new Point(22, 90);
            lblPathHeading.AutoSize = true;
            lblPathHeading.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.Controls.Add(lblPathHeading);

            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "NotifyHub");

            txtPath = new TextBox();
            txtPath.Text = defaultPath;
            txtPath.Location = new Point(25, 114);
            txtPath.Size = new Size(385, 24);
            this.Controls.Add(txtPath);

            btnBrowse = new Button();
            btnBrowse.Text = "Browse...";
            btnBrowse.Location = new Point(420, 112);
            btnBrowse.Size = new Size(85, 28);
            btnBrowse.BackColor = Color.White;
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            // Options
            chkDesktop = new CheckBox();
            chkDesktop.Text = "Create Desktop shortcut";
            chkDesktop.Checked = true;
            chkDesktop.Location = new Point(25, 155);
            chkDesktop.AutoSize = true;
            this.Controls.Add(chkDesktop);

            chkStartMenu = new CheckBox();
            chkStartMenu.Text = "Create Start Menu shortcut";
            chkStartMenu.Checked = true;
            chkStartMenu.Location = new Point(25, 182);
            chkStartMenu.AutoSize = true;
            this.Controls.Add(chkStartMenu);

            chkLaunch = new CheckBox();
            chkLaunch.Text = "Launch Notify Hub after installation completes";
            chkLaunch.Checked = true;
            chkLaunch.Location = new Point(25, 209);
            chkLaunch.AutoSize = true;
            this.Controls.Add(chkLaunch);

            // Progress Bar
            prgBar = new ProgressBar();
            prgBar.Location = new Point(25, 245);
            prgBar.Size = new Size(480, 20);
            prgBar.Style = ProgressBarStyle.Continuous;
            prgBar.Value = 0;
            this.Controls.Add(prgBar);

            // Status Label
            lblStatus = new Label();
            lblStatus.Text = "Ready to install. Click 'Install' to begin.";
            lblStatus.Location = new Point(25, 272);
            lblStatus.Size = new Size(480, 20);
            lblStatus.ForeColor = Color.FromArgb(70, 80, 95);
            this.Controls.Add(lblStatus);

            // Bottom Buttons
            btnInstall = new Button();
            btnInstall.Text = "Install";
            btnInstall.Location = new Point(310, 305);
            btnInstall.Size = new Size(100, 32);
            btnInstall.BackColor = Color.FromArgb(37, 99, 235);
            btnInstall.ForeColor = Color.White;
            btnInstall.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += BtnInstall_Click;
            this.Controls.Add(btnInstall);

            btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(420, 305);
            btnCancel.Size = new Size(85, 32);
            btnCancel.BackColor = Color.White;
            btnCancel.Click += new EventHandler(BtnCancel_Click);
            this.Controls.Add(btnCancel);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Notify Hub installation folder:";
                fbd.SelectedPath = txtPath.Text;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            if (isFinished)
            {
                if (chkLaunch.Checked)
                {
                    string exePath = Path.Combine(txtPath.Text, "NotifyHub.exe");
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    }
                }
                this.Close();
                return;
            }

            string targetDir = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(targetDir))
            {
                MessageBox.Show("Please select a valid installation folder.", "Notify Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Disable UI during install
            txtPath.Enabled = false;
            btnBrowse.Enabled = false;
            chkDesktop.Enabled = false;
            chkStartMenu.Enabled = false;
            chkLaunch.Enabled = false;
            btnInstall.Enabled = false;
            btnCancel.Enabled = false;

            prgBar.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = "Extracting and installing files...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    InstallLogic.PerformInstall(targetDir, chkDesktop.Checked, chkStartMenu.Checked, delegate(string msg)
                    {
                        this.Invoke(new Action(delegate() { lblStatus.Text = msg; }));
                    });

                    this.Invoke(new Action(delegate()
                    {
                        prgBar.Style = ProgressBarStyle.Continuous;
                        prgBar.Value = 100;
                        lblStatus.Text = "Installation completed successfully!";
                        lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                        btnInstall.Text = "Finish";
                        btnInstall.Enabled = true;
                        btnCancel.Visible = false;
                        isFinished = true;
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(delegate()
                    {
                        prgBar.Style = ProgressBarStyle.Continuous;
                        prgBar.Value = 0;
                        lblStatus.Text = "Installation failed: " + ex.Message;
                        lblStatus.ForeColor = Color.Red;
                        btnInstall.Enabled = true;
                        btnCancel.Enabled = true;
                        MessageBox.Show("Installation error: " + ex.Message, "Install Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }
    }

    public static class InstallLogic
    {
        public static void Log(string msg)
        {
            try
            {
                string logFile = Path.Combine(Path.GetTempPath(), "NotifyHubInstaller.log");
                File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        public static void PerformInstall(string targetDir, bool createDesktop, bool createStartMenu, Action<string> reportProgress)
        {
            Log("Starting installation to: " + targetDir);

            // Close any existing running NotifyHub process
            try
            {
                Process[] procs = Process.GetProcessesByName("NotifyHub");
                foreach (Process p in procs)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Log("Process termination warning: " + ex.Message);
            }

            if (reportProgress != null) reportProgress("Preparing installation directory...");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Extract embedded zip payload
            if (reportProgress != null) reportProgress("Unpacking application files...");
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream("Payload.zip"))
            {
                if (stream == null)
                {
                    throw new Exception("Embedded payload resource 'Payload.zip' not found in installer.");
                }

                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destPath = Path.Combine(targetDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                            continue;
                        }

                        string parentDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }

                        // Overwrite file cleanly
                        if (File.Exists(destPath))
                        {
                            try { File.Delete(destPath); } catch { }
                        }

                        entry.ExtractToFile(destPath, true);
                    }
                }
            }

            Log("Files unpacked successfully.");

            if (reportProgress != null) reportProgress("Creating shortcuts...");
            string exePath = Path.Combine(targetDir, "NotifyHub.exe");
            string desktopDir = createDesktop ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : null;
            string programsDir = createStartMenu ? Environment.GetFolderPath(Environment.SpecialFolder.Programs) : null;

            CreateShortcutsWithVbs(desktopDir, programsDir, exePath, targetDir);

            // Create uninstaller
            try
            {
                if (reportProgress != null) reportProgress("Generating uninstaller...");
                string uninstallerBat = Path.Combine(targetDir, "Uninstall.bat");
                string actualDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string actualPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

                string uninstallContent = "@echo off\r\n" +
                    "echo Uninstalling Notify Hub...\r\n" +
                    "taskkill /f /im NotifyHub.exe 2>nul\r\n" +
                    "del \"" + Path.Combine(actualDesktop, "Notify Hub.lnk") + "\" 2>nul\r\n" +
                    "del \"" + Path.Combine(actualPrograms, "Notify Hub.lnk") + "\" 2>nul\r\n" +
                    "echo Removing files...\r\n" +
                    "rmdir /s /q \"" + targetDir + "\" 2>nul\r\n" +
                    "echo Notify Hub uninstalled successfully.\r\n" +
                    "pause\r\n";
                File.WriteAllText(uninstallerBat, uninstallContent);
                Log("Created uninstaller at: " + uninstallerBat);
            }
            catch (Exception ex)
            {
                Log("Uninstaller creation error: " + ex.Message);
            }

            if (reportProgress != null) reportProgress("Ready!");
            Log("Installation completed successfully.");
        }

        private static void CreateShortcutsWithVbs(string desktopDir, string programsDir, string exePath, string workingDir)
        {
            try
            {
                string vbsPath = Path.Combine(Path.GetTempPath(), "nh_shortcuts_" + Guid.NewGuid().ToString("N") + ".vbs");
                using (StreamWriter sw = new StreamWriter(vbsPath, false, System.Text.Encoding.ASCII))
                {
                    sw.WriteLine("Set ws = CreateObject(\"WScript.Shell\")");
                    if (!string.IsNullOrEmpty(desktopDir))
                    {
                        string lnk = Path.Combine(desktopDir, "Notify Hub.lnk");
                        sw.WriteLine(string.Format("Set s1 = ws.CreateShortcut(\"{0}\")", lnk.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s1.TargetPath = \"{0}\"", exePath.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s1.WorkingDirectory = \"{0}\"", workingDir.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s1.IconLocation = \"{0},0\"", exePath.Replace("\"", "\"\"")));
                        sw.WriteLine("s1.Description = \"Notify Hub — Native Windows Notification Manager\"");
                        sw.WriteLine("s1.Save");
                    }
                    if (!string.IsNullOrEmpty(programsDir))
                    {
                        string lnk = Path.Combine(programsDir, "Notify Hub.lnk");
                        sw.WriteLine(string.Format("Set s2 = ws.CreateShortcut(\"{0}\")", lnk.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s2.TargetPath = \"{0}\"", exePath.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s2.WorkingDirectory = \"{0}\"", workingDir.Replace("\"", "\"\"")));
                        sw.WriteLine(string.Format("s2.IconLocation = \"{0},0\"", exePath.Replace("\"", "\"\"")));
                        sw.WriteLine("s2.Description = \"Notify Hub — Native Windows Notification Manager\"");
                        sw.WriteLine("s2.Save");
                    }
                }

                ProcessStartInfo psi = new ProcessStartInfo("wscript.exe", "\"" + vbsPath + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                if (p != null) p.WaitForExit(5000);

                try { File.Delete(vbsPath); } catch { }
                Log("VBS shortcuts created successfully.");
            }
            catch (Exception ex)
            {
                Log("VBS Shortcut creation error: " + ex.Message);
            }
        }
    }
}
