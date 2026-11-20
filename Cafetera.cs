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
    // Ojo de línea: abierto = activa, cerrado = en espera / desactivada
    static class Iconos
    {
        public static readonly Color Gris = Color.FromArgb(0x8C, 0x8C, 0x8C);

        public static Icon Crear(int estado, Color c)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap bmp = new Bitmap(s, s))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Pintar(g, 0, 0, s, estado, c);
                }
                return Icon.FromHandle(bmp.GetHicon());   // se crean al arrancar y viven toda la ejecución
            }
        }

        // estado: 0 = ojo abierto, cualquier otro = ojo cerrado
        public static void Pintar(Graphics g, float x0, float y0, float s, int estado, Color c)
        {
            float u = s / 16f;
            GraphicsState st = g.Save();
            g.TranslateTransform(x0, y0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (estado == 0)
            {
                using (GraphicsPath ojo = new GraphicsPath())
                using (Pen linea = new Pen(c, 1.3f * u))
                using (SolidBrush pupila = new SolidBrush(c))
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
                using (Pen p = new Pen(c, 1.5f * u))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, 1.5f * u, 7f * u, 4f * u, 10.6f * u, 12f * u, 10.6f * u, 14.5f * u, 7f * u);
                    g.DrawLine(p, 4f * u, 9.6f * u, 3f * u, 11.4f * u);
                    g.DrawLine(p, 8f * u, 10.4f * u, 8f * u, 12.5f * u);
                    g.DrawLine(p, 12f * u, 9.6f * u, 13f * u, 11.4f * u);
                }
            }
            g.Restore(st);
        }
    }

    // ------------------------------------------------------------------ Tema oscuro
    static class Tema
    {
        public static readonly Color Fondo = Color.FromArgb(0x18, 0x19, 0x1D);
        public static readonly Color Tarjeta = Color.FromArgb(0x23, 0x25, 0x2A);
        public static readonly Color Caja = Color.FromArgb(0x30, 0x33, 0x39);
        public static readonly Color CajaHover = Color.FromArgb(0x3C, 0x40, 0x47);
        public static readonly Color Borde = Color.FromArgb(0x3A, 0x3D, 0x44);
        public static readonly Color Texto = Color.FromArgb(0xEC, 0xED, 0xF0);
        public static readonly Color Suave = Color.FromArgb(0x95, 0x9B, 0xA5);
        public static readonly Color Acento = Color.FromArgb(0x3D, 0xD6, 0x8C);
        public static readonly Color AcentoHover = Color.FromArgb(0x5E, 0xE2, 0xA2);
        public static readonly Color AcentoTexto = Color.FromArgb(0x0C, 0x26, 0x19);
        public static readonly Color Ambar = Color.FromArgb(0xF2, 0xB8, 0x3A);

        public static GraphicsPath Redondo(RectangleF r, float radio)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            if (d < 1f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Suavizar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Texto_(Graphics g, string t, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, t, f, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                  TextFormatFlags.SingleLine | extra);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Barra de título oscura (Windows 10 2004+ / Windows 11)
        public static void TituloOscuro(Form f)
        {
            try
            {
                int si = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref si, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref si, 4);
                int color = Fondo.R | (Fondo.G << 8) | (Fondo.B << 16);   // Windows 11: color del título
                DwmSetWindowAttribute(f.Handle, 35, ref color, 4);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Menú del icono en oscuro
    class ColoresMenu : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Tema.Tarjeta; } }
        public override Color ImageMarginGradientBegin { get { return Tema.Tarjeta; } }
        public override Color ImageMarginGradientMiddle { get { return Tema.Tarjeta; } }
        public override Color ImageMarginGradientEnd { get { return Tema.Tarjeta; } }
        public override Color MenuBorder { get { return Tema.Borde; } }
        public override Color MenuItemBorder { get { return Tema.CajaHover; } }
        public override Color MenuItemSelected { get { return Tema.CajaHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Tema.CajaHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Tema.CajaHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Tema.CajaHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Tema.CajaHover; } }
        public override Color SeparatorDark { get { return Tema.Borde; } }
        public override Color SeparatorLight { get { return Tema.Tarjeta; } }
        public override Color CheckBackground { get { return Tema.Tarjeta; } }
        public override Color CheckSelectedBackground { get { return Tema.CajaHover; } }
        public override Color CheckPressedBackground { get { return Tema.CajaHover; } }
    }

    class RenderMenu : ToolStripProfessionalRenderer
    {
        public RenderMenu() : base(new ColoresMenu()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
                e.Item.Enabled ? Tema.Texto : Tema.Suave, e.TextFormat);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            Tema.Suavizar(g);
            Rectangle r = e.ImageRectangle;
            float d = Math.Min(r.Width, r.Height) * 0.45f;
            using (SolidBrush b = new SolidBrush(Tema.Acento))
                g.FillEllipse(b, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
        }
    }

    // ------------------------------------------------------------------ Controles propios (modernos)
    class ControlBase : Control
    {
        protected bool encima;

        public ControlBase()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);   // clics rápidos = varios clics
            Cursor = Cursors.Hand;
            ForeColor = Tema.Texto;
        }
        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
    }

    class Interruptor : ControlBase
    {
        bool marcado;
        public bool Checked
        {
            get { return marcado; }
            set { if (marcado != value) { marcado = value; Invalidate(); } }
        }
        public Interruptor() { Size = new Size(42, 24); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fondo = marcado ? (encima ? Tema.AcentoHover : Tema.Acento) : (encima ? Tema.CajaHover : Tema.Caja);
            using (GraphicsPath p = Tema.Redondo(r, r.Height / 2))
            using (SolidBrush b = new SolidBrush(fondo))
                g.FillPath(b, p);
            float m = r.Height * 0.18f;
            float d = r.Height - 2 * m;
            float x = marcado ? r.Right - d - m : r.X + m;
            using (SolidBrush b = new SolidBrush(marcado ? Tema.AcentoTexto : Tema.Suave))
                g.FillEllipse(b, x, r.Y + m, d, d);
        }
    }

    class Pastilla : ControlBase
    {
        bool marcado;
        public bool Checked
        {
            get { return marcado; }
            set { if (marcado != value) { marcado = value; Invalidate(); } }
        }
        public Pastilla(string texto) { Text = texto; Size = new Size(36, 36); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            Color fondo = marcado ? (encima ? Tema.AcentoHover : Tema.Acento) : (encima ? Tema.CajaHover : Tema.Caja);
            using (SolidBrush b = new SolidBrush(fondo))
                g.FillEllipse(b, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (Font f = new Font(Font, FontStyle.Bold))
                Tema.Texto_(g, Text, f, ClientRectangle, marcado ? Tema.AcentoTexto : Tema.Suave,
                            TextFormatFlags.HorizontalCenter);
        }
    }

    class Segmentado : ControlBase
    {
        string[] opciones;
        int sel, hover = -1;
        public int Seleccion
        {
            get { return sel; }
            set { if (sel != value) { sel = value; Invalidate(); } }
        }
        public Segmentado(string[] opciones) { this.opciones = opciones; Size = new Size(300, 36); }

        int Indice(int x)
        {
            float w = (Width - 6f) / opciones.Length;
            int i = (int)((x - 3) / w);
            return Math.Max(0, Math.Min(opciones.Length - 1, i));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Indice(e.X);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Seleccion = Indice(e.X);
            base.OnMouseDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Tema.Redondo(r, r.Height * 0.3f))
            using (SolidBrush b = new SolidBrush(Tema.Caja))
                g.FillPath(b, p);
            float w = (Width - 6f) / opciones.Length;
            using (Font negrita = new Font(Font, FontStyle.Bold))
            {
                for (int i = 0; i < opciones.Length; i++)
                {
                    RectangleF s = new RectangleF(3 + i * w, 3, w, Height - 6);
                    if (i == sel || i == hover)
                    {
                        using (GraphicsPath p = Tema.Redondo(s, s.Height * 0.28f))
                        using (SolidBrush b = new SolidBrush(i == sel ? Tema.Acento : Tema.CajaHover))
                            g.FillPath(b, p);
                    }
                    Rectangle rt = Rectangle.Round(s);
                    Tema.Texto_(g, opciones[i], i == sel ? negrita : Font, rt,
                                i == sel ? Tema.AcentoTexto : Tema.Texto, TextFormatFlags.HorizontalCenter);
                }
            }
        }
    }

    // ‹ valor ›  — clic en las flechas (mantener pulsado repite), rueda del ratón o cursores
    class Selector : ControlBase
    {
        int valor;
        public int Min, Max, Paso = 1;
        public bool Circular;
        public Func<int, string> Formato = delegate(int v) { return v.ToString(); };
        int zonaHover, dir, repeticiones;
        System.Windows.Forms.Timer repetir;

        public int Valor
        {
            get { return valor; }
            set { valor = value; Invalidate(); }
        }

        public Selector()
        {
            Size = new Size(132, 34);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            repetir = new System.Windows.Forms.Timer();
            repetir.Tick += delegate
            {
                repeticiones++;
                repetir.Interval = 70;
                Mover(dir * (repeticiones > 15 ? 4 : 1));
            };
        }

        void Mover(int pasos)
        {
            int v = valor + pasos * Paso;
            if (Circular)
            {
                int rango = Max - Min + Paso;
                v = Min + (((v - Min) % rango) + rango) % rango;
            }
            else v = Math.Max(Min, Math.Min(Max, v));
            Valor = v;
        }

        int Zona(int x)
        {
            if (x < Height) return -1;
            if (x > Width - Height) return 1;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Zona(e.X);
            if (z != zonaHover) { zonaHover = z; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { zonaHover = 0; repetir.Stop(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                dir = Zona(e.X);
                if (dir != 0)
                {
                    Mover(dir);
                    repeticiones = 0;
                    repetir.Interval = 400;
                    repetir.Start();
                }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { repetir.Stop(); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Mover(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }
        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) { Mover(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) { Mover(-1); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Tema.Redondo(r, r.Height * 0.3f))
            {
                using (SolidBrush b = new SolidBrush(Tema.Caja)) g.FillPath(b, p);
                if (encima && zonaHover != 0)
                {
                    g.SetClip(p);
                    float zx = zonaHover < 0 ? 0 : Width - Height;
                    using (SolidBrush b = new SolidBrush(Tema.CajaHover)) g.FillRectangle(b, zx, 0, Height, Height);
                    g.ResetClip();
                }
                using (Pen borde = new Pen(Focused ? Tema.Acento : Tema.Caja, 1f)) g.DrawPath(borde, p);
            }
            float s = Height * 0.13f;
            using (Pen flecha = new Pen(Tema.Suave, Math.Max(1.5f, Height * 0.06f)))
            {
                flecha.StartCap = LineCap.Round; flecha.EndCap = LineCap.Round; flecha.LineJoin = LineJoin.Round;
                float cx = Height / 2f, cy = Height / 2f;
                g.DrawLines(flecha, new PointF[] { new PointF(cx + s / 2, cy - s), new PointF(cx - s / 2, cy), new PointF(cx + s / 2, cy + s) });
                cx = Width - Height / 2f;
                g.DrawLines(flecha, new PointF[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
            }
            using (Font negrita = new Font(Font, FontStyle.Bold))
                Tema.Texto_(g, Formato(valor), negrita, new Rectangle(Height, 0, Width - 2 * Height, Height),
                            Tema.Texto, TextFormatFlags.HorizontalCenter);
        }
    }

    class Boton : ControlBase
    {
        public bool Primario;
        public Boton(string texto, bool primario) { Text = texto; Primario = primario; Size = new Size(110, 36); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            Color fondo = Primario ? (encima ? Tema.AcentoHover : Tema.Acento) : (encima ? Tema.CajaHover : Tema.Caja);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Tema.Redondo(r, r.Height * 0.3f))
            using (SolidBrush b = new SolidBrush(fondo))
                g.FillPath(b, p);
            using (Font negrita = new Font(Font, FontStyle.Bold))
                Tema.Texto_(g, Text, negrita, ClientRectangle, Primario ? Tema.AcentoTexto : Tema.Texto,
                            TextFormatFlags.HorizontalCenter);
        }
    }

    class Tarjeta : Panel
    {
        public string Titulo;
        public Tarjeta(string titulo)
        {
            Titulo = titulo;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Tema.Tarjeta;
            ForeColor = Tema.Texto;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Tema.Fondo);
            Tema.Suavizar(g);
            float esc = g.DpiX / 96f;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Tema.Redondo(r, 12 * esc))
            {
                using (SolidBrush b = new SolidBrush(Tema.Tarjeta)) g.FillPath(b, p);
                using (Pen borde = new Pen(Color.FromArgb(0x2E, 0x31, 0x37))) g.DrawPath(borde, p);
            }
            if (!string.IsNullOrEmpty(Titulo))
                using (Font f = new Font("Segoe UI Semibold", 8F))
                    TextRenderer.DrawText(g, Titulo.ToUpperInvariant(), f, new Point((int)(16 * esc), (int)(12 * esc)), Tema.Suave);
        }
    }

    class OjoEstado : Control
    {
        public int Estado;
        public OjoEstado()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(48, 48);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            Color c = Estado == 0 ? Tema.Acento : (Estado == 1 ? Tema.Ambar : Tema.Suave);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, c)))
                g.FillEllipse(b, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float s = Width * 0.62f;
            Iconos.Pintar(g, (Width - s) / 2f, (Height - s) / 2f, s, Estado, c);
        }
    }

    // ------------------------------------------------------------------ Ventana de configuración (modo oscuro)
    class FormConfig : Form
    {
        App app;
        OjoEstado ojo;
        Label lblEstado, lblHora, lblHace, lblInactivo;
        Segmentado segModo;
        Pastilla[] dias = new Pastilla[7];   // orden L..D
        Selector selDesde, selHasta, selTecla, selIntervalo;
        Interruptor swInactivo, swSuspension, swInicio;
        System.Windows.Forms.Timer refresco;

        public Config Resultado;
        public bool IniciarConWindows;

        static string Hora(int min) { return (min / 60).ToString("00") + ":" + (min % 60).ToString("00"); }

        static string Intervalo(int s)
        {
            if (s < 60) return s + " s";
            if (s % 60 == 0) return (s / 60) + " min";
            return (s / 60) + " min " + (s % 60) + " s";
        }

        static string Hace(double seg)
        {
            if (seg < 60) return "hace " + (int)seg + " s";
            if (seg < 3600) return "hace " + (int)(seg / 60) + " min";
            return "hace " + (int)(seg / 3600) + " h";
        }

        public FormConfig(Config cfg, bool inicioActivo, Icon icono, App app)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.75F);
            Text = "Cafetera";
            Icon = icono;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(768, 432);

            const int izq = 16, der = 392, ancho = 360;

            // ---------- Cabecera
            ojo = new OjoEstado();
            ojo.Location = new Point(izq, 14);
            Controls.Add(ojo);
            Etiqueta(this, "Cafetera", 74, 12, new Font("Segoe UI Semibold", 15F), Tema.Texto);
            lblEstado = Etiqueta(this, "", 76, 42, new Font("Segoe UI", 9.75F), Tema.Suave);

            // ---------- Actividad
            Tarjeta tAct = Tarjeta_("Última pulsación", izq, 76, ancho, 132);
            lblHora = Etiqueta(tAct, "--:--:--", 14, 30, new Font("Segoe UI Semibold", 22F), Tema.Texto);
            lblHace = Etiqueta(tAct, "", 17, 78, Font, Tema.Suave);
            lblInactivo = Etiqueta(tAct, "", 17, 100, Font, Tema.Suave);
            Boton probar = new Boton("Probar ahora", false);
            probar.Location = new Point(ancho - 16 - 120, 36);
            probar.Size = new Size(120, 34);
            probar.Click += delegate { app.ProbarAhora(); Refrescar(); };
            tAct.Controls.Add(probar);

            // ---------- Modo
            Tarjeta tModo = Tarjeta_("Modo", izq, 220, ancho, 88);
            segModo = new Segmentado(new string[] { "Según horario", "Siempre", "Apagada" });
            segModo.Location = new Point(16, 38);
            segModo.Size = new Size(ancho - 32, 36);
            segModo.Seleccion = cfg.Modo == Modo.SiempreActivo ? 1 : (cfg.Modo == Modo.Desactivado ? 2 : 0);
            tModo.Controls.Add(segModo);

            // ---------- Sistema
            Tarjeta tSis = Tarjeta_("Sistema", izq, 320, ancho, 96);
            swSuspension = Fila(tSis, "No dejar que el PC se suspenda", 38, cfg.EvitarSuspension, ancho);
            swInicio = Fila(tSis, "Iniciar con Windows", 66, inicioActivo, ancho);

            // ---------- Horario
            Tarjeta tHor = Tarjeta_("Horario", der, 16, ancho, 140);
            string[] letras = { "L", "M", "X", "J", "V", "S", "D" };
            int paso = (ancho - 32 - 36) / 6;
            for (int i = 0; i < 7; i++)
            {
                Pastilla p = new Pastilla(letras[i]);
                p.Location = new Point(16 + i * paso, 38);
                p.Checked = cfg.Dias[(i + 1) % 7];
                tHor.Controls.Add(p);
                dias[i] = p;
            }
            Etiqueta(tHor, "De", 16, 98, Font, Tema.Suave);
            selDesde = SelectorHora(tHor, 44, 90, cfg.InicioMin);
            Etiqueta(tHor, "a", 196, 98, Font, Tema.Suave);
            selHasta = SelectorHora(tHor, ancho - 16 - 132, 90, cfg.FinMin);

            // ---------- Pulsación
            Tarjeta tPul = Tarjeta_("Pulsación", der, 168, ancho, 150);
            Etiqueta(tPul, "Tecla", 16, 46, Font, Tema.Texto);
            selTecla = new Selector();
            selTecla.Min = 13; selTecla.Max = 24; selTecla.Paso = 1; selTecla.Circular = true;
            selTecla.Formato = delegate(int v) { return "F" + v; };
            selTecla.Valor = (cfg.Tecla >= 13 && cfg.Tecla <= 24) ? cfg.Tecla : 15;
            selTecla.Location = new Point(ancho - 16 - 132, 38);
            tPul.Controls.Add(selTecla);

            Etiqueta(tPul, "Cada", 16, 86, Font, Tema.Texto);
            selIntervalo = new Selector();
            selIntervalo.Min = 10; selIntervalo.Max = 3600; selIntervalo.Paso = 5;
            selIntervalo.Formato = Intervalo;
            selIntervalo.Valor = cfg.IntervaloSeg;
            selIntervalo.Location = new Point(ancho - 16 - 132, 78);
            tPul.Controls.Add(selIntervalo);

            swInactivo = Fila(tPul, "Solo si no estoy tocando nada", 118, cfg.SoloSiInactivo, ancho);

            // ---------- Botones
            Boton guardar = new Boton("Guardar", true);
            guardar.Location = new Point(der + ancho - 110, 380);
            guardar.Click += delegate { Guardar(); };
            Boton cancelar = new Boton("Cancelar", false);
            cancelar.Location = new Point(der + ancho - 110 - 12 - 110, 380);
            cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(guardar);
            Controls.Add(cancelar);

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.KeyCode == Keys.Enter) Guardar();
            };

            ResumeLayout(false);
            PerformLayout();

            refresco = new System.Windows.Forms.Timer();
            refresco.Interval = 1000;
            refresco.Tick += delegate { Refrescar(); };
            refresco.Start();
            Refrescar();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.TituloOscuro(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refresco.Stop();
            base.OnFormClosed(e);
        }

        void Refrescar()
        {
            int estado = app.Estado;
            if (ojo.Estado != estado) { ojo.Estado = estado; ojo.Invalidate(); }
            lblEstado.Text = app.TextoEstado();
            lblEstado.ForeColor = estado == 0 ? Tema.Acento : (estado == 1 ? Tema.Ambar : Tema.Suave);

            DateTime u = app.UltimaPulsacion;
            if (u == DateTime.MinValue)
            {
                lblHora.Text = "--:--:--";
                lblHace.Text = "Todavía ninguna desde que arrancó";
            }
            else
            {
                lblHora.Text = u.ToString("HH:mm:ss");
                int n = app.Pulsaciones;
                lblHace.Text = Hace((DateTime.Now - u).TotalSeconds) + "  ·  " + n +
                               (n == 1 ? " pulsación" : " pulsaciones") + " desde el arranque";
            }
            lblInactivo.Text = "Windows te ve inactivo desde " + Hace(Nativo.SegundosInactivo());
        }

        void Guardar()
        {
            Config c = new Config();
            c.Modo = segModo.Seleccion == 1 ? Modo.SiempreActivo : (segModo.Seleccion == 2 ? Modo.Desactivado : Modo.Horario);
            for (int i = 0; i < 7; i++) c.Dias[(i + 1) % 7] = dias[i].Checked;
            c.InicioMin = selDesde.Valor;
            c.FinMin = selHasta.Valor;
            c.IntervaloSeg = selIntervalo.Valor;
            c.Tecla = selTecla.Valor;
            c.SoloSiInactivo = swInactivo.Checked;
            c.EvitarSuspension = swSuspension.Checked;
            Resultado = c;
            IniciarConWindows = swInicio.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        Tarjeta Tarjeta_(string titulo, int x, int y, int w, int h)
        {
            Tarjeta t = new Tarjeta(titulo);
            t.Location = new Point(x, y);
            t.Size = new Size(w, h);
            Controls.Add(t);
            return t;
        }

        static Label Etiqueta(Control padre, string texto, int x, int y, Font f, Color c)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            padre.Controls.Add(l);
            return l;
        }

        static Interruptor Fila(Control padre, string texto, int y, bool marcado, int ancho)
        {
            Interruptor sw = new Interruptor();
            sw.Location = new Point(ancho - 16 - 42, y);
            sw.Checked = marcado;
            padre.Controls.Add(sw);
            Label l = Etiqueta(padre, texto, 16, y + 2, padre.Font, Tema.Texto);
            l.Cursor = Cursors.Hand;
            l.Click += delegate { sw.Checked = !sw.Checked; };
            return sw;
        }

        static Selector SelectorHora(Control padre, int x, int y, int minutos)
        {
            Selector s = new Selector();
            s.Min = 0; s.Max = 24 * 60 - 15; s.Paso = 15; s.Circular = true;
            s.Formato = Hora;
            s.Valor = minutos;
            s.Location = new Point(x, y);
            padre.Controls.Add(s);
            return s;
        }
    }

    // ------------------------------------------------------------------ Aplicación de bandeja
    class App : ApplicationContext
    {
        Config cfg;
        NotifyIcon icono;
        System.Windows.Forms.Timer timer;
        Icon icoActivo, icoEspera, icoOff, icoVentana;
        int pulsaciones = 0;

        public DateTime UltimaPulsacion { get { return ultimaPulsacion; } }
        public int Pulsaciones { get { return pulsaciones; } }
        public int Estado { get { return estadoActual; } }

        public void ProbarAhora()
        {
            Nativo.PulsarTecla(cfg.Tecla);
            ultimaPulsacion = DateTime.Now;
            pulsaciones++;
        }
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

            icoActivo = Iconos.Crear(0, Color.Black);
            icoEspera = Iconos.Crear(1, Color.Black);
            icoOff = Iconos.Crear(2, Iconos.Gris);
            icoVentana = Iconos.Crear(0, Tema.Acento);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new RenderMenu();
            menu.Font = new Font("Segoe UI", 9.5F);
            miEstado = new ToolStripMenuItem("Cafetera"); miEstado.Enabled = false;
            miUltima = new ToolStripMenuItem("Última pulsación: -"); miUltima.Enabled = false;
            miHorario = new ToolStripMenuItem("Según horario", null, delegate { CambiarModo(Modo.Horario); });
            miSiempre = new ToolStripMenuItem("Siempre activa", null, delegate { CambiarModo(Modo.SiempreActivo); });
            miOff = new ToolStripMenuItem("Desactivada", null, delegate { CambiarModo(Modo.Desactivado); });
            ToolStripMenuItem miConfig = new ToolStripMenuItem("Configuración...", null, delegate { AbrirConfig(); });
            miConfig.Font = new Font("Segoe UI Semibold", 9.5F);
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
                pulsaciones++;
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

        public string TextoEstado()
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
                using (FormConfig f = new FormConfig(cfg.Clonar(), InicioWindows.Activo(), icoVentana, this))
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
