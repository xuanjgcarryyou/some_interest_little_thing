// 麻糬 — a tiny desktop pet. Built with the C# 5 compiler that ships with .NET Framework 4.x.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopPet
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "DesktopPet_Mochi_SingleInstance", out created))
            {
                if (!created) return;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PetForm());
            }
        }
    }

    enum PetState { Idle, Walk, Sleep, Drag, Fall, Happy, Eat }

    enum ParticleKind { Heart, Zzz, Crumb, Sweat, Dust }

    class Particle
    {
        public ParticleKind Kind;
        public float X, Y, VX, VY, Size;
        public int Life, MaxLife;
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        public const int WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080,
                         WS_EX_TOPMOST = 0x00000008, WS_EX_NOACTIVATE = 0x08000000;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
    }

    // A borderless per-pixel-alpha window: fully transparent pixels are click-through,
    // so only the pet itself (and its speech bubble) catches the mouse.
    class PetForm : Form
    {
        const int Fps = 30;

        static readonly Color OutlineColor = Color.FromArgb(74, 60, 72);
        static readonly Color BodyTop = Color.FromArgb(255, 252, 250);
        static readonly Color BodyBottom = Color.FromArgb(255, 226, 233);
        static readonly Color EarInner = Color.FromArgb(255, 182, 199);
        static readonly Color CheekColor = Color.FromArgb(130, 255, 140, 170);
        static readonly Color MouthColor = Color.FromArgb(200, 70, 90);

        static readonly string[] TalkLines = { "今天也要加油喔！", "喵～", "陪我玩嘛～", "你在寫什麼呀？", "記得喝水喔～", "眼睛休息一下吧", "嘿嘿", "好無聊喔…", "要不要摸摸我？" };
        static readonly string[] ClickLines = { "嘿嘿～", "好癢！", "喜歡你～", "再摸一下嘛！", "喵嗚～" };
        static readonly string[] DragLines = { "哇啊啊～放我下來！", "好高喔！", "要去哪裡呀？" };
        static readonly string[] HungryLines = { "肚子餓了…", "想吃飯糰…", "咕嚕咕嚕…" };

        readonly float S;                 // DPI scale
        readonly int W, H, FootMargin;    // window size; pet's feet sit FootMargin above the window bottom
        readonly Bitmap canvas;
        readonly FontFamily fontFamily;
        readonly Font bubbleFont;
        readonly Random rnd = new Random();
        readonly List<Particle> particles = new List<Particle>();
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly string statePath;
        ToolStripMenuItem hungerItem, sleepItem, followItem;

        // World position = screen coords of the pet's foot center.
        float cx, footY, vx, vy;
        int dir = 1;
        PetState state;
        int stateTimer, tick, idleFrames;
        int blinkTimer = 90;
        float squash = 1f;
        float hunger = 80f;
        bool followMouse;
        string bubbleText;
        int bubbleFrames;

        bool mouseDown, dragMoved;
        Point downPos;
        float dragOffX, dragOffY;

        public PetForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            W = (int)(230 * S);
            H = (int)(210 * S);
            FootMargin = (int)(8 * S);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Text = "麻糬";
            Size = new Size(W, H);

            canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            fontFamily = PickFontFamily();
            bubbleFont = new Font(fontFamily, 14f * S, FontStyle.Regular, GraphicsUnit.Pixel);

            statePath = Path.Combine(Application.StartupPath, "mochi-state.txt");
            LoadState();

            ContextMenuStrip menu = BuildMenu();
            ContextMenuStrip = menu;
            tray = new NotifyIcon();
            tray.Text = "麻糬";
            tray.Icon = MakeTrayIcon();
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { BringHome(); };

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            cx = wa.Left + wa.Width * 0.75f;
            footY = wa.Top + 120 * S;
            SetState(PetState.Fall, 0);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000 / Fps;
            timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Say(hunger < 30 ? "肚子好餓…" : "嗨！我是麻糬～", Fps * 3);
            Render(WorkArea().Bottom);
            timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer.Stop();
            SaveState();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }

        // ---------- input ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            mouseDown = true;
            dragMoved = false;
            downPos = Cursor.Position;
            dragOffX = downPos.X - cx;
            dragOffY = downPos.Y - footY;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && mouseDown) ReleaseMouse();
        }

        void ReleaseMouse()
        {
            mouseDown = false;
            idleFrames = 0;
            if (dragMoved)
            {
                vx = Clamp(vx, -30 * S, 30 * S);
                vy = Clamp(vy, -30 * S, 30 * S);
                SetState(PetState.Fall, 0);
            }
            else Pet();
        }

        // ---------- simulation ----------

        void OnTick(object sender, EventArgs e)
        {
            tick++;
            idleFrames++;
            hunger = Math.Max(0f, hunger - (state == PetState.Sleep ? 1f / (Fps * 60) : 1f / (Fps * 20)));

            Rectangle wa = WorkArea();
            float ground = wa.Bottom;
            float half = 52 * S;

            // Safety net in case the button-up happened somewhere we never heard about.
            if (mouseDown && (Control.MouseButtons & MouseButtons.Left) == 0) ReleaseMouse();
            if (mouseDown) UpdateDrag(ground);

            switch (state)
            {
                case PetState.Idle: UpdateIdle(); break;
                case PetState.Walk: UpdateWalk(wa, half); break;
                case PetState.Sleep: UpdateSleep(); break;
                case PetState.Drag:
                    if (tick % 12 == 0) Spawn(ParticleKind.Sweat, (rnd.Next(2) == 0 ? -1 : 1) * 36 * S, -80 * S);
                    break;
                case PetState.Fall: UpdateFall(wa, ground, half); break;
                case PetState.Happy: UpdateHappy(); break;
                case PetState.Eat: UpdateEat(); break;
            }

            if (state != PetState.Drag && state != PetState.Fall)
            {
                if (footY < ground - 1) { vx = vy = 0; SetState(PetState.Fall, 0); } // e.g. the taskbar moved
                else footY = ground;
                cx = Clamp(cx, wa.Left + half, wa.Right - half);
            }

            squash += (1f - squash) * 0.15f;
            if (--blinkTimer < 0) blinkTimer = rnd.Next(60, 150);
            if (bubbleFrames > 0) bubbleFrames--;
            UpdateParticles();
            Render(ground);
        }

        void UpdateDrag(float ground)
        {
            Point p = Cursor.Position;
            if (!dragMoved)
            {
                if (Math.Abs(p.X - downPos.X) <= 4 * S && Math.Abs(p.Y - downPos.Y) <= 4 * S) return;
                dragMoved = true;
                SetState(PetState.Drag, 0);
                Say(Pick(DragLines), Fps * 2);
            }
            float nx = p.X - dragOffX, ny = Math.Min(p.Y - dragOffY, ground);
            vx = vx * 0.5f + (nx - cx) * 0.5f;
            vy = vy * 0.5f + (ny - footY) * 0.5f;
            cx = nx;
            footY = ny;
        }

        void UpdateIdle()
        {
            if (followMouse)
            {
                float d = Cursor.Position.X - cx;
                if (Math.Abs(d) > 60 * S) { dir = d > 0 ? 1 : -1; SetState(PetState.Walk, Fps); }
                return;
            }
            if (--stateTimer > 0) return;
            if (idleFrames > Fps * 90) { GoToSleep(); return; }

            int r = rnd.Next(100);
            if (r < 45)
            {
                dir = rnd.Next(2) == 0 ? -1 : 1;
                SetState(PetState.Walk, rnd.Next(Fps * 2, Fps * 6));
            }
            else
            {
                if (r < 58) Say(hunger < 30 ? Pick(HungryLines) : Pick(TalkLines), Fps * 3);
                SetState(PetState.Idle, rnd.Next(Fps * 2, Fps * 6));
            }
        }

        void UpdateWalk(Rectangle wa, float half)
        {
            float speed = (hunger < 20 ? 1f : 2f) * S;
            if (followMouse)
            {
                float d = Cursor.Position.X - cx;
                if (Math.Abs(d) < 30 * S) { SetState(PetState.Idle, Fps); return; }
                dir = d > 0 ? 1 : -1;
                speed = Math.Min(Math.Abs(d), 4f * S);
                stateTimer = Fps;
            }
            cx += dir * speed;
            if (cx <= wa.Left + half) dir = 1;
            else if (cx >= wa.Right - half) dir = -1;
            if (--stateTimer <= 0) SetState(PetState.Idle, rnd.Next(Fps * 2, Fps * 5));
        }

        void UpdateSleep()
        {
            if (tick % 40 == 0) Spawn(ParticleKind.Zzz, 30 * S, -78 * S);
            if (--stateTimer <= 0) WakeUp("嗯…睡飽了～");
        }

        void UpdateFall(Rectangle wa, float ground, float half)
        {
            vy = Math.Min(vy + 0.9f * S, 40 * S);
            cx += vx;
            footY += vy;
            vx *= 0.99f;
            if (cx < wa.Left + half) { cx = wa.Left + half; vx = Math.Abs(vx) * 0.6f; }
            else if (cx > wa.Right - half) { cx = wa.Right - half; vx = -Math.Abs(vx) * 0.6f; }
            if (footY < ground) return;

            footY = ground;
            if (vy > 7 * S)
            {
                squash = 1f + Math.Min(vy / (50 * S), 0.45f);
                for (int i = 0; i < 4; i++) Spawn(ParticleKind.Dust, (i < 2 ? -1 : 1) * 40 * S, -4 * S);
                if (vy > 22 * S) Say("痛痛…", Fps * 2);
                vy = -vy * 0.35f;
                vx *= 0.7f;
            }
            else
            {
                vx = vy = 0;
                squash = Math.Max(squash, 1.12f);
                SetState(PetState.Idle, Fps);
            }
        }

        void UpdateHappy()
        {
            if (stateTimer % 9 == 0) Spawn(ParticleKind.Heart, (float)(rnd.NextDouble() - 0.5) * 60 * S, -70 * S);
            if (--stateTimer <= 0) SetState(PetState.Idle, Fps * 2);
        }

        void UpdateEat()
        {
            if (stateTimer % 15 == 0) Spawn(ParticleKind.Crumb, dir * 34 * S, -30 * S);
            if (--stateTimer > 0) return;
            hunger = Math.Min(100f, hunger + 35f);
            Say("好好吃！", Fps * 2);
            SetState(PetState.Happy, 45);
        }

        // ---------- actions ----------

        void Pet()
        {
            idleFrames = 0;
            if (state == PetState.Sleep) { WakeUp("嗯…早安…"); return; }
            if (state == PetState.Fall || state == PetState.Eat) return;
            Say(Pick(ClickLines), Fps * 2);
            SetState(PetState.Happy, 45);
        }

        void Feed()
        {
            idleFrames = 0;
            if (state == PetState.Drag || state == PetState.Fall) return;
            if (hunger > 90) { Say("吃不下了啦～", Fps * 2); return; }
            SetState(PetState.Eat, 90);
            Say("嚼嚼嚼…", 90);
        }

        void GoToSleep()
        {
            if (state == PetState.Drag || state == PetState.Fall) return;
            SetState(PetState.Sleep, rnd.Next(Fps * 120, Fps * 240));
            Say("好睏…", Fps * 2);
        }

        void WakeUp(string line)
        {
            idleFrames = 0;
            SetState(PetState.Idle, Fps * 2);
            Say(line, Fps * 2);
        }

        void BringHome()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            cx = wa.Left + wa.Width / 2f;
            footY = wa.Top + 100 * S;
            vx = vy = 0;
            SetState(PetState.Fall, 0);
            Say("我回來了！", Fps * 2);
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            hungerItem = new ToolStripMenuItem();
            hungerItem.Enabled = false;
            m.Items.Add(hungerItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("餵食飯糰", null, delegate { Feed(); });
            m.Items.Add("摸摸頭", null, delegate { Pet(); });
            followItem = new ToolStripMenuItem("跟著滑鼠走", null, delegate
            {
                followMouse = !followMouse;
                idleFrames = 0;
                if (followMouse && state == PetState.Sleep) WakeUp("好～我跟著你！");
            });
            m.Items.Add(followItem);
            sleepItem = new ToolStripMenuItem("去睡覺", null, delegate
            {
                if (state == PetState.Sleep) WakeUp("嗯…早安…"); else GoToSleep();
            });
            m.Items.Add(sleepItem);
            m.Items.Add("說句話", null, delegate
            {
                idleFrames = 0;
                Say(hunger < 30 ? Pick(HungryLines) : Pick(TalkLines), Fps * 3);
            });
            m.Items.Add("叫回螢幕中間", null, delegate { BringHome(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("掰掰（結束）", null, delegate { Close(); });
            m.Opening += delegate
            {
                hungerItem.Text = string.Format("飽足度：{0}%", (int)Math.Round(hunger));
                followItem.Checked = followMouse;
                sleepItem.Text = state == PetState.Sleep ? "叫醒麻糬" : "去睡覺";
            };
            return m;
        }

        // ---------- particles ----------

        void Spawn(ParticleKind kind, float x, float y)
        {
            Particle p = new Particle();
            p.Kind = kind;
            p.X = x;
            p.Y = y;
            float r = (float)rnd.NextDouble();
            switch (kind)
            {
                case ParticleKind.Heart: p.VX = (r - 0.5f) * 1.2f * S; p.VY = -1.4f * S; p.Size = (10 + r * 6) * S; p.MaxLife = 40; break;
                case ParticleKind.Zzz: p.VX = 0.5f * S; p.VY = -0.8f * S; p.MaxLife = 70; break;
                case ParticleKind.Crumb: p.VX = (r - 0.5f) * 3f * S; p.VY = -2f * S; p.Size = (2 + r * 2) * S; p.MaxLife = 25; break;
                case ParticleKind.Sweat: p.VX = Math.Sign(x) * S; p.VY = -1f * S; p.Size = 6 * S; p.MaxLife = 20; break;
                case ParticleKind.Dust: p.VX = Math.Sign(x) * (1f + r) * S; p.VY = -0.5f * S; p.Size = (6 + r * 4) * S; p.MaxLife = 20; break;
            }
            p.Life = p.MaxLife;
            particles.Add(p);
        }

        void UpdateParticles()
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                p.X += p.VX;
                p.Y += p.VY;
                if (p.Kind == ParticleKind.Crumb || p.Kind == ParticleKind.Sweat) p.VY += 0.3f * S;
                if (p.Kind == ParticleKind.Crumb && p.Y > 0) { p.Y = 0; p.VX = p.VY = 0; }
                if (--p.Life <= 0) particles.RemoveAt(i);
            }
        }

        // ---------- rendering ----------

        void Render(float ground)
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                float ox = W / 2f, oy = H - FootMargin;
                float hop = state == PetState.Happy ? -(float)Math.Abs(Math.Sin(stateTimer * 0.21)) * 14 * S : 0f;
                if (footY >= ground - 1) DrawShadow(g, ox, oy, hop);
                DrawPet(g, ox, oy + hop);
                DrawParticles(g, ox, oy);
                DrawBubble(g, ox, oy + hop - 104 * S);
            }
            UpdateLayered((int)Math.Round(cx - W / 2f), (int)Math.Round(footY - (H - FootMargin)));
        }

        void UpdateLayered(int x, int y)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = canvas.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBmp = Native.SelectObject(memDc, hBmp);
            try
            {
                Native.POINT dst = new Native.POINT(x, y), src = new Native.POINT(0, 0);
                Native.SIZE size = new Native.SIZE(W, H);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, oldBmp);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        void DrawShadow(Graphics g, float x, float y, float hop)
        {
            float k = 1f + hop / (50 * S); // hop is negative, so the shadow shrinks mid-jump
            float w = 80 * S * k * squash, h = 9 * S * k;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, x - w / 2, y - h / 2, w, h);
        }

        void DrawPet(Graphics g, float x, float y)
        {
            GraphicsState saved = g.Save();
            float sq = squash * (1f + 0.025f * (float)Math.Sin(tick * 0.08));
            if (state == PetState.Walk) sq = squash * (1f + 0.05f * (float)Math.Sin(tick * 0.45));
            if (state == PetState.Drag) sq = 0.86f;
            float bw = 96 * S * sq, bh = 76 * S / sq;
            float top = y - 4 * S - bh, left = x - bw / 2;

            if (state == PetState.Drag)
            {
                // Dangle from the top of the head.
                g.TranslateTransform(x, top);
                g.RotateTransform((float)Math.Sin(tick * 0.25) * 10f);
                g.TranslateTransform(-x, -top);
            }

            using (Pen outline = new Pen(OutlineColor, 2.5f * S))
            {
                outline.LineJoin = LineJoin.Round;
                DrawFeet(g, outline, x, y, bw);
                DrawEars(g, outline, x, top, bw, bh);
                using (GraphicsPath body = BodyPath(left, top, bw, bh))
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(left, top - 1, bw, bh + 2), BodyTop, BodyBottom, 90f))
                {
                    g.FillPath(fill, body);
                    g.DrawPath(outline, body);
                }
                DrawFace(g, outline, x, top, bw, bh);
                if (state == PetState.Eat) DrawFood(g, x + dir * bw * 0.52f, top + bh * 0.62f);
            }
            g.Restore(saved);
        }

        void DrawFeet(Graphics g, Pen outline, float x, float y, float bw)
        {
            float step = 0f;
            if (state == PetState.Walk) step = (float)Math.Sin(tick * 0.45) * 3 * S;
            if (state == PetState.Drag) step = (float)Math.Sin(tick * 0.6) * 4 * S;
            float fw = 22 * S, fh = 11 * S;
            using (SolidBrush b = new SolidBrush(BodyBottom))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float fx = x + i * bw * 0.24f - fw / 2;
                    float fy = y - fh - S - (i < 0 ? Math.Max(step, 0) : Math.Max(-step, 0));
                    g.FillEllipse(b, fx, fy, fw, fh);
                    g.DrawEllipse(outline, fx, fy, fw, fh);
                }
            }
        }

        void DrawEars(Graphics g, Pen outline, float x, float top, float bw, float bh)
        {
            float wiggle = state == PetState.Happy ? (float)Math.Sin(tick * 0.5) * 3 * S : 0f;
            using (SolidBrush fill = new SolidBrush(BodyTop))
            using (SolidBrush inner = new SolidBrush(EarInner))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    PointF[] ear =
                    {
                        new PointF(x + i * bw * 0.40f, top + bh * 0.30f),
                        new PointF(x + i * bw * 0.33f + i * wiggle, top - bh * 0.14f),
                        new PointF(x + i * bw * 0.10f, top + bh * 0.06f)
                    };
                    g.FillPolygon(fill, ear);
                    g.DrawPolygon(outline, ear);

                    float mx = (ear[0].X + ear[1].X + ear[2].X) / 3, my = (ear[0].Y + ear[1].Y + ear[2].Y) / 3;
                    PointF[] innerEar = new PointF[3];
                    for (int k = 0; k < 3; k++) innerEar[k] = new PointF(mx + (ear[k].X - mx) * 0.5f, my + (ear[k].Y - my) * 0.5f);
                    g.FillPolygon(inner, innerEar);
                }
            }
        }

        void DrawFace(Graphics g, Pen outline, float x, float top, float bw, float bh)
        {
            float fx = x + (state == PetState.Walk ? dir * bw * 0.06f : 0f);
            float eyeY = top + bh * 0.46f, ex = bw * 0.19f;

            using (SolidBrush cheek = new SolidBrush(CheekColor))
            {
                g.FillEllipse(cheek, fx - ex - 17 * S, eyeY + 6 * S, 14 * S, 8 * S);
                g.FillEllipse(cheek, fx + ex + 3 * S, eyeY + 6 * S, 14 * S, 8 * S);
            }

            using (SolidBrush dark = new SolidBrush(OutlineColor))
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                PointF look = LookOffset();
                for (int i = -1; i <= 1; i += 2)
                {
                    float ecx = fx + i * ex;
                    switch (state)
                    {
                        case PetState.Sleep:
                            g.DrawArc(outline, ecx - 6 * S, eyeY - 4 * S, 12 * S, 8 * S, 20, 140);
                            break;
                        case PetState.Happy:
                            g.DrawArc(outline, ecx - 6 * S, eyeY - 2 * S, 12 * S, 10 * S, 200, 140);
                            break;
                        case PetState.Drag:
                            // > <
                            float tipX = ecx + i * -4 * S, backX = ecx + i * 5 * S;
                            g.DrawLines(outline, new[] { new PointF(backX, eyeY - 5 * S), new PointF(tipX, eyeY), new PointF(backX, eyeY + 5 * S) });
                            break;
                        default:
                            if (blinkTimer < 4)
                                g.DrawLine(outline, ecx - 5 * S, eyeY, ecx + 5 * S, eyeY);
                            else
                            {
                                float px = ecx + look.X, py = eyeY + look.Y;
                                g.FillEllipse(dark, px - 5 * S, py - 6.5f * S, 10 * S, 13 * S);
                                g.FillEllipse(white, px - 3 * S, py - 4.5f * S, 4 * S, 4 * S);
                            }
                            break;
                    }
                }
            }

            float my = eyeY + 9 * S;
            using (Pen mouth = new Pen(OutlineColor, 2f * S))
            using (SolidBrush inside = new SolidBrush(MouthColor))
            {
                mouth.StartCap = mouth.EndCap = LineCap.Round;
                switch (state)
                {
                    case PetState.Eat:
                        float open = (stateTimer / 6) % 2 == 0 ? 9 * S : 4 * S;
                        g.FillEllipse(inside, fx - 5 * S, my - 1 * S, 10 * S, open);
                        g.DrawEllipse(mouth, fx - 5 * S, my - 1 * S, 10 * S, open);
                        break;
                    case PetState.Happy:
                        g.FillPie(inside, fx - 7 * S, my - 6 * S, 14 * S, 12 * S, 0, 180);
                        g.DrawPie(mouth, fx - 7 * S, my - 6 * S, 14 * S, 12 * S, 0, 180);
                        break;
                    case PetState.Drag:
                        g.DrawEllipse(mouth, fx - 3.5f * S, my - 1 * S, 7 * S, 8 * S);
                        break;
                    case PetState.Sleep:
                        float b = (2.5f + 1.5f * (float)Math.Sin(tick * 0.08)) * S;
                        g.DrawEllipse(mouth, fx - b / 2, my, b, b);
                        break;
                    default:
                        if (hunger < 30)
                            g.DrawArc(mouth, fx - 5 * S, my, 10 * S, 8 * S, 200, 140);
                        else
                        {
                            // ω
                            g.DrawArc(mouth, fx - 8 * S, my - 3 * S, 8 * S, 7 * S, 0, 180);
                            g.DrawArc(mouth, fx, my - 3 * S, 8 * S, 7 * S, 0, 180);
                        }
                        break;
                }
            }
        }

        PointF LookOffset()
        {
            if (state == PetState.Walk) return new PointF(dir * 2f * S, 0);
            Point c = Cursor.Position;
            float dx = c.X - cx, dy = c.Y - (footY - 45 * S);
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return PointF.Empty;
            float k = 2.5f * S / len;
            return new PointF(dx * k, dy * k);
        }

        void DrawFood(Graphics g, float x, float y)
        {
            int bites = Math.Max(0, Math.Min(3, (stateTimer + 29) / 30));
            if (bites == 0) return;
            float s = (10 + bites * 4) * S;
            PointF[] tri = { new PointF(x, y - s), new PointF(x - s, y + s * 0.8f), new PointF(x + s, y + s * 0.8f) };
            using (SolidBrush rice = new SolidBrush(Color.White)) g.FillPolygon(rice, tri);
            using (Pen p = new Pen(OutlineColor, 2f * S))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawPolygon(p, tri);
            }
            using (SolidBrush nori = new SolidBrush(Color.FromArgb(45, 62, 52)))
                g.FillRectangle(nori, x - s * 0.45f, y + s * 0.2f, s * 0.9f, s * 0.6f);
        }

        void DrawParticles(Graphics g, float ox, float oy)
        {
            foreach (Particle p in particles)
            {
                int a = (int)(255f * Math.Min(1f, p.Life / (p.MaxLife * 0.4f)));
                float x = ox + p.X, y = oy + p.Y;
                switch (p.Kind)
                {
                    case ParticleKind.Heart:
                        DrawHeart(g, x, y, p.Size, Color.FromArgb(a, 255, 105, 140));
                        break;
                    case ParticleKind.Zzz:
                        float t = 1f - (float)p.Life / p.MaxLife;
                        using (Font f = new Font(fontFamily, (10 + t * 10) * S, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 110, 120, 200)))
                            g.DrawString("z", f, b, x, y);
                        break;
                    case ParticleKind.Crumb:
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 235, 225, 205)))
                            g.FillEllipse(b, x - p.Size / 2, y - p.Size / 2, p.Size, p.Size);
                        break;
                    case ParticleKind.Sweat:
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 120, 190, 255)))
                            g.FillEllipse(b, x - p.Size / 2, y - p.Size * 0.7f, p.Size, p.Size * 1.4f);
                        break;
                    case ParticleKind.Dust:
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a * 6 / 10, 200, 190, 190)))
                            g.FillEllipse(b, x - p.Size / 2, y - p.Size / 2, p.Size, p.Size);
                        break;
                }
            }
        }

        void DrawBubble(Graphics g, float x, float tipY)
        {
            if (bubbleFrames <= 0 || string.IsNullOrEmpty(bubbleText)) return;
            int a = Math.Min(255, bubbleFrames * 25);
            SizeF sz = g.MeasureString(bubbleText, bubbleFont, (int)(170 * S));
            float pw = sz.Width + 18 * S, ph = sz.Height + 10 * S;
            float left = Clamp(x - pw / 2, 2, W - pw - 2);
            float bottom = tipY - 8 * S, topY = Math.Max(2, bottom - ph);
            using (GraphicsPath box = RoundedRect(left, topY, pw, bottom - topY, 10 * S))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            using (Pen pen = new Pen(Color.FromArgb(a, OutlineColor), 2f * S))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(a, OutlineColor)))
            {
                PointF[] tail = { new PointF(x - 7 * S, bottom - S), new PointF(x + 7 * S, bottom - S), new PointF(x, tipY) };
                g.FillPath(fill, box);
                g.DrawPath(pen, box);
                g.FillPolygon(fill, tail);
                g.DrawLine(pen, tail[0].X, bottom, tail[2].X, tail[2].Y);
                g.DrawLine(pen, tail[1].X, bottom, tail[2].X, tail[2].Y);
                g.DrawString(bubbleText, bubbleFont, text, new RectangleF(left + 9 * S, topY + 5 * S, sz.Width + 2 * S, sz.Height + 2 * S));
            }
        }

        static void DrawHeart(Graphics g, float x, float y, float s, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            using (SolidBrush b = new SolidBrush(c))
            {
                p.AddBezier(x, y + s * 0.3f, x, y, x - s * 0.5f, y, x - s * 0.5f, y + s * 0.3f);
                p.AddBezier(x - s * 0.5f, y + s * 0.3f, x - s * 0.5f, y + s * 0.6f, x, y + s * 0.75f, x, y + s);
                p.AddBezier(x, y + s, x, y + s * 0.75f, x + s * 0.5f, y + s * 0.6f, x + s * 0.5f, y + s * 0.3f);
                p.AddBezier(x + s * 0.5f, y + s * 0.3f, x + s * 0.5f, y, x, y, x, y + s * 0.3f);
                g.FillPath(b, p);
            }
        }

        // Mochi shape: big round top corners, small bottom corners.
        static GraphicsPath BodyPath(float l, float t, float w, float h)
        {
            float rt = Math.Min(w * 0.45f, h * 0.6f), rb = Math.Min(w * 0.2f, h * 0.3f);
            GraphicsPath p = new GraphicsPath();
            p.AddArc(l, t, rt * 2, rt * 2, 180, 90);
            p.AddArc(l + w - rt * 2, t, rt * 2, rt * 2, 270, 90);
            p.AddArc(l + w - rb * 2, t + h - rb * 2, rb * 2, rb * 2, 0, 90);
            p.AddArc(l, t + h - rb * 2, rb * 2, rb * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        static GraphicsPath RoundedRect(float l, float t, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w, h) / 2);
            GraphicsPath p = new GraphicsPath();
            p.AddArc(l, t, r * 2, r * 2, 180, 90);
            p.AddArc(l + w - r * 2, t, r * 2, r * 2, 270, 90);
            p.AddArc(l + w - r * 2, t + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(l, t + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        Icon MakeTrayIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(OutlineColor, 2f))
                using (SolidBrush fill = new SolidBrush(BodyBottom))
                using (SolidBrush dark = new SolidBrush(OutlineColor))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PointF[] le = { new PointF(5, 14), new PointF(7, 2), new PointF(14, 8) };
                    PointF[] re = { new PointF(27, 14), new PointF(25, 2), new PointF(18, 8) };
                    g.FillPolygon(fill, le); g.DrawPolygon(pen, le);
                    g.FillPolygon(fill, re); g.DrawPolygon(pen, re);
                    using (GraphicsPath body = BodyPath(2, 6, 28, 24)) { g.FillPath(fill, body); g.DrawPath(pen, body); }
                    g.FillEllipse(dark, 9, 15, 4, 5);
                    g.FillEllipse(dark, 19, 15, 4, 5);
                }
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        // ---------- helpers ----------

        Rectangle WorkArea()
        {
            return Screen.FromPoint(new Point((int)cx, (int)footY - 10)).WorkingArea;
        }

        void SetState(PetState s, int frames) { state = s; stateTimer = frames; }

        void Say(string text, int frames) { bubbleText = text; bubbleFrames = frames; }

        string Pick(string[] lines) { return lines[rnd.Next(lines.Length)]; }

        static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }

        static FontFamily PickFontFamily()
        {
            foreach (string name in new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei UI", "Segoe UI" })
            {
                try { return new FontFamily(name); } catch (ArgumentException) { }
            }
            return SystemFonts.DefaultFont.FontFamily;
        }

        // Hunger survives restarts; it drains slowly while the pet is closed.
        void LoadState()
        {
            try
            {
                if (!File.Exists(statePath)) return;
                string[] parts = File.ReadAllText(statePath).Trim().Split(';');
                float saved = float.Parse(parts[0], CultureInfo.InvariantCulture);
                DateTime when = new DateTime(long.Parse(parts[1], CultureInfo.InvariantCulture), DateTimeKind.Utc);
                double hours = (DateTime.UtcNow - when).TotalHours;
                hunger = (float)Math.Max(Math.Min(saved, 10), saved - hours * 10);
            }
            catch { }
        }

        void SaveState()
        {
            try { File.WriteAllText(statePath, string.Format(CultureInfo.InvariantCulture, "{0};{1}", hunger, DateTime.UtcNow.Ticks)); }
            catch { }
        }
    }
}
