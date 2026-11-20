// Cafetera - mantiene Teams (y Windows) "despierto" pulsando una tecla invisible (F13-F24)
// Compatible con el compilador de .NET Framework 4 que trae Windows (C# 5).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        // accion > 0: código de tecla virtual (VK) · -1/-2/-3: clic izquierdo / derecho / central
        public static void Pulsar(int accion)
        {
            if (accion < 0)
            {
                uint abajo = 0x0002, arriba = 0x0004;                       // izquierdo
                if (accion == -2) { abajo = 0x0008; arriba = 0x0010; }      // derecho
                else if (accion == -3) { abajo = 0x0020; arriba = 0x0040; } // central
                mouse_event(abajo, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                mouse_event(arriba, 0, 0, 0, UIntPtr.Zero);
                return;
            }
            byte vk = (byte)accion;
            byte scan = (byte)MapVirtualKey(vk, 0);
            uint ext = EsExtendida(vk) ? 0x0001u : 0u;   // KEYEVENTF_EXTENDEDKEY
            keybd_event(vk, scan, ext, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(vk, scan, ext | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        static bool EsExtendida(int vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24:   // RePág, AvPág, Fin, Inicio
                case 0x25: case 0x26: case 0x27: case 0x28:   // flechas
                case 0x2D: case 0x2E:                         // Insert, Supr
                case 0x5B: case 0x5C: case 0x5D:              // Win, Win, Menú
                case 0x6F: case 0x90:                         // Num /, Bloq Num
                case 0xA3: case 0xA5:                         // Ctrl dcho, Alt Gr
                    return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ Configuración
    class Config
    {
        public Modo Modo = Modo.Horario;
        public Modo ModoAnterior = Modo.Horario;   // al que vuelve al reactivar con un clic
        // Índice = DayOfWeek (0 = domingo ... 6 = sábado)
        public bool[] Dias = new bool[] { false, true, true, true, true, true, false };
        public int InicioMin = 7 * 60;
        public int FinMin = 17 * 60;
        public int IntervaloSeg = 60;
        public int Accion = 0x10;   // Mayús (Shift). VK de la tecla, o -1/-2/-3 = clic izquierdo/derecho/central
        public bool SoloSiInactivo = true;
        public bool EvitarSuspension = false;

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

        static int LeerAccion(string v, int defecto)
        {
            string l = v.Trim().ToLowerInvariant();
            if (l.StartsWith("clic:"))
            {
                string b = l.Substring(5);
                return b.StartsWith("der") ? -2 : (b.StartsWith("cen") ? -3 : -1);
            }
            if (l.StartsWith("vk:"))
            {
                int vk = int.Parse(l.Substring(3));
                return (vk > 0 && vk < 256) ? vk : defecto;
            }
            int n = int.Parse(l.TrimStart('f'));
            return (n >= 1 && n <= 24) ? 0x6F + n : defecto;
        }

        static string EscribirAccion(int a)
        {
            if (a == -1) return "clic:izquierdo";
            if (a == -2) return "clic:derecho";
            if (a == -3) return "clic:central";
            if (a >= 0x70 && a <= 0x87) return "F" + (a - 0x6F);
            return "vk:" + a;
        }

        public static string NombreAccion(int a)
        {
            if (a == -1) return "Clic izquierdo";
            if (a == -2) return "Clic derecho";
            if (a == -3) return "Clic central";
            if (a >= 0x70 && a <= 0x87) return "F" + (a - 0x6F);
            if ((a >= 0x30 && a <= 0x39) || (a >= 0x41 && a <= 0x5A)) return ((char)a).ToString();
            if (a >= 0x60 && a <= 0x69) return "Num " + (a - 0x60);
            switch ((Keys)a)
            {
                case Keys.Space: return "Espacio";
                case Keys.Return: return "Intro";
                case Keys.Back: return "Retroceso";
                case Keys.Tab: return "Tab";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Mayús";
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt";
                case Keys.LWin: case Keys.RWin: return "Windows";
                case Keys.Capital: return "Bloq Mayús";
                case Keys.NumLock: return "Bloq Num";
                case Keys.Scroll: return "Bloq Despl";
                case Keys.Pause: return "Pausa";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Supr";
                case Keys.Home: return "Inicio";
                case Keys.End: return "Fin";
                case Keys.PageUp: return "Re Pág";
                case Keys.PageDown: return "Av Pág";
                case Keys.Up: return "Flecha arriba";
                case Keys.Down: return "Flecha abajo";
                case Keys.Left: return "Flecha izq.";
                case Keys.Right: return "Flecha dcha.";
                case Keys.Apps: return "Menú";
                case Keys.PrintScreen: return "Impr Pant";
            }
            return ((Keys)a).ToString();
        }

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
                        case "modoanterior":
                            c.ModoAnterior = (Modo)Enum.Parse(typeof(Modo), v, true);
                            if (c.ModoAnterior == Modo.Desactivado) c.ModoAnterior = Modo.Horario;
                            break;
                        case "dias": // orden L M X J V S D
                            for (int i = 0; i < 7 && i < v.Length; i++) c.Dias[(i + 1) % 7] = v[i] == '1';
                            break;
                        case "inicio": c.InicioMin = LeerHora(v, c.InicioMin); break;
                        case "fin": c.FinMin = LeerHora(v, c.FinMin); break;
                        case "intervalo": c.IntervaloSeg = Limitar(int.Parse(v), 5, 3600); break;
                        case "tecla": c.Accion = LeerAccion(v, c.Accion); break;
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
            sb.AppendLine("modoAnterior=" + ModoAnterior);
            sb.AppendLine("dias=" + dias);
            sb.AppendLine("inicio=" + Hora(InicioMin));
            sb.AppendLine("fin=" + Hora(FinMin));
            sb.AppendLine("intervalo=" + IntervaloSeg);
            sb.AppendLine("tecla=" + EscribirAccion(Accion));
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

        public static Icon Crear(int estado, Color c) { return Crear(estado, c, false); }

        public static Icon Crear(int estado, Color c, bool punto)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap bmp = new Bitmap(s, s))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Pintar(g, 0, 0, s, estado, c);
                    if (punto)
                    {
                        // Puntito abajo a la derecha (modo "Según horario")
                        float u = s / 16f, r = 1.7f * u;
                        using (SolidBrush b = new SolidBrush(c))
                            g.FillEllipse(b, 14.0f * u - r, 13.9f * u - r, 2 * r, 2 * r);
                    }
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
        public static readonly Color Acento = Color.FromArgb(0xEE, 0xD7, 0xFC);       // #EED7FC
        public static readonly Color AcentoHover = Color.FromArgb(0xF6, 0xEA, 0xFE);
        public static readonly Color AcentoTexto = Color.FromArgb(0x2E, 0x14, 0x40);
        public static readonly Color Ambar = Color.FromArgb(0xF2, 0xB8, 0x3A);
        public static readonly Color Rojo = Color.FromArgb(0xD9, 0x3F, 0x3F);
        public static readonly Color RojoHover = Color.FromArgb(0xE8, 0x5A, 0x5A);

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

    // ‹ valor ›  — clic en las flechas (mantener pulsado repite), rueda del ratón o cursores.
    // Opcional: clic en el centro para escribir el valor (EditableTexto) o para capturar
    // la siguiente tecla / botón del ratón (Capturable).
    class Selector : ControlBase
    {
        int valor;
        public int Min, Max, Paso = 1, ValorDefecto;
        public bool Circular, EditableTexto, Capturable;
        public Func<int, string> Formato = delegate(int v) { return v.ToString(); };
        public Func<int, string> TextoEdicion;
        public Func<string, int> Parsear;        // int.MinValue = no válido
        public event EventHandler Cambiado;
        int zonaHover, dir, repeticiones;
        bool capturando, pendienteCaptura;
        TextBox editor;
        System.Windows.Forms.Timer repetir;

        public bool Capturando { get { return capturando; } }
        public bool Editando { get { return editor != null; } }

        public int Valor
        {
            get { return valor; }
            set
            {
                bool cambia = valor != value;
                valor = value;
                Invalidate();
                if (cambia && Cambiado != null) Cambiado(this, EventArgs.Empty);
            }
        }

        // ---- Escribir el valor
        void EmpezarEdicion()
        {
            if (editor != null) return;
            editor = new TextBox();
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = Tema.CajaHover;
            editor.ForeColor = Tema.Texto;
            editor.Font = new Font(Font, FontStyle.Bold);
            editor.TextAlign = HorizontalAlignment.Center;
            editor.Text = TextoEdicion != null ? TextoEdicion(valor) : valor.ToString();
            int h = editor.PreferredHeight;
            editor.SetBounds(Height, (Height - h) / 2, Width - 2 * Height, h);
            editor.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TerminarEdicion(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; TerminarEdicion(false); }
            };
            editor.LostFocus += delegate { TerminarEdicion(true); };
            Controls.Add(editor);
            Invalidate();
            editor.Focus();
            editor.SelectAll();
        }

        public void CerrarEdicion() { TerminarEdicion(true); }

        void TerminarEdicion(bool aplicar)
        {
            if (editor == null) return;
            TextBox ed = editor;
            editor = null;
            if (aplicar && Parsear != null)
            {
                int v = Parsear(ed.Text);
                if (v != int.MinValue) Valor = Math.Max(Min, Math.Min(Max, v));
            }
            Controls.Remove(ed);
            ed.Dispose();
            Invalidate();
        }

        // ---- Capturar tecla o botón
        void TerminarCaptura(int nuevo)
        {
            capturando = false;
            Capture = false;
            if (nuevo != int.MinValue) Valor = nuevo;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (capturando && !Capture) { capturando = false; Invalidate(); }
            base.OnMouseCaptureChanged(e);
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
            if (Capturable && (valor < Min || valor > Max)) { Valor = ValorDefecto; return; }   // de tecla/clic propio a F13-F24
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
            if (capturando)
            {
                int b = e.Button == MouseButtons.Left ? -1 : (e.Button == MouseButtons.Right ? -2 :
                        (e.Button == MouseButtons.Middle ? -3 : int.MinValue));
                TerminarCaptura(b);
                return;
            }
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                dir = Zona(e.X);
                if (dir == 0)
                {
                    if (EditableTexto) EmpezarEdicion();
                    else if (Capturable) pendienteCaptura = true;
                }
                else
                {
                    Mover(dir);
                    repeticiones = 0;
                    repetir.Interval = 400;
                    repetir.Start();
                }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            repetir.Stop();
            base.OnMouseUp(e);
            if (pendienteCaptura)
            {
                pendienteCaptura = false;
                BeginInvoke((MethodInvoker)delegate
                {
                    capturando = true;
                    Focus();
                    Capture = true;   // el siguiente clic (en cualquier parte de la ventana) llega aquí
                    Invalidate();
                });
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (capturando || editor != null) return;
            Mover(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }
        protected override bool IsInputKey(Keys k)
        {
            if (capturando) return true;
            return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (capturando)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                TerminarCaptura(e.KeyCode == Keys.Escape ? int.MinValue : (int)e.KeyCode);
                return;
            }
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) { Mover(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) { Mover(-1); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e)
        {
            if (capturando && !ContainsFocus) TerminarCaptura(int.MinValue);
            Invalidate();
            base.OnLostFocus(e);
        }

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
                Color cb = capturando ? Tema.Ambar : ((Focused || editor != null) ? Tema.Acento : Tema.Caja);
                using (Pen borde = new Pen(cb, capturando ? 1.6f : 1f)) g.DrawPath(borde, p);
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
            if (editor != null) return;
            using (Font negrita = new Font(Font, FontStyle.Bold))
                Tema.Texto_(g, capturando ? "Pulsa…" : Formato(valor), negrita,
                            new Rectangle(Height, 0, Width - 2 * Height, Height),
                            capturando ? Tema.Ambar : Tema.Texto,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    class Boton : ControlBase
    {
        public bool Primario, Peligro;
        public Boton(string texto, bool primario) { Text = texto; Primario = primario; Size = new Size(110, 36); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Tema.Suavizar(g);
            Color fondo = Primario ? (encima ? Tema.AcentoHover : Tema.Acento) : (encima ? Tema.CajaHover : Tema.Caja);
            if (Peligro) fondo = encima ? Tema.RojoHover : Tema.Rojo;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Tema.Redondo(r, r.Height * 0.3f))
            using (SolidBrush b = new SolidBrush(fondo))
                g.FillPath(b, p);
            using (Font negrita = new Font(Font, FontStyle.Bold))
                Tema.Texto_(g, Text, negrita, ClientRectangle,
                            Peligro ? Color.White : (Primario ? Tema.AcentoTexto : Tema.Texto),
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

    // ------------------------------------------------------------------ Modal oscuro: confirmar cierre
    class ConfirmarCierre : Form
    {
        public static bool Preguntar(Form padre, Icon icono)
        {
            using (ConfirmarCierre f = new ConfirmarCierre(icono))
                return f.ShowDialog(padre) == DialogResult.OK;
        }

        ConfirmarCierre(Icon icono)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.75F);
            Text = "Cerrar Cafetera";
            Icon = icono;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            ClientSize = new Size(420, 196);

            Label titulo = new Label();
            titulo.Text = "¿Cerrar Cafetera del todo?";
            titulo.Font = new Font("Segoe UI Semibold", 12.5F);
            titulo.ForeColor = Tema.Texto;
            titulo.AutoSize = true;
            titulo.Location = new Point(20, 18);
            Controls.Add(titulo);

            Label texto = new Label();
            texto.Text = "La app se cerrará también en segundo plano: dejará de pulsar y " +
                         "desaparecerá el icono de la barra de tareas.\nSi solo quieres pausarla, " +
                         "usa el modo \"Apagada\" o haz un clic en el icono.";
            texto.ForeColor = Tema.Suave;
            texto.Location = new Point(20, 52);
            texto.Size = new Size(380, 80);
            Controls.Add(texto);

            Boton cerrar = new Boton("Cerrar app", false);
            cerrar.Peligro = true;
            cerrar.Location = new Point(420 - 20 - 110, 142);
            cerrar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Boton cancelar = new Boton("Cancelar", false);
            cancelar.Location = new Point(420 - 20 - 110 - 12 - 110, 142);
            cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cerrar);
            Controls.Add(cancelar);

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };

            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.TituloOscuro(this);
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
        public bool SalirDeLaApp;
        Label lblTeclaAyuda;

        static string Hora(int min) { return (min / 60).ToString("00") + ":" + (min % 60).ToString("00"); }

        static string Intervalo(int s)
        {
            if (s < 60) return s + " s";
            if (s % 60 == 0) return (s / 60) + " min";
            return (s / 60) + " min " + (s % 60) + " s";
        }

        static string MinSeg(int s) { return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

        // Acepta "1:30", "90", "90s", "2m", "2min", "2m30s", "2 min 30 s"
        static int LeerIntervalo(string t)
        {
            t = t.Trim().ToLowerInvariant().Replace(" ", "");
            int m, s;
            string[] p = t.Split(':');
            if (p.Length == 2 && int.TryParse(p[0], out m) && int.TryParse(p[1], out s)) return m * 60 + s;
            Match r = Regex.Match(t, @"^(?:(\d+)m(?:in)?)?(?:(\d+)s?)?$");
            if (r.Success && (r.Groups[1].Success || r.Groups[2].Success))
            {
                m = r.Groups[1].Success ? int.Parse(r.Groups[1].Value) : 0;
                s = r.Groups[2].Success ? int.Parse(r.Groups[2].Value) : 0;
                return m * 60 + s;
            }
            return int.MinValue;
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
            Tarjeta tPul = Tarjeta_("Pulsación", der, 168, ancho, 200);
            Font chica = new Font("Segoe UI", 8F);
            Etiqueta(tPul, "Tecla", 16, 46, Font, Tema.Texto);
            selTecla = new Selector();
            selTecla.Min = 0x7C; selTecla.Max = 0x87; selTecla.Paso = 1; selTecla.Circular = true;  // flechas: F13-F24
            selTecla.ValorDefecto = 0x7E;
            selTecla.Capturable = true;
            selTecla.Formato = Config.NombreAccion;
            selTecla.Valor = cfg.Accion;
            selTecla.Size = new Size(180, 34);
            selTecla.Location = new Point(ancho - 16 - 180, 38);
            tPul.Controls.Add(selTecla);
            lblTeclaAyuda = Etiqueta(tPul, "", 16, 76, chica, Tema.Suave);
            selTecla.Cambiado += delegate { AyudaTecla(); };
            AyudaTecla();

            Etiqueta(tPul, "Cada", 16, 108, Font, Tema.Texto);
            selIntervalo = new Selector();
            selIntervalo.Min = 5; selIntervalo.Max = 3600; selIntervalo.Paso = 5;
            selIntervalo.Formato = Intervalo;
            selIntervalo.EditableTexto = true;
            selIntervalo.TextoEdicion = MinSeg;
            selIntervalo.Parsear = LeerIntervalo;
            selIntervalo.Valor = cfg.IntervaloSeg;
            selIntervalo.Size = new Size(180, 34);
            selIntervalo.Location = new Point(ancho - 16 - 180, 100);
            tPul.Controls.Add(selIntervalo);
            Etiqueta(tPul, "Clic en el centro para escribirlo (mm:ss)", 16, 138, chica, Tema.Suave);

            swInactivo = Fila(tPul, "Solo si no estoy tocando nada", 164, cfg.SoloSiInactivo, ancho);

            // ---------- Botones
            Boton guardar = new Boton("Guardar", true);
            guardar.Location = new Point(der + ancho - 110, 380);
            guardar.Click += delegate { Guardar(); };
            Boton cancelar = new Boton("Cancelar", false);
            cancelar.Location = new Point(der + ancho - 110 - 12 - 110, 380);
            cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(guardar);
            Controls.Add(cancelar);

            Boton salir = new Boton("Cerrar app", false);
            salir.Peligro = true;
            salir.Location = new Point(der, 380);
            salir.Click += delegate
            {
                if (!ConfirmarCierre.Preguntar(this, icono)) return;
                SalirDeLaApp = true;
                DialogResult = DialogResult.Abort;
                Close();
            };
            Controls.Add(salir);

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (selTecla.Capturando || selIntervalo.Editando) return;
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

        void AyudaTecla()
        {
            if (selTecla.Valor < 0)
            {
                lblTeclaAyuda.Text = "Ojo: hará clic donde esté el ratón";
                lblTeclaAyuda.ForeColor = Tema.Ambar;
            }
            else
            {
                lblTeclaAyuda.Text = "Clic en el centro y pulsa una tecla o botón del ratón";
                lblTeclaAyuda.ForeColor = Tema.Suave;
            }
        }

        void Guardar()
        {
            selIntervalo.CerrarEdicion();
            Config c = new Config();
            c.Modo = segModo.Seleccion == 1 ? Modo.SiempreActivo : (segModo.Seleccion == 2 ? Modo.Desactivado : Modo.Horario);
            for (int i = 0; i < 7; i++) c.Dias[(i + 1) % 7] = dias[i].Checked;
            c.InicioMin = selDesde.Valor;
            c.FinMin = selHasta.Valor;
            c.IntervaloSeg = selIntervalo.Valor;
            c.Accion = selTecla.Valor;
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
        Icon icoActivo, icoPunto, icoOff, icoVentana;
        int pulsaciones = 0;

        public DateTime UltimaPulsacion { get { return ultimaPulsacion; } }
        public int Pulsaciones { get { return pulsaciones; } }
        public int Estado { get { return estadoActual; } }

        public void ProbarAhora()
        {
            Nativo.Pulsar(cfg.Accion);
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

            // 3 iconos según el modo: Siempre = ojo · Según horario = ojo con puntito · Apagada = ojo cerrado
            icoActivo = Iconos.Crear(0, Color.Black);
            icoPunto = Iconos.Crear(0, Color.Black, true);
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
            icono.Icon = icoPunto;
            icono.Text = "Cafetera";
            icono.ContextMenuStrip = menu;
            icono.Visible = true;
            // 1 clic = apagar / volver al modo anterior · 2 clics = configuración
            timerClics = new System.Windows.Forms.Timer();
            timerClics.Interval = Math.Max(250, SystemInformation.DoubleClickTime);
            timerClics.Tick += delegate { timerClics.Stop(); ProcesarClics(); };
            icono.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                clics++;
                timerClics.Stop();
                if (clics >= 2) { ProcesarClics(); return; }
                timerClics.Start();
            };

            if (nuevo)
            {
                cfg.Guardar();
                InicioWindows.Establecer(true);
                icono.ShowBalloonTip(6000, "Cafetera",
                    "Estoy aquí, junto al reloj. Activa " + cfg.ResumenHorario() +
                    ", pulsando " + Config.NombreAccion(cfg.Accion) + " cada " + cfg.IntervaloSeg + " s.\nClic derecho para configurar.",
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
                Nativo.Pulsar(cfg.Accion);
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
                return "Activa: " + Config.NombreAccion(cfg.Accion) + " cada " + cfg.IntervaloSeg + " s" +
                       (cfg.Modo == Modo.SiempreActivo ? " (siempre)" : "");
            if (estadoActual == 1) return "En espera (" + cfg.ResumenHorario() + ")";
            return "Desactivada";
        }

        void ActualizarIcono()
        {
            if (cfg.Modo == Modo.Horario) icono.Icon = icoPunto;
            else icono.Icon = cfg.Modo == Modo.SiempreActivo ? icoActivo : icoOff;
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
            if (n == 1) Alternar();
            else if (n >= 2) AbrirConfig();
        }

        // Apagada <-> el modo que tuviera antes (Según horario o Siempre)
        void Alternar()
        {
            CambiarModo(cfg.Modo == Modo.Desactivado ? cfg.ModoAnterior : Modo.Desactivado);
        }

        void CambiarModo(Modo m)
        {
            if (m != Modo.Desactivado) cfg.ModoAnterior = m;
            else if (cfg.Modo != Modo.Desactivado) cfg.ModoAnterior = cfg.Modo;
            cfg.Modo = m;
            cfg.Guardar();
            estadoActual = -1;
            Comprobar();
        }

        void AbrirConfig()
        {
            if (configAbierta) return;
            configAbierta = true;
            bool salir = false;
            try
            {
                using (FormConfig f = new FormConfig(cfg.Clonar(), InicioWindows.Activo(), icoVentana, this))
                {
                    f.ShowDialog();
                    if (f.SalirDeLaApp) salir = true;
                    else if (f.DialogResult == DialogResult.OK && f.Resultado != null)
                    {
                        Config nueva = f.Resultado;
                        if (nueva.Modo != Modo.Desactivado) nueva.ModoAnterior = nueva.Modo;
                        else nueva.ModoAnterior = cfg.Modo != Modo.Desactivado ? cfg.Modo : cfg.ModoAnterior;
                        cfg = nueva;
                        cfg.Guardar();
                        InicioWindows.Establecer(f.IniciarConWindows);
                        estadoActual = -1;
                        Comprobar();
                    }
                }
            }
            finally { configAbierta = false; }
            if (salir) Salir();
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
