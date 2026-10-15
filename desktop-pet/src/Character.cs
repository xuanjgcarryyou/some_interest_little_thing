using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopPet
{
    // Snapshot of everything a character needs to draw one frame.
    class PetView
    {
        public float X, Y;      // foot center, canvas coords
        public float S;         // DPI scale
        public PetState State;
        public int Tick, Dir, StateTimer;
        public float Squash = 1f;
        public bool Blink, Hungry;
        public PointF Look;     // pupil offset toward the cursor
        public float VX;        // horizontal movement this frame, px
        public bool Speaking;
        public Mouth Mouth;     // lip-sync shape while speaking
        public int BlinkPhase;  // while Blink: 0 = half-closed, 1 = fully closed
        public PointF Gaze;     // where it's looking, -1..1 on each axis
    }

    class PetLines
    {
        public string Greeting;
        public string[] Talk, Click, Drag, Hungry;
        public string FoodMenu = "餵食飯糰", Eating = "嚼嚼嚼…", Ate = "好好吃！", Full = "吃不下了啦～";
        public string Hurt = "痛痛…", Sleepy = "好睏…", Wake = "嗯…早安…";
        // Only used by characters that can transform.
        public string Annoyed, TransformShout, Transformed, TransformMenu;
    }

    abstract class Character
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract PetLines Lines { get; }
        public virtual bool CanTransform { get { return false; } }
        public virtual int Form { get { return 1; } set { } }
        public virtual float SpeedFactor { get { return 1f; } }
        public virtual bool AlwaysSteaming { get { return false; } }
        public virtual float ShadowWidth { get { return 80f; } }
        // Characters with a voice speak their lines: a recorded clip when there is one, text-to-speech otherwise.
        public virtual bool HasVoice { get { return false; } }
        // Anchored characters stay where they're put (VTuber-style) instead of walking around and falling.
        public virtual bool Anchored { get { return false; } }
        public virtual byte[] VoiceClip(string text) { return null; }

        // Distance from the feet to the top of the head (unscaled px * S); the speech bubble sits above it.
        public abstract float HeadHeight(float S);
        // Widest the character gets, including arms/capes and squash.
        public virtual float BodyWidth(float S) { return 150 * S; }
        // Where steam puffs come out, relative to the feet.
        public virtual PointF[] SteamVents(float S) { return new PointF[0]; }
        // Draws the character with its feet at (v.X, v.Y).
        public abstract void Draw(Graphics g, PetView v);
        // Draws a 32x32 tray icon.
        public abstract void DrawIcon(Graphics g);

        protected static float Breathe(PetView v)
        {
            if (v.State == PetState.Drag) return 0.88f;
            if (v.State == PetState.Walk) return v.Squash * (1f + 0.05f * (float)Math.Sin(v.Tick * 0.45));
            return v.Squash * (1f + 0.025f * (float)Math.Sin(v.Tick * 0.08));
        }

        protected static float StepLift(PetView v, float S)
        {
            if (v.State == PetState.Walk) return (float)Math.Sin(v.Tick * 0.45) * 3 * S;
            if (v.State == PetState.Drag) return (float)Math.Sin(v.Tick * 0.6) * 4 * S;
            return 0f;
        }

        // 3 bites left at the start of eating, down to 0.
        protected static int Bites(PetView v) { return Math.Max(0, Math.Min(3, (v.StateTimer + 29) / 30)); }

        protected static int Facing(PetView v) { return v.Dir < 0 ? -1 : 1; }
    }

    // Shared drawing helpers so every character has the same hand-drawn line style.
    static class Ink
    {
        public static readonly Color Outline = Color.FromArgb(74, 60, 72);

        public static Pen OutlinePen(float S) { return OutlinePen(S, 2.5f); }

        public static Pen OutlinePen(float S, float width)
        {
            Pen p = new Pen(Outline, width * S);
            p.LineJoin = LineJoin.Round;
            p.StartCap = p.EndCap = LineCap.Round;
            return p;
        }

        public static PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public static void Ball(Graphics g, Pen outline, Brush fill, PointF c, float r)
        {
            g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
            g.DrawEllipse(outline, c.X - r, c.Y - r, r * 2, r * 2);
        }

        // A thick outlined stroke: arms, legs.
        public static void Limb(Graphics g, float S, Color fill, PointF a, PointF b, float width)
        {
            using (Pen o = new Pen(Outline, width + 5 * S))
            using (Pen f = new Pen(fill, width))
            {
                o.StartCap = o.EndCap = f.StartCap = f.EndCap = LineCap.Round;
                g.DrawLine(o, a, b);
                g.DrawLine(f, a, b);
            }
        }

        // A thick outlined curve: antennae, tails.
        public static void Tentacle(Graphics g, float S, Color fill, PointF a, PointF b, PointF c, PointF d, float width)
        {
            using (Pen o = new Pen(Outline, width + 5 * S))
            using (Pen f = new Pen(fill, width))
            {
                o.StartCap = o.EndCap = f.StartCap = f.EndCap = LineCap.Round;
                g.DrawBezier(o, a, b, c, d);
                g.DrawBezier(f, a, b, c, d);
            }
        }

        public static void SleepEye(Graphics g, Pen p, float x, float y, float S)
        {
            g.DrawArc(p, x - 6 * S, y - 4 * S, 12 * S, 8 * S, 20, 140);
        }

        public static void HappyEye(Graphics g, Pen p, float x, float y, float S)
        {
            g.DrawArc(p, x - 6 * S, y - 2 * S, 12 * S, 10 * S, 200, 140);
        }

        // ">" for the left eye, "<" for the right.
        public static void SquintEye(Graphics g, Pen p, float x, float y, float S, int side)
        {
            float tip = x - side * 4 * S, back = x + side * 5 * S;
            g.DrawLines(p, new[] { new PointF(back, y - 5 * S), new PointF(tip, y), new PointF(back, y + 5 * S) });
        }

        public static void Onigiri(Graphics g, float x, float y, int bites, float S)
        {
            if (bites <= 0) return;
            float s = (10 + bites * 4) * S;
            PointF[] tri = { new PointF(x, y - s), new PointF(x - s, y + s * 0.8f), new PointF(x + s, y + s * 0.8f) };
            using (SolidBrush rice = new SolidBrush(Color.White)) g.FillPolygon(rice, tri);
            using (Pen p = OutlinePen(S, 2f)) g.DrawPolygon(p, tri);
            using (SolidBrush nori = new SolidBrush(Color.FromArgb(45, 62, 52)))
                g.FillRectangle(nori, x - s * 0.45f, y + s * 0.2f, s * 0.9f, s * 0.6f);
        }

        public static void Candy(Graphics g, float x, float y, int bites, float S)
        {
            if (bites <= 0) return;
            float r = (4 + bites * 2.5f) * S;
            using (Pen o = OutlinePen(S, 2f))
            using (SolidBrush wrapper = new SolidBrush(Color.FromArgb(255, 214, 90)))
            using (SolidBrush candy = new SolidBrush(Color.FromArgb(255, 120, 170)))
            using (Pen swirl = new Pen(Color.White, 2f * S))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    PointF[] w = { new PointF(x + i * r * 0.8f, y), new PointF(x + i * r * 1.9f, y - r * 0.8f), new PointF(x + i * r * 1.9f, y + r * 0.8f) };
                    g.FillPolygon(wrapper, w);
                    g.DrawPolygon(o, w);
                }
                g.FillEllipse(candy, x - r, y - r, r * 2, r * 2);
                g.DrawArc(swirl, x - r * 0.55f, y - r * 0.55f, r * 1.1f, r * 1.1f, 200, 220);
                g.DrawEllipse(o, x - r, y - r, r * 2, r * 2);
            }
        }

        // A round mochi (daifuku) with a peek of red-bean filling.
        public static void Daifuku(Graphics g, float x, float y, int bites, float S)
        {
            if (bites <= 0) return;
            float r = (5 + bites * 2.5f) * S;
            using (Pen o = OutlinePen(S, 2f))
            using (SolidBrush rice = new SolidBrush(Color.FromArgb(255, 250, 250)))
            using (SolidBrush bean = new SolidBrush(Color.FromArgb(150, 70, 80)))
            {
                g.FillEllipse(rice, x - r, y - r * 0.85f, r * 2, r * 1.7f);
                g.DrawEllipse(o, x - r, y - r * 0.85f, r * 2, r * 1.7f);
                if (bites < 3) g.FillEllipse(bean, x - r * 0.35f, y - r * 0.3f, r * 0.7f, r * 0.5f);
            }
        }

        public static void Heart(Graphics g, float x, float y, float s, Color c)
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
        public static GraphicsPath MochiPath(float l, float t, float w, float h)
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

        public static GraphicsPath RoundedRect(float l, float t, float w, float h, float r)
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
    }
}
