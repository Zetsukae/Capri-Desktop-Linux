using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapriSetup
{
    internal static class Program
    {
        internal static string InstallPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"); } }
        internal const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Capri.Desktop";

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--verify") return Verify(args[1]);
            if (args.Length == 1 && args[0] == "--remove-files") return RemoveFiles();
            using (SetupWindow window = new SetupWindow(args.Length == 1 && args[0] == "--uninstall")) Application.Run(window);
            return 0;
        }

        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        internal static byte[] Payload()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.App.exe"))
            using (MemoryStream memory = new MemoryStream())
            {
                if (source == null) throw new InvalidDataException("The Capri application payload is missing.");
                source.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static int Verify(string output)
        {
            try
            {
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(output);
                byte[] bytes = Payload();
                if (bytes.Length < 1024 || bytes[0] != 77 || bytes[1] != 90) throw new InvalidDataException("Application payload is not a Windows executable.");
                File.WriteAllBytes(Path.Combine(output, "Capri.exe"), bytes);
                using (SHA256 sha = SHA256.Create()) File.WriteAllText(Path.Combine(output, "payload.sha256"), BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
                using (SetupWindow preview = new SetupWindow(false))
                {
                    preview.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                    {
                        preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, "installer.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    preview.Close();
                }
                File.WriteAllText(Path.Combine(output, "packaging-test.txt"), "PASS embedded executable extracted\nPASS PE signature\nPASS SHA-256 computed\nPASS installer window rendered\nLIMIT installation and uninstallation were not performed\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        }

        internal static void Install(bool desktop)
        {
            string destination = InstallPath;
            string marker = Path.Combine(destination, ".capri-install");
            if (Directory.Exists(destination) && !File.Exists(marker) && File.Exists(Path.Combine(destination, "Capri.exe"))) throw new IOException("That folder already contains an unrecognized Capri.exe. Move it before installing.");
            foreach (Process process in Process.GetProcessesByName("Capri"))
            {
                using (process)
                {
                    try { if (string.Equals(process.MainModule.FileName, Path.Combine(destination, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Quit Capri from her tray menu, then install again."); }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            Directory.CreateDirectory(destination);
            File.WriteAllBytes(Path.Combine(destination, "Capri.exe"), Payload());
            File.Copy(Application.ExecutablePath, Path.Combine(destination, "Uninstall.exe"), true);
            File.WriteAllText(marker, "Capri desktop companion 0.2.0");
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Capri.lnk");
            Shortcut(menu, Path.Combine(destination, "Capri.exe"));
            if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Capri.lnk"), Path.Combine(destination, "Capri.exe"));
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", "Capri");
                key.SetValue("DisplayVersion", "0.2.0");
                key.SetValue("Publisher", "Capri fan project");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", Path.Combine(destination, "Capri.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (Payload().Length + new FileInfo(Application.ExecutablePath).Length) / 1024, RegistryValueKind.DWord);
            }
        }

        private static void Shortcut(string path, string target)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic shortcut = shell.CreateShortcut(path);
                try { shortcut.TargetPath = target; shortcut.WorkingDirectory = Path.GetDirectoryName(target); shortcut.Description = "Capri desktop companion"; shortcut.IconLocation = target + ",0"; shortcut.Save(); }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        internal static void BeginUninstall()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "Capri-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, temporary);
            Process.Start(new ProcessStartInfo(temporary, "--remove-files") { UseShellExecute = true });
        }

        private static int RemoveFiles()
        {
            try
            {
                string destination = Path.GetFullPath(InstallPath);
                string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"));
                if (!string.Equals(destination, expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(destination, ".capri-install"))) throw new IOException("The Capri install marker could not be verified.");
                foreach (Process process in Process.GetProcessesByName("Capri"))
                {
                    using (process)
                    {
                        try { if (string.Equals(process.MainModule.FileName, Path.Combine(destination, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Quit Capri from her tray menu before removing her."); }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
                string uninstaller = Path.Combine(destination, "Uninstall.exe");
                for (int i = 0; i < 25; i++)
                {
                    try { File.Delete(uninstaller); break; }
                    catch (IOException) { System.Threading.Thread.Sleep(100); }
                }
                if (File.Exists(uninstaller)) throw new IOException("The uninstaller is still open. Close it and try removing Capri again.");
                File.Delete(Path.Combine(destination, "Capri.exe"));
                File.Delete(Path.Combine(destination, ".capri-install"));
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Capri.lnk"), destination);
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Capri.lnk"), destination);
                Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
                MessageBox.Show("Capri has been removed.\n\nYour position and size settings were kept in " + destination + ".", "Capri", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Capri could not be removed", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }

        private static void DeleteShortcut(string path, string destination)
        {
            if (!File.Exists(path)) return;
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic shortcut = shell.CreateShortcut(path);
                try { if (string.Equals((string)shortcut.TargetPath, Path.Combine(destination, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) File.Delete(path); }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }

    internal sealed class SetupWindow : Form
    {
        private readonly bool removing;
        private readonly PictureBox chip;
        private readonly Button primary;
        private readonly CheckBox desktop;
        private readonly CheckBox launch;
        private readonly Label status;

        internal SetupWindow(bool uninstall)
        {
            removing = uninstall;
            Text = removing ? "Remove Capri" : "Install Capri";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 470);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(19, 20, 24);
            ForeColor = Color.FromArgb(240, 241, 243);
            Font = new Font("Segoe UI", 10);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            Label eyebrow = LabelAt("CAPRI  /  DESKTOP COMPANION", 34, 29, 590, 22, 9, Color.FromArgb(172, 248, 98));
            Label heading = LabelAt(removing ? "See you around." : "A little company.\nZero noise.", 31, 69, 408, 116, 29, ForeColor);
            heading.Font = new Font("Segoe UI Semibold", 29, FontStyle.Bold);
            chip = new PictureBox();
            chip.Bounds = new Rectangle(462, 80, 180, 112);
            chip.SizeMode = PictureBoxSizeMode.Zoom;
            using (Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.png")) if (image != null) using (Bitmap loaded = new Bitmap(image)) chip.Image = new Bitmap(loaded);
            Controls.Add(chip);
            LabelAt(removing ? "Remove the app and its shortcuts for this Windows user.\nQuit Capri from the tray before continuing." : "Right-click for her menu.", 34, 208, 610, 53, 11, Color.FromArgb(175, 179, 188));
            desktop = new CheckBox { Text = "Add a desktop shortcut", Checked = false, AutoSize = true, Location = new Point(34, 280), Visible = !removing, ForeColor = ForeColor, BackColor = BackColor };
            launch = new CheckBox { Text = "Open Capri when finished", Checked = true, AutoSize = true, Location = new Point(34, 310), Visible = !removing, ForeColor = ForeColor, BackColor = BackColor };
            Controls.Add(desktop);
            Controls.Add(launch);
            status = LabelAt(removing ? "Your saved position and size will be kept." : "", 34, 350, 604, 22, 9, Color.FromArgb(146, 151, 161));
            primary = new Button { Text = removing ? "Remove Capri" : "Install Capri", Bounds = new Rectangle(450, 399, 194, 43), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(239, 58, 78), ForeColor = Color.White, Cursor = Cursors.Hand };
            primary.FlatAppearance.BorderSize = 0;
            primary.Click += Perform;
            Controls.Add(primary);
            Button cancel = new Button { Text = "Cancel", Bounds = new Rectangle(318, 399, 116, 43), FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(196, 199, 205), BackColor = BackColor };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(62, 65, 74);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            AcceptButton = primary;
            CancelButton = cancel;
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float size, Color color)
        {
            Label label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font("Segoe UI", size), ForeColor = color, BackColor = Color.Transparent };
            Controls.Add(label);
            return label;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen line = new Pen(Color.FromArgb(56, 58, 66))) e.Graphics.DrawLine(line, 34, 382, Width - 52, 382);
            using (Pen line = new Pen(Color.FromArgb(239, 58, 78), 3)) e.Graphics.DrawLine(line, 0, 0, ClientSize.Width, 0);
        }

        private void Perform(object sender, EventArgs e)
        {
            primary.Enabled = false;
            try
            {
                if (removing) { Program.BeginUninstall(); Close(); return; }
                status.Text = "Making a little room for Capri…";
                Program.Install(desktop.Checked);
                if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(Program.InstallPath, "Capri.exe")) { UseShellExecute = true });
                Close();
            }
            catch (Exception error) { status.Text = "Capri could not finish. Your files are still here."; primary.Enabled = true; MessageBox.Show(error.Message, "Capri", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        protected override void Dispose(bool disposing) { if (disposing && chip.Image != null) chip.Image.Dispose(); base.Dispose(disposing); }
    }
}
