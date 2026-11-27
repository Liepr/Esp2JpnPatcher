// Interfaz clásica (WinForms) del parcheador Esp2Jpn para VOCALOID7.
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Esp2Jpn
{
    class MainForm : Form
    {
        TextBox txtDll, txtTest, txtOut, log;
        Label lblStatus;
        Button btnBrowse, btnPatch, btnRestore, btnToggle;
        CheckBox chkRemove, chkNotes;
        RadioButton rbZ, rbS, rbJ, rbZsdz, rbZsts, rbZsz, rbAm, rbSo, rbSh, rbFl;
        TextBox txtNotesPrev;
        HelpForm help;      // ventana "Atajos y fonemas" (si está abierta)
        bool loading;       // true mientras se cargan los controles desde Esp2Jpn.ini (no guardar ni registrar)

        public MainForm()
        {
            Text = "Esp2Jpn para VOCALOID7";
            // Menú Ayuda (nativo): Atajos y fonemas (F1), como en Esp2XSAMPA v2.4
            Menu = new MainMenu(new[]
            {
                new MenuItem("Ayuda", new[]
                {
                    new MenuItem("Atajos y fonemas", (s, e) => ShowHelp()) { Shortcut = Shortcut.F1 },
                    new MenuItem("-"),
                    new MenuItem("Creado por Liper") { Enabled = false }      // solo créditos
                })
            });
            ClientSize = new Size(620, 682);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }   // ícono incrustado en el .exe (Esp2Jpn.ico)

            var g1 = new GroupBox { Text = "VOCALOID7.dll", Left = 10, Top = 8, Width = 600, Height = 78 };
            txtDll = new TextBox { Left = 12, Top = 22, Width = 490 };
            btnBrowse = new Button { Text = "Examinar...", Left = 510, Top = 20, Width = 80 };
            lblStatus = new Label { Left = 12, Top = 50, Width = 570, Text = "Estado: (elige el archivo)" };
            g1.Controls.AddRange(new Control[] { txtDll, btnBrowse, lblStatus });

            var g2 = new GroupBox { Text = "Acciones", Left = 10, Top = 92, Width = 600, Height = 82 };
            btnPatch = new Button { Text = "Aplicar / re-aplicar", Left = 12, Top = 22, Width = 120, Height = 26 };
            btnRestore = new Button { Text = "Restaurar original", Left = 140, Top = 22, Width = 120, Height = 26 };
            btnToggle = new Button { Text = "Activar / Desactivar", Left = 268, Top = 22, Width = 130, Height = 26 };
            chkRemove = new CheckBox { Text = "Al restaurar, borrar también Esp2JpnHook.dll y .ini", Left = 12, Top = 54, Width = 400, Checked = true };
            g2.Controls.AddRange(new Control[] { btnPatch, btnRestore, btnToggle, chkRemove });

            // Sonido de ll / y (igual que en Esp2XSAMPA v2.4)
            var g5 = new GroupBox { Text = "Sonido de ll / y", Left = 10, Top = 180, Width = 600, Height = 48 };
            rbZ = new RadioButton { Text = "Z  (zheísmo)", Left = 12, Top = 18, Width = 140, Checked = true };
            rbS = new RadioButton { Text = "S  (sheísmo)", Left = 160, Top = 18, Width = 140 };
            rbJ = new RadioButton { Text = "j  (yeísmo)", Left = 308, Top = 18, Width = 140 };
            g5.Controls.AddRange(new Control[] { rbZ, rbS, rbJ });

            // Sonido de z / c (minúscula / MAYÚSCULA), igual que en Esp2XSAMPA v3.0.1
            var g7 = new GroupBox { Text = "Sonido de z / c  (minúscula / MAYÚSCULA)", Left = 10, Top = 234, Width = 600, Height = 48 };
            rbZsdz = new RadioButton { Text = "z \u2192 s,  Z / C \u2192 dz", Left = 12, Top = 18, Width = 140, Checked = true };
            rbZsts = new RadioButton { Text = "z \u2192 s,  Z / C \u2192 ts", Left = 160, Top = 18, Width = 140 };
            rbZsz = new RadioButton { Text = "z \u2192 s,  Z / C \u2192 z", Left = 308, Top = 18, Width = 140 };
            g7.Controls.AddRange(new Control[] { rbZsdz, rbZsts, rbZsz });

            // Nombres de las notas (vocaloid7_notes.py)
            var g6 = new GroupBox { Text = "Nombres de las notas del piano roll", Left = 10, Top = 288, Width = 600, Height = 126 };
            chkNotes = new CheckBox { Text = "Cambiar los nombres de las notas al aplicar el parche", Left = 12, Top = 18, Width = 570 };
            var panNot = new Panel { Left = 12, Top = 42, Width = 580, Height = 24 };
            rbAm = new RadioButton { Text = "Americana (C D E F G A B)", Left = 0, Top = 2, Width = 250, Checked = true };
            rbSo = new RadioButton { Text = "Solfeo (Do Re Mi Fa Sol La Si)", Left = 260, Top = 2, Width = 300 };
            panNot.Controls.AddRange(new Control[] { rbAm, rbSo });
            var panAcc = new Panel { Left = 12, Top = 66, Width = 580, Height = 24 };
            rbSh = new RadioButton { Text = "Sostenidos (\u266f)", Left = 0, Top = 2, Width = 250, Checked = true };
            rbFl = new RadioButton { Text = "Bemoles (\u266d)", Left = 260, Top = 2, Width = 300 };
            panAcc.Controls.AddRange(new Control[] { rbSh, rbFl });
            var lblPrev = new Label { Text = "Vista previa (Do a Si):", Left = 12, Top = 99, Width = 125 };
            txtNotesPrev = new TextBox { Left = 140, Top = 96, Width = 442, ReadOnly = true };
            g6.Controls.AddRange(new Control[] { chkNotes, panNot, panAcc, lblPrev, txtNotesPrev });

            var g3 = new GroupBox { Text = "Probar conversión (español -> fonemas japoneses)", Left = 10, Top = 420, Width = 600, Height = 74 };
            txtTest = new TextBox { Left = 12, Top = 22, Width = 260, Text = "hola cantaré" };
            var arrow = new Label { Left = 278, Top = 25, Width = 20, Text = "->" };
            txtOut = new TextBox { Left = 300, Top = 22, Width = 282, ReadOnly = true };
            var hint = new Label { Left = 12, Top = 48, Width = 570, Text = "Solo afecta a notas cuyo idioma es japonés; los bancos de otros idiomas no se tocan." };
            g3.Controls.AddRange(new Control[] { txtTest, arrow, txtOut, hint });

            var g4 = new GroupBox { Text = "Registro", Left = 10, Top = 500, Width = 600, Height = 172 };
            log = new TextBox { Left = 10, Top = 20, Width = 580, Height = 142, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            g4.Controls.Add(log);

            Controls.AddRange(new Control[] { g1, g2, g5, g7, g6, g3, g4 });

            btnBrowse.Click += (s, e) => Browse();
            btnPatch.Click += (s, e) => DoPatch();
            btnRestore.Click += (s, e) => DoRestore();
            btnToggle.Click += (s, e) => DoToggle();
            txtTest.TextChanged += (s, e) => Preview();
            txtDll.TextChanged += (s, e) => { RefreshStatus(); LoadSettings(); };
            foreach (RadioButton rb in new[] { rbZ, rbS, rbJ })
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) YeismoChanged(); };
            foreach (RadioButton rb in new[] { rbZsdz, rbZsts, rbZsz })
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) ZetaChanged(); };
            chkNotes.CheckedChanged += (s, e) => NotesChanged();
            foreach (RadioButton rb in new[] { rbAm, rbSo, rbSh, rbFl })
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) NotesChanged(); };
            Load += (s, e) => { AutoDetect(); LoadSettings(); NotesChanged(); Preview(); RefreshStatus(); };
        }

        void Log(string m) { log.AppendText(m + "\r\n"); }

        void AutoDetect()
        {
            string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            foreach (string c in new[] {
                @"C:\Program Files\VOCALOID7\Editor\VOCALOID7.dll",
                Path.Combine(pf, @"VOCALOID7\Editor\VOCALOID7.dll"),
                Path.Combine(pf, @"Yamaha\VOCALOID7\VOCALOID7.dll"),
                Path.Combine(pf, @"VOCALOID7\VOCALOID7.dll"),
                Path.Combine(pf, @"VOCALOID7 Editor\VOCALOID7.dll") })
                if (File.Exists(c)) { txtDll.Text = c; return; }
        }

        void Browse()
        {
            using (var d = new OpenFileDialog { Filter = "VOCALOID7.dll|VOCALOID7.dll|DLL (*.dll)|*.dll", Title = "Selecciona VOCALOID7.dll" })
                if (d.ShowDialog(this) == DialogResult.OK) txtDll.Text = d.FileName;
        }

        void RefreshStatus()
        {
            string p = txtDll.Text.Trim();
            if (p.Length == 0) { lblStatus.Text = "Estado: (elige el archivo)"; return; }
            lblStatus.Text = "Estado: " + Patcher.GetStatus(p);
            bool patched = lblStatus.Text.EndsWith("PARCHEADO");
            btnPatch.Enabled = !lblStatus.Text.Contains("No parece") && !lblStatus.Text.Contains("no encontrado") && !lblStatus.Text.Contains("No se pudo"); btnRestore.Enabled = File.Exists(Patcher.BackupPath(p));
            btnToggle.Enabled = patched;
        }

        void Preview()
        {
            string r = null;
            try { r = Hook.LyricToJapanesePhonemes(txtTest.Text); } catch { }
            txtOut.Text = r ?? "(sin conversión)";
        }

        // Ventana de ayuda no modal; si ya está abierta se trae al frente.
        void ShowHelp()
        {
            if (help != null && !help.IsDisposed)
            {
                if (help.WindowState == FormWindowState.Minimized) help.WindowState = FormWindowState.Normal;
                help.Activate();
                return;
            }
            help = new HelpForm();
            help.Icon = Icon;
            help.Show(this);
        }

        string CurrentYeismo() { return rbS.Checked ? "S" : rbJ.Checked ? "j" : "Z"; }

        // Cambia el sonido de ll / y: vista previa inmediata y, si el parche ya está instalado, se guarda en Esp2Jpn.ini
        // (el hook relee el archivo al cambiar, sin reiniciar nada).
        void YeismoChanged()
        {
            string v = CurrentYeismo();
            Hook.SetYeismo(v);
            Preview();
            if (loading) return;
            string p = txtDll.Text.Trim();
            if (p.Length > 0 && Patcher.IniExists(p))
                Log(Patcher.IniSet(p, "ll", v) ? "Sonido de ll / y = " + v + " (guardado en Esp2Jpn.ini, efecto inmediato)."
                                                : "No se pudo escribir Esp2Jpn.ini (¿permisos?). Ejecuta como administrador.");
            else Log("Sonido de ll / y = " + v + " (se aplicará con «Aplicar / re-aplicar»).");
        }

        string CurrentZeta() { return rbZsz.Checked ? "sz" : rbZsts.Checked ? "sts" : "sdz"; }

        // Cambia el esquema de la z: vista previa inmediata y, si el parche ya está instalado, se guarda en zeta= de Esp2Jpn.ini.
        void ZetaChanged()
        {
            string v = CurrentZeta();
            Hook.SetZeta(v);
            Preview();
            if (loading) return;
            string p = txtDll.Text.Trim();
            if (p.Length > 0 && Patcher.IniExists(p))
                Log(Patcher.IniSet(p, "zeta", v) ? "Sonido de z / c = " + Patcher.ZetaLabel(v) + " (guardado en Esp2Jpn.ini, efecto inmediato)."
                                                : "No se pudo escribir Esp2Jpn.ini (¿permisos?). Ejecuta como administrador.");
            else Log("Sonido de z / c = " + Patcher.ZetaLabel(v) + " (se aplicará con «Aplicar / re-aplicar»).");
        }

        void NotesChanged()
        {
            rbAm.Enabled = rbSo.Enabled = rbSh.Enabled = rbFl.Enabled = chkNotes.Checked;
            txtNotesPrev.Text = string.Join("  ", Patcher.NoteNames(rbSo.Checked ? "solfege" : "american", rbFl.Checked ? "flats" : "sharps"));
        }

        // Carga los ajustes guardados en Esp2Jpn.ini (si existe) en los controles.
        void LoadSettings()
        {
            string p = txtDll.Text.Trim();
            if (p.Length == 0 || !Patcher.IniExists(p)) return;
            loading = true;
            try
            {
                PatchOptions o = PatchOptions.FromIni(p);
                string y = o.Yeismo ?? "Z";
                if (y == "S") rbS.Checked = true; else if (y == "j") rbJ.Checked = true; else rbZ.Checked = true;
                chkNotes.Checked = o.ChangeNoteNames;
                if (o.Notation == "solfege") rbSo.Checked = true; else rbAm.Checked = true;
                if (o.Accidentals == "flats") rbFl.Checked = true; else rbSh.Checked = true;
                string z = o.Zeta ?? "sdz";
                if (z == "sz") rbZsz.Checked = true; else if (z == "sts") rbZsts.Checked = true; else rbZsdz.Checked = true;
                Hook.SetYeismo(y);
                Hook.SetZeta(z);
            }
            finally { loading = false; }
            NotesChanged();
            Preview();
        }

        PatchOptions BuildOptions()
        {
            return new PatchOptions
            {
                ChangeNoteNames = chkNotes.Checked,
                Notation = rbSo.Checked ? "solfege" : "american",
                Accidentals = rbFl.Checked ? "flats" : "sharps",
                Yeismo = CurrentYeismo(),
                Zeta = CurrentZeta()
            };
        }

        // VOCALOID7 debe estar cerrado para aplicar/restaurar. Devuelve false si el usuario cancela.
        bool EnsureVocaloidClosed(string dll, string action)
        {
            while (true)
            {
                var run = Patcher.RunningVocaloid(dll);
                if (run.Count == 0) return true;
                string m = "VOCALOID7 está abierto (" + string.Join(", ", run.ToArray()) + ").\r\n\r\n" +
                           "Ciérralo por completo y pulsa «Reintentar». Para " + action + " VOCALOID7 debe estar cerrado.";
                if (MessageBox.Show(this, m, Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                { Log("Cancelado: VOCALOID7 sigue abierto."); return false; }
            }
        }

        string HookSource() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Patcher.HookAsm + ".dll"); }

        void DoPatch()
        {
            string p = txtDll.Text.Trim();
            if (!EnsureVocaloidClosed(p, "aplicar el parche")) return;
            if (MessageBox.Show(this, "Se modificará VOCALOID7.dll (se conserva una copia de seguridad del original).\r\n\r\n¿Continuar?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UseWaitCursor = true; Application.DoEvents();
            var r = Patcher.Patch(p, HookSource(), Log, BuildOptions());
            UseWaitCursor = false;
            Log(r.Message);
            MessageBox.Show(this, r.Message, Text, MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        void DoRestore()
        {
            string p = txtDll.Text.Trim();
            if (!EnsureVocaloidClosed(p, "restaurar el original")) return;
            var r = Patcher.Restore(p, chkRemove.Checked, Log);
            Log(r.Message);
            MessageBox.Show(this, r.Message, Text, MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        void DoToggle()
        {
            try
            {
                string ini = Path.Combine(Path.GetDirectoryName(txtDll.Text.Trim()), "Esp2Jpn.ini");
                var lines = File.Exists(ini) ? new System.Collections.Generic.List<string>(File.ReadAllLines(ini))
                                             : new System.Collections.Generic.List<string> { "enabled=1", "langs=0" };
                int idx = lines.FindIndex(l => l.Replace(" ", "").ToLowerInvariant().StartsWith("enabled="));
                bool on = idx < 0 || !lines[idx].Replace(" ", "").ToLowerInvariant().Contains("enabled=0");
                string val = on ? "enabled=0" : "enabled=1";
                if (idx < 0) lines.Insert(0, val); else lines[idx] = val;
                File.WriteAllLines(ini, lines.ToArray());
                Log("Conversión " + (on ? "DESACTIVADA" : "ACTIVADA") + " (efecto inmediato).");
            }
            catch (Exception e) { Log("Error: " + e.Message); }
        }
    }

    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            // Autotest del conversor (compara con Esp2XSAMPA.py):  Esp2JpnPatcher.exe --selftest
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                try { AttachConsole(-1); } catch { }
                string rep; bool ok = Hook.SelfTest(out rep);
                Console.WriteLine(rep);
                return ok ? 0 : 1;
            }
            // Modo consola:  Esp2JpnPatcher.exe --cli patch|restore|status "ruta\VOCALOID7.dll"  [ll=Z|S|j] [zeta=sdz|sts|sz] [notes=none|american|solfege] [acc=sharps|flats]
            if (args.Length >= 3 && args[0] == "--cli")
            {
                try { AttachConsole(-1); } catch { }
                string dll = args[2];
                Action<string> log = Console.WriteLine;
                string hook = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Patcher.HookAsm + ".dll");
                if (args[1] == "status") { Console.WriteLine(Patcher.GetStatus(dll)); return 0; }
                if (args[1] == "dump")
                {
                    string d; try { d = Patcher.Dump(dll); } catch (Exception e) { d = "Error: " + e; }
                    string outp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Esp2Jpn-dump.txt");
                    File.WriteAllText(outp, d); Console.WriteLine("Escrito: " + outp); return 0;
                }
                PatchOptions po = PatchOptions.FromIni(dll);      // opciones extra:  ll=Z|S|j  zeta=sdz|sts|sz  notes=none|american|solfege  acc=sharps|flats
                for (int i = 3; i < args.Length; i++)
                { int eq = args[i].IndexOf('='); if (eq > 0) po.Apply(args[i].Substring(0, eq), args[i].Substring(eq + 1)); }
                PatchResult r = args[1] == "patch" ? Patcher.Patch(dll, hook, log, po)
                              : args[1] == "restore" ? Patcher.Restore(dll, true, log)
                              : new PatchResult { Ok = false, Message = "Comando desconocido" };
                Console.WriteLine(r.Message);
                return r.Ok ? 0 : 1;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
