// Cafetera - mantiene Teams (y Windows) "despierto" pulsando una tecla invisible (F13-F24)
// Compatible con el compilador de .NET Framework 4 que trae Windows (C# 5).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Cafetera")]
[assembly: System.Reflection.AssemblyProduct("Cafetera")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace Cafetera
{
    enum Modo { Horario = 0, SiempreActivo = 1, Desactivado = 2 }

    // ------------------------------------------------------------------ Win32
    static class Nativo
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;

        public static double SegundosInactivo()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii)) return 0;
            uint ahora = unchecked((uint)Environment.TickCount);
            uint diff = unchecked(ahora - lii.dwTime);
            return diff / 1000.0;
        }

        // numeroF: 1..24  (F1 = 0x70 ... F24 = 0x87)
        public static void PulsarTecla(int numeroF)
        {
            byte vk = (byte)(0x70 + numeroF - 1);
            byte scan = (byte)MapVirtualKey(vk, 0);
            keybd_event(vk, scan, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(vk, scan, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    // ------------------------------------------------------------------ Configuración
    class Config
    {
        public Modo Modo = Modo.Horario;
        // Índice = DayOfWeek (0 = domingo ... 6 = sábado)
        public bool[] Dias = new bool[] { false, true, true, true, true, true, false };
        public int InicioMin = 7 * 60;
        public int FinMin = 17 * 60;
        public int IntervaloSeg = 60;
        public int Tecla = 15;
        public bool SoloSiInactivo = true;
        public bool EvitarSuspension = true;

        public static string Carpeta
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cafetera"); }
        }
        public static string Ruta { get { return Path.Combine(Carpeta, "config.ini"); } }

        public Config Clonar()
        {
            Config c = (Config)MemberwiseClone();
            c.Dias = (bool[])Dias.Clone();
            return c;
        }

        static int Limitar(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }

        static int LeerHora(string v, int defecto)
        {
            string[] p = v.Split(':');
            int h, m;
            if (p.Length == 2 && int.TryParse(p[0], out h) && int.TryParse(p[1], out m))
                return Limitar(h, 0, 23) * 60 + Limitar(m, 0, 59);
            return defecto;
        }

        static string Hora(int min) { return (min / 60).ToString("00") + ":" + (min % 60).ToString("00"); }

        static bool LeerBool(string v) { v = v.ToLowerInvariant(); return v == "1" || v == "true" || v == "si"; }

        public static Config Cargar(out bool nuevo)
        {
            Config c = new Config();
            nuevo = !File.Exists(Ruta);
            if (nuevo) return c;
            string[] lineas;
            try { lineas = File.ReadAllLines(Ruta); } catch { return c; }
            foreach (string linea in lineas)
            {
                if (linea.StartsWith(";") || linea.StartsWith("#")) continue;
                int p = linea.IndexOf('=');
                if (p <= 0) continue;
                string k = linea.Substring(0, p).Trim().ToLowerInvariant();
                string v = linea.Substring(p + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "modo": c.Modo = (Modo)Enum.Parse(typeof(Modo), v, true); break;
                        case "dias": // orden L M X J V S D
                            for (int i = 0; i < 7 && i < v.Length; i++) c.Dias[(i + 1) % 7] = v[i] == '1';
                            break;
                        case "inicio": c.InicioMin = LeerHora(v, c.InicioMin); break;
                        case "fin": c.FinMin = LeerHora(v, c.FinMin); break;
                        case "intervalo": c.IntervaloSeg = Limitar(int.Parse(v), 10, 3600); break;
                        case "tecla": c.Tecla = Limitar(int.Parse(v.TrimStart('F', 'f')), 1, 24); break;
                        case "solosiinactivo": c.SoloSiInactivo = LeerBool(v); break;
                        case "evitarsuspension": c.EvitarSuspension = LeerBool(v); break;
                    }
                }
                catch { }
            }
            return c;
        }

        public void Guardar()
        {
            StringBuilder dias = new StringBuilder();
            for (int i = 0; i < 7; i++) dias.Append(Dias[(i + 1) % 7] ? '1' : '0');
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Configuración de Cafetera");
            sb.AppendLine("; modo: Horario | SiempreActivo | Desactivado");
            sb.AppendLine("; dias: L M X J V S D (1 = activo)");
            sb.AppendLine("modo=" + Modo);
            sb.AppendLine("dias=" + dias);
            sb.AppendLine("inicio=" + Hora(InicioMin));
            sb.AppendLine("fin=" + Hora(FinMin));
            sb.AppendLine("intervalo=" + IntervaloSeg);
            sb.AppendLine("tecla=F" + Tecla);
            sb.AppendLine("soloSiInactivo=" + (SoloSiInactivo ? "1" : "0"));
            sb.AppendLine("evitarSuspension=" + (EvitarSuspension ? "1" : "0"));
            try
            {
                Directory.CreateDirectory(Carpeta);
                File.WriteAllText(Ruta, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public string ResumenDias()
        {
            string[] letras = { "L", "M", "X", "J", "V", "S", "D" };
            bool lv = Dias[1] && Dias[2] && Dias[3] && Dias[4] && Dias[5];
            if (lv && !Dias[6] && !Dias[0]) return "L-V";
            if (lv && Dias[6] && Dias[0]) return "todos los días";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 7; i++) if (Dias[(i + 1) % 7]) sb.Append(letras[i]);
            return sb.Length == 0 ? "ningún día" : sb.ToString();
        }

        public string ResumenHorario()
        {
            return ResumenDias() + " " + Hora(InicioMin) + "-" + Hora(FinMin);
        }

        public bool DebeEstarActivo(DateTime ahora)
        {
            if (Modo == Modo.SiempreActivo) return true;
            if (Modo == Modo.Desactivado) return false;
            int min = ahora.Hour * 60 + ahora.Minute;
            int hoy = (int)ahora.DayOfWeek;
            int ayer = (hoy + 6) % 7;
            if (InicioMin == FinMin) return Dias[hoy];                         // todo el día
            if (InicioMin < FinMin) return Dias[hoy] && min >= InicioMin && min < FinMin;
            // Cruza medianoche (p. ej. 22:00-06:00): el día marcado es el de inicio
            return (Dias[hoy] && min >= InicioMin) || (Dias[ayer] && min < FinMin);
        }
    }

    // ------------------------------------------------------------------ Inicio con Windows
    static class InicioWindows
    {
        const string Clave = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Nombre = "Cafetera";

        static string Comando { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool Activo()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Clave, false))
                    return k != null && k.GetValue(Nombre) != null;
            }
            catch { return false; }
        }

        public static void Establecer(bool activar)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Clave, true))
                {
                    if (k == null) return;
                    if (activar) k.SetValue(Nombre, Comando);
                    else if (k.GetValue(Nombre) != null) k.DeleteValue(Nombre, false);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Iconos dibujados por código
    // Ojo de línea negra
    static class Iconos
    {
        static readonly Color Negro = Color.Black;
        static readonly Color Gris = Color.FromArgb(0x8C, 0x8C, 0x8C);

        // estado: 0 = activa (ojo abierto), 1 = en espera (ojo cerrado negro), 2 = desactivada (ojo cerrado gris)
        public static Icon Crear(int estado)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap bmp = Dibujar(s, estado))
                return Icon.FromHandle(bmp.GetHicon());   // se crean 3 al arrancar y viven toda la ejecución
        }

        static Bitmap Dibujar(int s, int estado)
        {
            float u = s / 16f;
            Bitmap bmp = new Bitmap(s, s);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                if (estado == 0)
                {
                    using (GraphicsPath ojo = new GraphicsPath())
                    using (Pen linea = new Pen(Negro, 1.3f * u))
                    using (SolidBrush pupila = new SolidBrush(Negro))
                    {
                        ojo.AddBezier(1f * u, 8f * u, 3.5f * u, 3f * u, 12.5f * u, 3f * u, 15f * u, 8f * u);
                        ojo.AddBezier(15f * u, 8f * u, 12.5f * u, 13f * u, 3.5f * u, 13f * u, 1f * u, 8f * u);
                        ojo.CloseFigure();
                        linea.LineJoin = LineJoin.Round;
                        g.DrawPath(linea, ojo);
                        g.FillEllipse(pupila, 5.7f * u, 5.7f * u, 4.6f * u, 4.6f * u);
                    }
                }
                else
                {
                    using (Pen p = new Pen(estado == 1 ? Negro : Gris, 1.5f * u))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawBezier(p, 1.5f * u, 7f * u, 4f * u, 10.6f * u, 12f * u, 10.6f * u, 14.5f * u, 7f * u);
                        g.DrawLine(p, 4f * u, 9.6f * u, 3f * u, 11.4f * u);
                        g.DrawLine(p, 8f * u, 10.4f * u, 8f * u, 12.5f * u);
                        g.DrawLine(p, 12f * u, 9.6f * u, 13f * u, 11.4f * u);
                    }
                }
            }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ Ventana de configuración
    class FormConfig : Form
    {
        RadioButton rbHorario, rbSiempre, rbOff;
        CheckBox[] chkDias = new CheckBox[7];   // orden L..D
        DateTimePicker dtDesde, dtHasta;
        NumericUpDown numIntervalo;
        ComboBox cmbTecla;
        CheckBox chkSoloInactivo, chkSuspension, chkInicio;

        public Config Resultado;
        public bool IniciarConWindows;

        public FormConfig(Config cfg, bool inicioActivo, Icon icono)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Cafetera - Configuración";
            Icon = icono;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(420, 452);

            // Modo
            GroupBox gModo = Grupo("Modo", 12, 10, 396, 56);
            rbHorario = Radio(gModo, "Según horario", 14, 22, cfg.Modo == Modo.Horario);
            rbSiempre = Radio(gModo, "Siempre activa", 140, 22, cfg.Modo == Modo.SiempreActivo);
            rbOff = Radio(gModo, "Desactivada", 270, 22, cfg.Modo == Modo.Desactivado);

            // Horario
            GroupBox gHor = Grupo("Horario", 12, 74, 396, 126);
            string[] letras = { "L", "M", "X", "J", "V", "S", "D" };
            Etiqueta(gHor, "Días:", 14, 26);
            for (int i = 0; i < 7; i++)
            {
                CheckBox c = new CheckBox();
                c.Text = letras[i];
                c.AutoSize = true;
                c.Location = new Point(64 + i * 46, 24);
                c.Checked = cfg.Dias[(i + 1) % 7];
                gHor.Controls.Add(c);
                chkDias[i] = c;
            }
            Etiqueta(gHor, "Desde:", 14, 62);
            dtDesde = Reloj(gHor, 64, 58, cfg.InicioMin);
            Etiqueta(gHor, "Hasta:", 170, 62);
            dtHasta = Reloj(gHor, 218, 58, cfg.FinMin);
            Label nota = Etiqueta(gHor, "Si \"Hasta\" es anterior a \"Desde\", el horario cruza la medianoche.", 14, 96);
            nota.ForeColor = SystemColors.GrayText;

            // Pulsación
            GroupBox gPul = Grupo("Pulsación", 12, 208, 396, 118);
            Etiqueta(gPul, "Tecla:", 14, 28);
            cmbTecla = new ComboBox();
            cmbTecla.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTecla.Location = new Point(64, 24);
            cmbTecla.Width = 70;
            for (int f = 13; f <= 24; f++) cmbTecla.Items.Add("F" + f);
            int idx = cfg.Tecla - 13;
            cmbTecla.SelectedIndex = (idx >= 0 && idx < 12) ? idx : 2;
            gPul.Controls.Add(cmbTecla);
            Label tnota = Etiqueta(gPul, "(F13-F24 no existen en el teclado: no hacen nada)", 142, 28);
            tnota.ForeColor = SystemColors.GrayText;

            Etiqueta(gPul, "Cada:", 14, 60);
            numIntervalo = new NumericUpDown();
            numIntervalo.Location = new Point(64, 56);
            numIntervalo.Width = 70;
            numIntervalo.Minimum = 10; numIntervalo.Maximum = 3600; numIntervalo.Increment = 5;
            numIntervalo.Value = cfg.IntervaloSeg;
            gPul.Controls.Add(numIntervalo);
            Etiqueta(gPul, "segundos", 142, 60);

            chkSoloInactivo = Check(gPul, "Solo si no he tocado teclado ni ratón en ese tiempo", 14, 88, cfg.SoloSiInactivo);

            // Otros
            chkSuspension = Check(this, "Evitar que el PC se suspenda o apague la pantalla (mientras esté activa)", 18, 336, cfg.EvitarSuspension);
            chkInicio = Check(this, "Iniciar con Windows", 18, 362, inicioActivo);

            Button ok = new Button();
            ok.Text = "Guardar"; ok.Size = new Size(96, 30); ok.Location = new Point(208, 406);
            ok.Click += delegate { Guardar(); };
            Button cancelar = new Button();
            cancelar.Text = "Cancelar"; cancelar.Size = new Size(96, 30); cancelar.Location = new Point(312, 406);
            cancelar.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancelar);
            AcceptButton = ok; CancelButton = cancelar;

            ResumeLayout(false);
            PerformLayout();
        }

        void Guardar()
        {
            Config c = new Config();
            c.Modo = rbSiempre.Checked ? Modo.SiempreActivo : (rbOff.Checked ? Modo.Desactivado : Modo.Horario);
            for (int i = 0; i < 7; i++) c.Dias[(i + 1) % 7] = chkDias[i].Checked;
            c.InicioMin = dtDesde.Value.Hour * 60 + dtDesde.Value.Minute;
            c.FinMin = dtHasta.Value.Hour * 60 + dtHasta.Value.Minute;
            c.IntervaloSeg = (int)numIntervalo.Value;
            c.Tecla = 13 + cmbTecla.SelectedIndex;
            c.SoloSiInactivo = chkSoloInactivo.Checked;
            c.EvitarSuspension = chkSuspension.Checked;
            Resultado = c;
            IniciarConWindows = chkInicio.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        GroupBox Grupo(string texto, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = texto; g.Location = new Point(x, y); g.Size = new Size(w, h);
            Controls.Add(g);
            return g;
        }

        static Label Etiqueta(Control padre, string texto, int x, int y)
        {
            Label l = new Label();
            l.Text = texto; l.AutoSize = true; l.Location = new Point(x, y);
            padre.Controls.Add(l);
            return l;
        }

        static RadioButton Radio(Control padre, string texto, int x, int y, bool marcado)
        {
            RadioButton r = new RadioButton();
            r.Text = texto; r.AutoSize = true; r.Location = new Point(x, y); r.Checked = marcado;
            padre.Controls.Add(r);
            return r;
        }

        static CheckBox Check(Control padre, string texto, int x, int y, bool marcado)
        {
            CheckBox c = new CheckBox();
            c.Text = texto; c.AutoSize = true; c.Location = new Point(x, y); c.Checked = marcado;
            padre.Controls.Add(c);
            return c;
        }

        static DateTimePicker Reloj(Control padre, int x, int y, int minutos)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = "HH:mm";
            d.ShowUpDown = true;
            d.Width = 80;
            d.Location = new Point(x, y);
            d.Value = DateTime.Today.AddMinutes(minutos);
            padre.Controls.Add(d);
            return d;
        }
    }

    // ------------------------------------------------------------------ Aplicación de bandeja
    class App : ApplicationContext
    {
        Config cfg;
        NotifyIcon icono;
        System.Windows.Forms.Timer timer;
        Icon icoActivo, icoEspera, icoOff;
        ToolStripMenuItem miEstado, miUltima, miHorario, miSiempre, miOff, miInicio;
        DateTime ultimaPulsacion = DateTime.MinValue;
        int estadoActual = -1;           // 0 = activa, 1 = en espera, 2 = desactivada
        bool despiertoAplicado = false;
        bool configAbierta = false;
        int clics = 0;
        System.Windows.Forms.Timer timerClics;

        public App()
        {
            bool nuevo;
            cfg = Config.Cargar(out nuevo);

            icoActivo = Iconos.Crear(0);
            icoEspera = Iconos.Crear(1);
            icoOff = Iconos.Crear(2);

            ContextMenuStrip menu = new ContextMenuStrip();
            miEstado = new ToolStripMenuItem("Cafetera"); miEstado.Enabled = false;
            miUltima = new ToolStripMenuItem("Última pulsación: -"); miUltima.Enabled = false;
            miHorario = new ToolStripMenuItem("Según horario", null, delegate { CambiarModo(Modo.Horario); });
            miSiempre = new ToolStripMenuItem("Siempre activa", null, delegate { CambiarModo(Modo.SiempreActivo); });
            miOff = new ToolStripMenuItem("Desactivada", null, delegate { CambiarModo(Modo.Desactivado); });
            ToolStripMenuItem miConfig = new ToolStripMenuItem("Configuración...", null, delegate { AbrirConfig(); });
            miConfig.Font = new Font(miConfig.Font, FontStyle.Bold);
            miInicio = new ToolStripMenuItem("Iniciar con Windows", null, delegate
            {
                InicioWindows.Establecer(!InicioWindows.Activo());
            });
            ToolStripMenuItem miSalir = new ToolStripMenuItem("Salir", null, delegate { Salir(); });

            menu.Items.Add(miEstado);
            menu.Items.Add(miUltima);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miHorario);
            menu.Items.Add(miSiempre);
            menu.Items.Add(miOff);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miConfig);
            menu.Items.Add(miInicio);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miSalir);
            menu.Opening += delegate { RefrescarMenu(); };

            icono = new NotifyIcon();
            icono.Icon = icoEspera;
            icono.Text = "Cafetera";
            icono.ContextMenuStrip = menu;
            icono.Visible = true;
            // 2 clics = activar/desactivar · 3 clics = configuración
            timerClics = new System.Windows.Forms.Timer();
            timerClics.Interval = Math.Max(250, SystemInformation.DoubleClickTime);
            timerClics.Tick += delegate { timerClics.Stop(); ProcesarClics(); };
            icono.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                clics++;
                timerClics.Stop();
                if (clics >= 3) { ProcesarClics(); return; }
                timerClics.Start();
            };

            if (nuevo)
            {
                cfg.Guardar();
                InicioWindows.Establecer(true);
                icono.ShowBalloonTip(6000, "Cafetera",
                    "Estoy aquí, junto al reloj. Activa " + cfg.ResumenHorario() +
                    ", pulsando F" + cfg.Tecla + " cada " + cfg.IntervaloSeg + " s.\nClic derecho para configurar.",
                    ToolTipIcon.Info);
            }
            else if (InicioWindows.Activo())
            {
                InicioWindows.Establecer(true);   // actualiza la ruta por si se ha movido el .exe
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 5000;
            timer.Tick += delegate { Comprobar(); };
            timer.Start();
            Comprobar();
        }

        void Comprobar()
        {
            DateTime ahora = DateTime.Now;
            bool activa = cfg.DebeEstarActivo(ahora);
            int estado = activa ? 0 : (cfg.Modo == Modo.Desactivado ? 2 : 1);
            if (estado != estadoActual)
            {
                estadoActual = estado;
                ActualizarIcono();
            }
            MantenerDespierto(activa && cfg.EvitarSuspension);
            if (!activa) return;

            double desdeUltima = (ahora - ultimaPulsacion).TotalSeconds;
            bool toca;
            if (cfg.SoloSiInactivo)
                toca = Nativo.SegundosInactivo() >= cfg.IntervaloSeg - 2 && desdeUltima >= cfg.IntervaloSeg - 2;
            else
                toca = desdeUltima >= cfg.IntervaloSeg - 2;

            if (toca)
            {
                Nativo.PulsarTecla(cfg.Tecla);
                ultimaPulsacion = ahora;
            }
        }

        void MantenerDespierto(bool si)
        {
            if (si == despiertoAplicado) return;
            despiertoAplicado = si;
            if (si)
                Nativo.SetThreadExecutionState(Nativo.ES_CONTINUOUS | Nativo.ES_SYSTEM_REQUIRED | Nativo.ES_DISPLAY_REQUIRED);
            else
                Nativo.SetThreadExecutionState(Nativo.ES_CONTINUOUS);
        }

        string TextoEstado()
        {
            if (estadoActual == 0)
                return "Activa: F" + cfg.Tecla + " cada " + cfg.IntervaloSeg + " s" +
                       (cfg.Modo == Modo.SiempreActivo ? " (siempre)" : "");
            if (estadoActual == 1) return "En espera (" + cfg.ResumenHorario() + ")";
            return "Desactivada";
        }

        void ActualizarIcono()
        {
            icono.Icon = estadoActual == 0 ? icoActivo : (estadoActual == 1 ? icoEspera : icoOff);
            string t = "Cafetera - " + TextoEstado();
            icono.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void RefrescarMenu()
        {
            miEstado.Text = TextoEstado();
            miUltima.Text = ultimaPulsacion == DateTime.MinValue
                ? "Última pulsación: todavía ninguna"
                : "Última pulsación: " + ultimaPulsacion.ToString("HH:mm:ss");
            miHorario.Checked = cfg.Modo == Modo.Horario;
            miHorario.Text = "Según horario (" + cfg.ResumenHorario() + ")";
            miSiempre.Checked = cfg.Modo == Modo.SiempreActivo;
            miOff.Checked = cfg.Modo == Modo.Desactivado;
            miInicio.Checked = InicioWindows.Activo();
        }

        void ProcesarClics()
        {
            int n = clics;
            clics = 0;
            if (n == 2) Alternar();
            else if (n >= 3) AbrirConfig();
        }

        // Si está activa -> Desactivada.
        // Si no -> vuelve a "Según horario" si eso la activa ahora; si no, "Siempre activa".
        void Alternar()
        {
            if (cfg.DebeEstarActivo(DateTime.Now)) { CambiarModo(Modo.Desactivado); return; }
            Config prueba = cfg.Clonar();
            prueba.Modo = Modo.Horario;
            CambiarModo(prueba.DebeEstarActivo(DateTime.Now) ? Modo.Horario : Modo.SiempreActivo);
        }

        void CambiarModo(Modo m)
        {
            cfg.Modo = m;
            cfg.Guardar();
            estadoActual = -1;
            Comprobar();
        }

        void AbrirConfig()
        {
            if (configAbierta) return;
            configAbierta = true;
            try
            {
                using (FormConfig f = new FormConfig(cfg.Clonar(), InicioWindows.Activo(), icoActivo))
                {
                    if (f.ShowDialog() == DialogResult.OK && f.Resultado != null)
                    {
                        cfg = f.Resultado;
                        cfg.Guardar();
                        InicioWindows.Establecer(f.IniciarConWindows);
                        estadoActual = -1;
                        Comprobar();
                    }
                }
            }
            finally { configAbierta = false; }
        }

        void Salir()
        {
            timer.Stop();
            timerClics.Stop();
            MantenerDespierto(false);
            icono.Visible = false;
            icono.Dispose();
            ExitThread();
        }
    }

    // ------------------------------------------------------------------ Main
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool creado;
            using (Mutex m = new Mutex(true, "Local\\Cafetera_InstanciaUnica", out creado))
            {
                if (!creado)
                {
                    MessageBox.Show("Cafetera ya se está ejecutando.\nBusca el ojo junto al reloj (puede estar en la flecha ^).",
                        "Cafetera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Nativo.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
                GC.KeepAlive(m);
            }
        }
    }
}
