using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace DesktopPet
{
    // A "Live2D-lite" character built from AI-generated art: one base image plus expression
    // patches cut out by tools/AssetBuilder, embedded in the exe under "<id>/...".
    // Each frame the composed image is drawn as thin horizontal strips with per-strip offsets,
    // which gives a soft, spring-driven sway without needing a rigged model.
    class SpriteCharacter : Character
    {
        const float TargetHeight = 220f; // on-screen height, unscaled px
        const int Strip = 3;

        class Patch
        {
            public Bitmap Image, Scaled;
            public float X, Y;
        }

        readonly string id, name;
        readonly PetLines script;
        readonly Dictionary<string, string> clipByText;
        readonly Bitmap source;
        readonly Dictionary<string, Patch> patches = new Dictionary<string, Patch>();
        readonly RectangleF mouthZone; // in source px; empty if unknown

        float scaledFor = -1f, k;
        Bitmap scaledBase, frame;
        float lean, leanVel;

        SpriteCharacter(string id, string name, PetLines script, Dictionary<string, string> clipByText, Bitmap source, RectangleF mouthZone)
        {
            this.id = id;
            this.name = name;
            this.script = script;
            this.clipByText = clipByText;
            this.source = source;
            this.mouthZone = mouthZone;
        }

        // Returns null when the exe was built without this character's assets.
        public static SpriteCharacter TryLoad(string id, string name, PetLines script, Dictionary<string, string> clipByText)
        {
            string manifest = ReadText(id + "/manifest.txt");
            if (manifest == null) return null;

            Bitmap baseImage = null;
            RectangleF zone = RectangleF.Empty;
            List<string[]> patchLines = new List<string[]>();
            foreach (string line in manifest.Split('\n'))
            {
                string[] f = line.Trim().Split(' ');
                if (f[0] == "base") baseImage = ReadImage(id + "/" + f[1]);
                else if (f[0] == "patch") patchLines.Add(f);
                else if (f[0] == "zone" && f[1] == "mouth") zone = new RectangleF(F(f[2]), F(f[3]), F(f[4]), F(f[5]));
            }
            if (baseImage == null) return null;

            SpriteCharacter c = new SpriteCharacter(id, name, script, clipByText, baseImage, zone);
            foreach (string[] f in patchLines)
            {
                Bitmap img = ReadImage(id + "/" + f[2]);
                if (img == null) continue;
                Patch p = new Patch();
                p.Image = img;
                p.X = F(f[3]);
                p.Y = F(f[4]);
                c.patches[f[1]] = p;
            }
            return c;
        }

        public override string Id { get { return id; } }
        public override string Name { get { return name; } }
        public override PetLines Lines { get { return script; } }
        public override bool HasVoice { get { return true; } }
        public override float HeadHeight(float S) { return (TargetHeight + 4) * S; }
        public override float BodyWidth(float S) { return TargetHeight * S * source.Width / source.Height * 1.4f + 30 * S; } // squash + sway
        public override float ShadowWidth { get { return TargetHeight * source.Width / source.Height * 0.55f; } }

        public override byte[] VoiceClip(string text)
        {
            string clip;
            if (text == null || !clipByText.TryGetValue(text, out clip)) return null;
            return ReadBytes(id + "/voice/" + clip + ".wav");
        }

        public override void Draw(Graphics g, PetView v)
        {
            float S = v.S;
            EnsureScaled(S);
            ComposeFrame(v);

            // Breathing and impact squash; a little bob while walking.
            float sq = v.Squash * (1f + 0.012f * (float)Math.Sin(v.Tick * (v.State == PetState.Sleep ? 0.05 : 0.08)));
            if (v.State == PetState.Drag) sq = 0.94f;
            float bob = v.State == PetState.Walk ? -(float)Math.Abs(Math.Sin(v.Tick * 0.3)) * 3 * S : 0f;

            // The upper body lags behind horizontal motion like it's on a spring, plus a gentle idle sway.
            float target = Math.Max(-14 * S, Math.Min(14 * S, -v.VX * 1.5f));
            target += (float)Math.Sin(v.Tick * 0.05) * 2.5f * S;
            if (v.State == PetState.Walk) target += Facing(v) * 3 * S;
            leanVel = (leanVel + (target - lean) * 0.12f) * 0.82f;
            lean += leanVel;

            float sx = sq, sy = 1f / sq;
            float dw = frame.Width * sx, dh = frame.Height * sy;
            float left = v.X - dw / 2, top = v.Y - dh + bob;

            InterpolationMode oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                for (int y0 = 0; y0 < frame.Height; y0 += Strip)
                {
                    int h = Math.Min(Strip, frame.Height - y0);
                    float t = (float)y0 / frame.Height; // 0 = top of head, 1 = feet
                    float dx = lean * (1 - t) * (1 - t);
                    float dy = top + y0 * sy;
                    PointF[] dest = { new PointF(left + dx, dy), new PointF(left + dx + dw, dy), new PointF(left + dx, dy + h * sy + 0.75f) };
                    g.DrawImage(frame, dest, new RectangleF(0, y0, frame.Width, h), GraphicsUnit.Pixel, ia);
                }
            }
            g.InterpolationMode = oldMode;

            if (v.State == PetState.Eat && !mouthZone.IsEmpty)
            {
                float mx = left + (mouthZone.X + mouthZone.Width / 2) * k * sx + lean * 0.5f;
                float my = top + (mouthZone.Y + mouthZone.Height / 2) * k * sy;
                Ink.Daifuku(g, mx + Facing(v) * 22 * S, my + 6 * S, Bites(v), S);
            }
        }

        void ComposeFrame(PetView v)
        {
            string eyes = null, mouth = null;
            switch (v.State)
            {
                case PetState.Sleep: eyes = "eyes_closed"; break;
                case PetState.Happy: eyes = "happy_eyes"; mouth = "happy_mouth"; break;
                case PetState.Drag: eyes = "surprised_eyes"; mouth = "surprised_mouth"; break;
                case PetState.Eat: eyes = "happy_eyes"; mouth = (v.StateTimer / 6) % 2 == 0 ? "mouth_a" : null; break;
                default: if (v.Blink) eyes = "eyes_closed"; break;
            }
            if (v.Speaking && v.State != PetState.Eat) mouth = MouthPatch(v.Mouth);

            using (Graphics fg = Graphics.FromImage(frame))
            {
                fg.Clear(Color.Transparent);
                fg.DrawImage(scaledBase, new Rectangle(0, 0, scaledBase.Width, scaledBase.Height));
                DrawPatch(fg, eyes, eyes == "happy_eyes" || eyes == "surprised_eyes" ? "eyes_closed" : null);
                DrawPatch(fg, mouth, mouth == "happy_mouth" ? "mouth_a" : mouth == "surprised_mouth" ? "mouth_o" : null);
            }
        }

        static string MouthPatch(Mouth m)
        {
            switch (m)
            {
                case Mouth.Small: return "mouth_small";
                case Mouth.A: return "mouth_a";
                case Mouth.O: return "mouth_o";
                default: return null;
            }
        }

        void DrawPatch(Graphics fg, string name, string fallback)
        {
            if (name == null) return;
            Patch p;
            if (!patches.TryGetValue(name, out p) && (fallback == null || !patches.TryGetValue(fallback, out p))) return;
            fg.DrawImage(p.Scaled, new Rectangle((int)Math.Round(p.X * k), (int)Math.Round(p.Y * k), p.Scaled.Width, p.Scaled.Height));
        }

        // Pre-scale everything once for the current DPI so per-frame drawing stays cheap.
        void EnsureScaled(float S)
        {
            if (scaledFor == S) return;
            scaledFor = S;
            k = TargetHeight * S / source.Height;
            scaledBase = Resize(source, k);
            foreach (Patch p in patches.Values) p.Scaled = Resize(p.Image, k);
            frame = new Bitmap(scaledBase.Width, scaledBase.Height, PixelFormat.Format32bppPArgb);
        }

        static Bitmap Resize(Bitmap src, float scale)
        {
            int w = Math.Max(1, (int)Math.Round(src.Width * scale)), h = Math.Max(1, (int)Math.Round(src.Height * scale));
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dst))
            using (ImageAttributes ia = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }

        public override void DrawIcon(Graphics g)
        {
            // Square crop around the head: from the top of the image down to just below the mouth.
            float bottom = mouthZone.IsEmpty ? source.Height * 0.35f : mouthZone.Bottom * 1.08f;
            float cx = mouthZone.IsEmpty ? source.Width / 2f : mouthZone.X + mouthZone.Width / 2;
            RectangleF crop = new RectangleF(cx - bottom / 2, 0, bottom, bottom);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new RectangleF(0, 0, 32, 32), crop, GraphicsUnit.Pixel);
        }

        // ---------- embedded resources ----------

        static Stream Open(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }

        static string ReadText(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        static byte[] ReadBytes(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null) return null;
                using (MemoryStream m = new MemoryStream())
                {
                    s.CopyTo(m);
                    return m.ToArray();
                }
            }
        }

        static Bitmap ReadImage(string name)
        {
            byte[] bytes = ReadBytes(name);
            if (bytes == null) return null;
            using (MemoryStream m = new MemoryStream(bytes))
            using (Bitmap loaded = new Bitmap(m))
                return new Bitmap(loaded); // detach from the stream
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
    }
}
