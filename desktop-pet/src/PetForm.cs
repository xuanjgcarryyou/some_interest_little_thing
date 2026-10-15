using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DesktopPet
{
    // A borderless per-pixel-alpha window: fully transparent pixels are click-through,
    // so only the pet itself (and its speech bubble) catches the mouse.
    class PetForm : Form
    {
        const int Fps = 30;
        const int TransformFrames = 75, TransformSwapAt = 38;

        readonly float S;                 // DPI scale
        readonly int W, H, FootMargin;    // window size; pet's feet sit FootMargin above the window bottom
        readonly Bitmap canvas;
        readonly FontFamily fontFamily;
        readonly Font bubbleFont;
        readonly Random rnd = new Random();
        readonly List<Particle> particles = new List<Particle>();
        readonly List<int> recentClicks = new List<int>();
        readonly List<Character> characters = new List<Character> { new Mochi(), new Buu() };
        readonly PetView view = new PetView();
        readonly Voice voice = new Voice();
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly string statePath;
        readonly List<ToolStripMenuItem> characterItems = new List<ToolStripMenuItem>();
        ToolStripMenuItem hungerItem, feedItem, sleepItem, followItem, transformItem, muteItem;
        Character character;

        // World position = screen coords of the pet's foot center.
        float cx, footY, vx, vy, lastCx;
        int dir = 1;
        PetState state;
        int stateTimer, tick, idleFrames, flashFrames;
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
            Character sprite = Xiaoshu.TryCreate(); // only present when the exe was built with its art
            if (sprite != null) characters.Add(sprite);

            // Tall enough for the tallest character plus a speech bubble above its head, wide enough for the widest.
            float tallest = 0, widest = 0;
            foreach (Character c in characters)
            {
                tallest = Math.Max(tallest, c.HeadHeight(S));
                widest = Math.Max(widest, c.BodyWidth(S));
            }
            W = (int)Math.Max(240 * S, widest);
            H = (int)(tallest + 110 * S);
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

            character = characters[0];
            statePath = Path.Combine(Application.StartupPath, "mochi-state.txt");
            LoadState();

            ContextMenuStrip menu = BuildMenu();
            ContextMenuStrip = menu;
            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            UpdateTrayIcon();
            tray.Visible = true;
            tray.DoubleClick += delegate { BringHome(); };

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            cx = wa.Left + wa.Width * 0.75f;
            footY = wa.Top + 120 * S;
            lastCx = cx;
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
            Say(hunger < 30 ? Pick(character.Lines.Hungry) : character.Lines.Greeting, Fps * 3);
            Render(WorkArea().Bottom);
            timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer.Stop();
            SaveState();
            voice.Dispose();
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
            downPos = PointToScreen(e.Location); // where the press happened, not where the cursor is by now
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
                case PetState.Drag: UpdateDragged(); break;
                case PetState.Fall: UpdateFall(wa, ground, half); break;
                case PetState.Happy: UpdateHappy(); break;
                case PetState.Eat: UpdateEat(); break;
                case PetState.Transform: UpdateTransform(); break;
            }

            if (state != PetState.Drag && state != PetState.Fall)
            {
                if (footY < ground - 1) { vx = vy = 0; SetState(PetState.Fall, 0); } // e.g. the taskbar moved
                else footY = ground;
                cx = Clamp(cx, wa.Left + half, wa.Right - half);
            }

            if (character.AlwaysSteaming && state != PetState.Sleep && tick % 25 == 0) SpawnSteam();
            squash += (1f - squash) * 0.15f;
            if (flashFrames > 0) flashFrames--;
            if (--blinkTimer < 0) blinkTimer = rnd.Next(60, 150);
            if (bubbleFrames > 0) bubbleFrames--;
            if (voice.IsSpeaking && bubbleFrames < 10) bubbleFrames = 10; // keep the bubble up until the line is finished
            UpdateParticles();
            Render(ground);
            lastCx = cx;
        }

        void UpdateDrag(float ground)
        {
            Point p = Cursor.Position;
            if (!dragMoved)
            {
                if (Math.Abs(p.X - downPos.X) <= 4 * S && Math.Abs(p.Y - downPos.Y) <= 4 * S) return;
                dragMoved = true;
                SetState(PetState.Drag, 0);
                Say(Pick(character.Lines.Drag), Fps * 2);
            }
            float nx = p.X - dragOffX, ny = Math.Min(p.Y - dragOffY, ground);
            vx = vx * 0.5f + (nx - cx) * 0.5f;
            vy = vy * 0.5f + (ny - footY) * 0.5f;
            cx = nx;
            footY = ny;
        }

        void UpdateDragged()
        {
            if (character.SteamVents(S).Length > 0)
            {
                if (tick % 6 == 0) SpawnSteam();
            }
            else if (tick % 12 == 0)
                Spawn(ParticleKind.Sweat, (rnd.Next(2) == 0 ? -1 : 1) * 36 * S, -(character.HeadHeight(S) - 16 * S));
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
                if (r < 58) Say(hunger < 30 ? Pick(character.Lines.Hungry) : Pick(character.Lines.Talk), Fps * 3);
                SetState(PetState.Idle, rnd.Next(Fps * 2, Fps * 6));
            }
        }

        void UpdateWalk(Rectangle wa, float half)
        {
            float speed = (hunger < 20 ? 1f : 2f) * S * character.SpeedFactor;
            if (followMouse)
            {
                float d = Cursor.Position.X - cx;
                if (Math.Abs(d) < 30 * S) { SetState(PetState.Idle, Fps); return; }
                dir = d > 0 ? 1 : -1;
                speed = Math.Min(Math.Abs(d), 4f * S * character.SpeedFactor);
                stateTimer = Fps;
            }
            cx += dir * speed;
            if (cx <= wa.Left + half) dir = 1;
            else if (cx >= wa.Right - half) dir = -1;
            if (--stateTimer <= 0) SetState(PetState.Idle, rnd.Next(Fps * 2, Fps * 5));
        }

        void UpdateSleep()
        {
            if (tick % 40 == 0) Spawn(ParticleKind.Zzz, 30 * S, -(character.HeadHeight(S) - 18 * S));
            if (--stateTimer <= 0) WakeUp(character.Lines.Wake);
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
                if (vy > 22 * S) Say(character.Lines.Hurt, Fps * 2);
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
            if (stateTimer % 9 == 0) Spawn(ParticleKind.Heart, (float)(rnd.NextDouble() - 0.5) * 60 * S, -(character.HeadHeight(S) - 22 * S));
            if (--stateTimer <= 0) SetState(PetState.Idle, Fps * 2);
        }

        void UpdateEat()
        {
            if (stateTimer % 15 == 0) Spawn(ParticleKind.Crumb, dir * 34 * S, -30 * S);
            if (--stateTimer > 0) return;
            hunger = Math.Min(100f, hunger + 35f);
            // Sweets calm a transformed character back down.
            if (character.CanTransform && character.Form == 2) { StartTransform(); return; }
            Say(character.Lines.Ate, Fps * 2);
            SetState(PetState.Happy, 45);
        }

        void UpdateTransform()
        {
            if (stateTimer % 3 == 0) SpawnSteam();
            if (stateTimer == TransformSwapAt)
            {
                string line = character.Lines.Transformed; // the old form announces what it becomes
                character.Form = character.Form == 1 ? 2 : 1;
                for (int i = 0; i < 12; i++) SpawnSteam();
                squash = 1.35f;
                flashFrames = 14;
                Say(line, Fps * 3);
                UpdateTrayIcon();
            }
            if (--stateTimer <= 0) SetState(PetState.Idle, Fps * 2);
        }

        // ---------- actions ----------

        void Pet()
        {
            idleFrames = 0;
            if (state == PetState.Sleep) { WakeUp(character.Lines.Wake); return; }
            if (state == PetState.Fall || state == PetState.Eat || state == PetState.Transform) return;

            // Poking a transformable character too often makes it angry.
            if (character.CanTransform && character.Form == 1)
            {
                recentClicks.Add(tick);
                recentClicks.RemoveAll(t => tick - t > Fps * 2);
                if (recentClicks.Count >= 5) { recentClicks.Clear(); StartTransform(); return; }
                if (recentClicks.Count >= 3)
                {
                    Say(character.Lines.Annoyed, Fps * 2);
                    for (int i = 0; i < 3; i++) SpawnSteam();
                    SetState(PetState.Idle, Fps);
                    return;
                }
            }
            Say(Pick(character.Lines.Click), Fps * 2);
            SetState(PetState.Happy, 45);
        }

        void Feed()
        {
            idleFrames = 0;
            if (state == PetState.Drag || state == PetState.Fall || state == PetState.Transform) return;
            if (hunger > 90 && character.Form == 1) { Say(character.Lines.Full, Fps * 2); return; }
            SetState(PetState.Eat, 90);
            Say(character.Lines.Eating, 90);
        }

        void StartTransform()
        {
            if (!character.CanTransform || state == PetState.Drag || state == PetState.Fall || state == PetState.Transform) return;
            idleFrames = 0;
            Say(character.Lines.TransformShout, Fps * 3);
            SetState(PetState.Transform, TransformFrames);
        }

        void GoToSleep()
        {
            if (state == PetState.Drag || state == PetState.Fall || state == PetState.Transform) return;
            SetState(PetState.Sleep, rnd.Next(Fps * 120, Fps * 240));
            Say(character.Lines.Sleepy, Fps * 2);
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

        void SwitchCharacter(Character next)
        {
            if (next == character) return;
            character = next;
            idleFrames = 0;
            recentClicks.Clear();
            for (int i = 0; i < 8; i++) Spawn(ParticleKind.Dust, (i < 4 ? -1 : 1) * (20 + i * 4) * S, -(10 + i * 6) * S);
            squash = 1.3f;
            if (state != PetState.Drag && state != PetState.Fall) SetState(PetState.Idle, Fps * 2);
            Say(character.Lines.Greeting, Fps * 3);
            UpdateTrayIcon();
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            hungerItem = new ToolStripMenuItem();
            hungerItem.Enabled = false;
            m.Items.Add(hungerItem);
            m.Items.Add(new ToolStripSeparator());

            feedItem = new ToolStripMenuItem("餵食", null, delegate { Feed(); });
            m.Items.Add(feedItem);
            m.Items.Add("摸摸頭", null, delegate { Pet(); });
            transformItem = new ToolStripMenuItem("變身！", null, delegate { StartTransform(); });
            m.Items.Add(transformItem);
            followItem = new ToolStripMenuItem("跟著滑鼠走", null, delegate
            {
                followMouse = !followMouse;
                idleFrames = 0;
                if (followMouse && state == PetState.Sleep) WakeUp("好～我跟著你！");
            });
            m.Items.Add(followItem);
            sleepItem = new ToolStripMenuItem("去睡覺", null, delegate
            {
                if (state == PetState.Sleep) WakeUp(character.Lines.Wake); else GoToSleep();
            });
            m.Items.Add(sleepItem);
            m.Items.Add("說句話", null, delegate
            {
                idleFrames = 0;
                Say(hunger < 30 ? Pick(character.Lines.Hungry) : Pick(character.Lines.Talk), Fps * 3);
            });

            ToolStripMenuItem switchMenu = new ToolStripMenuItem("換角色");
            foreach (Character c in characters)
            {
                Character target = c;
                ToolStripMenuItem item = new ToolStripMenuItem(c.Name, null, delegate { SwitchCharacter(target); });
                item.Tag = c;
                characterItems.Add(item);
                switchMenu.DropDownItems.Add(item);
            }
            m.Items.Add(switchMenu);
            muteItem = new ToolStripMenuItem("靜音", null, delegate
            {
                voice.Muted = !voice.Muted;
                if (voice.Muted) voice.Stop();
            });
            m.Items.Add(muteItem);
            m.Items.Add("叫回螢幕中間", null, delegate { BringHome(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("掰掰（結束）", null, delegate { Close(); });

            m.Opening += delegate
            {
                hungerItem.Text = string.Format("{0}　飽足度：{1}%", character.Name, (int)Math.Round(hunger));
                feedItem.Text = character.Lines.FoodMenu;
                transformItem.Visible = character.CanTransform;
                transformItem.Text = character.Lines.TransformMenu ?? "變身！";
                transformItem.Enabled = state != PetState.Drag && state != PetState.Fall && state != PetState.Transform;
                followItem.Checked = followMouse;
                muteItem.Visible = character.HasVoice;
                muteItem.Checked = voice.Muted;
                sleepItem.Text = state == PetState.Sleep ? "叫醒" + character.Name : "去睡覺";
                foreach (ToolStripMenuItem item in characterItems) item.Checked = item.Tag == character;
            };
            return m;
        }

        // ---------- particles ----------

        void SpawnSteam()
        {
            PointF[] vents = character.SteamVents(S);
            if (vents.Length == 0) return;
            PointF v = vents[rnd.Next(vents.Length)];
            Spawn(ParticleKind.Steam, v.X, v.Y);
        }

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
                case ParticleKind.Steam: p.VX = Math.Sign(x) * (0.6f + r * 0.8f) * S; p.VY = -(1f + r) * S; p.Size = (5 + r * 3) * S; p.MaxLife = 28; break;
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
                float headH = character.HeadHeight(S);
                if (footY >= ground - 1) DrawShadow(g, ox, oy, hop, character.ShadowWidth * S);

                FillView(ox, oy + hop);
                GraphicsState saved = g.Save();
                if (state == PetState.Drag)
                {
                    // Dangle from where it's being held, near the top of the head.
                    float px = ox, py = oy - headH * 0.8f;
                    g.TranslateTransform(px, py);
                    g.RotateTransform((float)Math.Sin(tick * 0.25) * 10f);
                    g.TranslateTransform(-px, -py);
                }
                else if (state == PetState.Transform && stateTimer > TransformSwapAt)
                    g.TranslateTransform((float)(rnd.NextDouble() - 0.5) * 5 * S, 0); // shaking with rage
                character.Draw(g, view);
                g.Restore(saved);

                if (flashFrames > 0) DrawFlash(g, ox, oy - headH * 0.45f, headH * 0.75f);
                DrawParticles(g, ox, oy);
                DrawBubble(g, ox, oy + hop - headH - 6 * S);
            }
            UpdateLayered((int)Math.Round(cx - W / 2f), (int)Math.Round(footY - (H - FootMargin)));
        }

        void FillView(float x, float y)
        {
            view.X = x;
            view.Y = y;
            view.S = S;
            view.State = state;
            view.Tick = tick;
            view.Dir = dir;
            view.StateTimer = stateTimer;
            view.Squash = squash;
            view.Blink = blinkTimer < 4;
            view.Hungry = hunger < 30;
            view.Look = LookOffset();
            view.VX = cx - lastCx;
            view.Speaking = voice.IsSpeaking;
            view.Mouth = view.Speaking ? voice.CurrentMouth() : Mouth.Closed;
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

        void DrawShadow(Graphics g, float x, float y, float hop, float width)
        {
            float k = 1f + hop / (50 * S); // hop is negative, so the shadow shrinks mid-jump
            float w = width * k * squash, h = 9 * S * k;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, x - w / 2, y - h / 2, w, h);
        }

        void DrawFlash(Graphics g, float x, float y, float r)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(x - r, y - r, r * 2, r * 2);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = Color.FromArgb(Math.Min(230, flashFrames * 17), 255, 250, 220);
                    b.SurroundColors = new[] { Color.FromArgb(0, 255, 250, 220) };
                    g.FillPath(b, p);
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

        void DrawParticles(Graphics g, float ox, float oy)
        {
            foreach (Particle p in particles)
            {
                int a = (int)(255f * Math.Min(1f, p.Life / (p.MaxLife * 0.4f)));
                float x = ox + p.X, y = oy + p.Y;
                float t = 1f - (float)p.Life / p.MaxLife;
                switch (p.Kind)
                {
                    case ParticleKind.Heart:
                        Ink.Heart(g, x, y, p.Size, Color.FromArgb(a, 255, 105, 140));
                        break;
                    case ParticleKind.Zzz:
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
                    case ParticleKind.Steam:
                        float s = p.Size * (1f + t * 1.6f);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a * 85 / 100, 255, 255, 255)))
                        using (Pen pen = new Pen(Color.FromArgb(a * 35 / 100, 150, 140, 150), S))
                        {
                            g.FillEllipse(b, x - s / 2, y - s / 2, s, s);
                            g.DrawEllipse(pen, x - s / 2, y - s / 2, s, s);
                        }
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
            using (GraphicsPath box = Ink.RoundedRect(left, topY, pw, bottom - topY, 10 * S))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            using (Pen pen = new Pen(Color.FromArgb(a, Ink.Outline), 2f * S))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(a, Ink.Outline)))
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

        void UpdateTrayIcon()
        {
            Icon old = tray.Icon;
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    character.DrawIcon(g);
                }
                IntPtr h = bmp.GetHicon();
                tray.Icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
            }
            tray.Text = character.Name;
            if (old != null) old.Dispose();
        }

        // ---------- helpers ----------

        Rectangle WorkArea()
        {
            return Screen.FromPoint(new Point((int)cx, (int)footY - 10)).WorkingArea;
        }

        void SetState(PetState s, int frames) { state = s; stateTimer = frames; }

        void Say(string text, int frames)
        {
            bubbleText = text;
            bubbleFrames = frames;
            if (character.HasVoice) voice.Speak(character.VoiceClip(text), text);
            else voice.Stop();
        }

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

        // State file: hunger;savedAtUtcTicks;characterId;form;muted. Hunger drains slowly while closed.
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
                if (parts.Length >= 4)
                {
                    foreach (Character c in characters)
                        if (c.Id == parts[2]) character = c;
                    character.Form = int.Parse(parts[3], CultureInfo.InvariantCulture);
                }
                if (parts.Length >= 5) voice.Muted = parts[4] == "1";
            }
            catch { }
        }

        void SaveState()
        {
            try
            {
                File.WriteAllText(statePath, string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4}",
                    hunger, DateTime.UtcNow.Ticks, character.Id, character.Form, voice.Muted ? 1 : 0));
            }
            catch { }
        }
    }
}
