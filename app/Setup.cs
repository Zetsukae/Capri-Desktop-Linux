using System;
using System.Collections.Generic;
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
        internal const string Version = "2.1";
        internal static string InstallPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"); } }
        internal const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Capri.Desktop";
        internal const string RunPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--verify") return Verify(args[1]);
            if (args.Length >= 1 && args[0] == "--remove-files") return RemoveFiles(Array.IndexOf(args, "--data") >= 0, Array.IndexOf(args, "--speech") >= 0);
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
                foreach (bool removing in new bool[] { false, true })
                    using (SetupWindow preview = new SetupWindow(removing))
                    {
                        preview.Show();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                        {
                            preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(output, removing ? "uninstaller.png" : "installer.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        preview.Close();
                    }
                File.WriteAllText(Path.Combine(output, "packaging-test.txt"), "PASS embedded executable extracted\nPASS PE signature\nPASS SHA-256 computed\nPASS installer and uninstaller windows rendered\nLIMIT installation and uninstallation were not performed\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        }

        internal static void Install(bool desktop, bool startup, Action<string, int> progress)
        {
            string destination = InstallPath;
            string marker = Path.Combine(destination, ".capri-install");
            progress("checking the folder", 10);
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
            progress("copying her chip", 35);
            File.WriteAllBytes(Path.Combine(destination, "Capri.exe"), Payload());
            File.Copy(Application.ExecutablePath, Path.Combine(destination, "Uninstall.exe"), true);
            File.WriteAllText(marker, "Capri desktop companion " + Version);
            progress("making shortcuts", 60);
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Capri.lnk");
            Shortcut(menu, Path.Combine(destination, "Capri.exe"));
            if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Capri.lnk"), Path.Combine(destination, "Capri.exe"));
            progress("telling Windows about her", 85);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", "Capri");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Capri fan project");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", Path.Combine(destination, "Capri.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (Payload().Length + new FileInfo(Application.ExecutablePath).Length) / 1024, RegistryValueKind.DWord);
            }
            // Starting with Windows is only ever done when asked for, and only for this user.
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunPath))
            {
                if (startup) run.SetValue("Capri", "\"" + Path.Combine(destination, "Capri.exe") + "\"");
                else if (run.GetValue("Capri") != null) run.DeleteValue("Capri", false);
            }
            progress("SYNC COMPLETE", 100);
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

        // Her own files, kept by default. Settings, her notes about you, a saved cloud key and a replaced persona are one choice; the large speech and voice files are another.
        internal static readonly string[] DataFiles = { "settings.json", "memory.txt", "cloud.key", "persona.txt" };
        internal static readonly string[] SpeechFolders = { "speech", "voices" };

        internal static long SpeechSize()
        {
            long total = 0;
            foreach (string name in SpeechFolders)
            {
                string folder = Path.Combine(InstallPath, name);
                try { if (Directory.Exists(folder)) foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) total += new FileInfo(file).Length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return total;
        }

        internal static void BeginUninstall(bool data, bool speech)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "Capri-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, temporary);
            Process.Start(new ProcessStartInfo(temporary, "--remove-files" + (data ? " --data" : "") + (speech ? " --speech" : "")) { UseShellExecute = true });
        }

        private static int RemoveFiles(bool data, bool speech)
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
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunPath, true)) if (run != null && run.GetValue("Capri") != null) run.DeleteValue("Capri", false);
                if (data) foreach (string name in DataFiles) { try { File.Delete(Path.Combine(destination, name)); } catch (IOException) { } }
                if (speech) foreach (string name in SpeechFolders) { try { if (Directory.Exists(Path.Combine(destination, name))) Directory.Delete(Path.Combine(destination, name), true); } catch (IOException) { } }
                try { if (Directory.GetFileSystemEntries(destination).Length == 0) Directory.Delete(destination); } catch (IOException) { }
                string kept = Directory.Exists(destination) ? "\n\nWhat you chose to keep (settings, notes, speech files) is still in " + destination + "." : "";
                MessageBox.Show("Capri has been removed." + kept, "Capri", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // The install shown as her syncing: a bar that fills while the steps happen.
    internal sealed class SyncBar : Control
    {
        private int value;
        private string caption = "";
        internal SyncBar() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); BackColor = Color.FromArgb(8, 9, 13); }
        internal void Set(string text, int percent) { caption = text; value = Math.Max(0, Math.Min(100, percent)); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Font mono = new Font("Consolas", 9.5f, FontStyle.Bold))
            using (Brush green = new SolidBrush(SetupWindow.Green))
            using (Brush dim = new SolidBrush(SetupWindow.Dim))
            using (Brush track = new SolidBrush(Color.FromArgb(60, 20, 26)))
            using (Brush fill = new SolidBrush(SetupWindow.Crimson))
            using (Pen edge = new Pen(SetupWindow.Line))
            {
                g.DrawString("ENGRAM SYNC", mono, green, 0, 0);
                g.DrawString(value + "%", mono, green, Width - 40, 0);
                g.FillRectangle(track, 0, 22, Width, 12);
                g.FillRectangle(fill, 0, 22, (int)(Width * value / 100.0), 12);
                g.DrawRectangle(edge, 0, 22, Width - 1, 12);
                g.DrawString(caption.ToUpperInvariant(), mono, dim, 0, 40);
            }
        }
    }

    internal sealed class SetupWindow : Form
    {
        internal static readonly Color Void = Color.FromArgb(8, 9, 13);
        internal static readonly Color Panel = Color.FromArgb(14, 16, 22);
        internal static readonly Color Crimson = Color.FromArgb(239, 58, 78);
        internal static readonly Color Green = Color.FromArgb(167, 245, 88);
        internal static readonly Color Ink = Color.FromArgb(232, 234, 238);
        internal static readonly Color Dim = Color.FromArgb(128, 134, 148);
        internal static readonly Color Line = Color.FromArgb(52, 56, 66);
        private const int Cut = 16;

        private readonly bool removing;
        private readonly PictureBox chip;
        private readonly Button primary;
        private readonly CheckBox desktop;
        private readonly CheckBox startup;
        private readonly CheckBox launch;
        private readonly CheckBox wipeData;
        private readonly CheckBox wipeSpeech;
        private readonly Label status;
        private readonly SyncBar bar = new SyncBar();

        internal SetupWindow(bool uninstall)
        {
            removing = uninstall;
            Text = removing ? "Remove Capri" : "Install Capri";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 500);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Void;
            ForeColor = Ink;
            Font = new Font("Bahnschrift", 10);
            DoubleBuffered = true;
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);

            Label heading = LabelAt(removing ? "Bye-bye." : "Hi! It's me.\nI live here now.", 30, 70, 420, 112, 29, Ink);
            heading.Font = new Font("Bahnschrift SemiBold", 29, FontStyle.Bold);
            chip = new PictureBox();
            chip.Bounds = new Rectangle(470, 80, 180, 112);
            chip.SizeMode = PictureBoxSizeMode.Zoom;
            chip.BackColor = Color.Transparent;
            using (Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.png")) if (image != null) using (Bitmap loaded = new Bitmap(image)) chip.Image = new Bitmap(loaded);
            Controls.Add(chip);
            LabelAt(removing ? "Remove the app and its shortcuts for this Windows user. Quit Capri from the tray first." : "She installs for you only. No admin needed. Right-click her for her menu.", 32, 196, 610, 42, 10.5f, Dim);

            desktop = Box("Add a desktop shortcut", 32, 252, false);
            startup = Box("Start Capri when I sign in to Windows", 32, 280, false);
            launch = Box("Open Capri when finished", 32, 308, true);
            desktop.Visible = startup.Visible = launch.Visible = !removing;
            long speech = removing ? Program.SpeechSize() : 0;
            wipeData = Box("Also delete my settings, her notes about me and any saved key", 32, 252, false);
            wipeSpeech = Box("Also delete the speech and voice files (" + (speech / 1048576) + " MB)", 32, 284, false);
            wipeData.Visible = removing;
            wipeSpeech.Visible = removing && speech > 0;

            bar.Bounds = new Rectangle(32, 350, 616, 56);
            bar.Set(removing ? "Nothing is removed until you click" : "Ready", 0);
            Controls.Add(bar);
            status = LabelAt("", 32, 410, 616, 20, 9, Dim);
            primary = new Button { Text = removing ? "REMOVE CAPRI" : "INSTALL CAPRI", Bounds = new Rectangle(446, 440, 202, 42), FlatStyle = FlatStyle.Flat, BackColor = Crimson, ForeColor = Color.White, Cursor = Cursors.Hand, Font = new Font("Bahnschrift SemiBold", 10.5f, FontStyle.Bold) };
            primary.FlatAppearance.BorderSize = 0;
            primary.Click += Perform;
            Controls.Add(primary);
            Button cancel = new Button { Text = "CANCEL", Bounds = new Rectangle(326, 440, 104, 42), FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Void, Cursor = Cursors.Hand };
            cancel.FlatAppearance.BorderColor = Line;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            LabelAt("Unofficial fan project. Not affiliated with CD PROJEKT RED or Studio Trigger.", 32, 456, 280, 30, 8, Dim);
            Label close = new Label { Text = "X", Bounds = new Rectangle(630, 10, 32, 24), ForeColor = Crimson, BackColor = Color.Transparent, Font = new Font("Consolas", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            AcceptButton = primary;
            CancelButton = cancel;
            Shape();
        }

        private CheckBox Box(string text, int x, int y, bool on)
        {
            CheckBox box = new CheckBox { Text = text, Checked = on, AutoSize = true, Location = new Point(x, y), ForeColor = Ink, BackColor = Void };
            Controls.Add(box);
            return box;
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float size, Color color)
        {
            Label label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font("Bahnschrift", size), ForeColor = color, BackColor = Color.Transparent };
            Controls.Add(label);
            return label;
        }

        private GraphicsPath Outline(float inset)
        {
            float w = ClientSize.Width - inset, h = ClientSize.Height - inset;
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(new PointF[] { new PointF(Cut + inset, inset), new PointF(w, inset), new PointF(w, h - Cut), new PointF(w - Cut, h), new PointF(inset, h), new PointF(inset, Cut + inset) });
            return path;
        }

        private void Shape()
        {
            using (GraphicsPath path = Outline(0)) Region = new Region(path);
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen scan = new Pen(Color.FromArgb(10, 255, 255, 255))) for (int y = 44; y < Height; y += 3) e.Graphics.DrawLine(scan, 16, y, Width - 16, y);
            using (Font mono = new Font("Consolas", 10, FontStyle.Bold))
            using (Brush red = new SolidBrush(Crimson))
            using (Brush green = new SolidBrush(Green)) { e.Graphics.DrawString("//", mono, red, 24, 16); e.Graphics.DrawString("CAPRI  " + (removing ? "REMOVE" : "INSTALL"), mono, green, 48, 16); }
            using (Pen line = new Pen(Line)) e.Graphics.DrawLine(line, 24, 44, Width - 24, 44);
            using (GraphicsPath path = Outline(0.5f))
            using (LinearGradientBrush gradient = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Crimson, Green, LinearGradientMode.ForwardDiagonal))
            using (Pen outline = new Pen(gradient, 2)) e.Graphics.DrawPath(outline, path);
        }

        // The top strip stands in for the title bar and can be dragged.
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84)
            {
                Point at = PointToClient(new Point((short)(message.LParam.ToInt32() & 0xFFFF), (short)(message.LParam.ToInt32() >> 16)));
                if (at.Y < 44 && at.X < Width - 60) { message.Result = (IntPtr)2; return; }
            }
            base.WndProc(ref message);
        }

        private void Step(string text, int percent)
        {
            bar.Set(text, percent);
            status.Text = "";
            Application.DoEvents();
            System.Threading.Thread.Sleep(110);
        }

        private void Perform(object sender, EventArgs e)
        {
            primary.Enabled = false;
            try
            {
                if (removing) { Program.BeginUninstall(wipeData.Checked, wipeSpeech.Checked); Close(); return; }
                Program.Install(desktop.Checked, startup.Checked, Step);
                if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(Program.InstallPath, "Capri.exe")) { UseShellExecute = true });
                System.Threading.Thread.Sleep(350);
                Close();
            }
            catch (Exception error) { bar.Set("Stopped", 0); status.Text = "Capri could not finish. Your files are still here."; primary.Enabled = true; MessageBox.Show(error.Message, "Capri", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        protected override void Dispose(bool disposing) { if (disposing && chip.Image != null) chip.Image.Dispose(); base.Dispose(disposing); }
    }
}
