using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DesktopPet
{
    // A VTuber-style character built from AI-generated art: one base image plus expression patches
    // cut out by tools/AssetBuilder, embedded in the exe under "<id>/...".
    // Each frame the expression is composed onto the base and then warped pixel by pixel, Live2D-style:
    // the head rolls around the neck, the face shifts toward where it's looking, the chest breathes
    // and the hair at the sides sways. Every motion is a small spring-driven parameter.
    class SpriteCharacter : Character
    {
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
        readonly RectangleF eyesZone, mouthZone; // in source px; empty if unknown
        readonly float targetHeight;             // on-screen height, unscaled px
        readonly bool anchored;

        // Per-DPI caches.
        float scaledFor = -1f, k;
        int margin;
        Bitmap scaledBase, frame, output;
        int[] srcPx, dstPx;
        int[] rowMin, rowMax;  // opaque span of each row of the composed frame
        string composedKey;    // which expression srcPx currently holds
        float faceX, faceY, faceR, neckY, neckBand; // rig geometry in frame px

        // Rig parameters (spring-driven): head roll in degrees, gaze yaw/pitch in -1..1.
        float roll, rollVel, yaw, yawVel, pitch, pitchVel;

        SpriteCharacter(string id, string name, PetLines script, Dictionary<string, string> clipByText, Bitmap source,
            RectangleF eyesZone, RectangleF mouthZone, float targetHeight, bool anchored)
        {
            this.id = id;
            this.name = name;
            this.script = script;
            this.clipByText = clipByText;
            this.source = source;
            this.eyesZone = eyesZone;
            this.mouthZone = mouthZone;
            this.targetHeight = targetHeight;
            this.anchored = anchored;
        }

        // Returns null when the exe was built without this character's assets.
        public static SpriteCharacter TryLoad(string id, string name, PetLines script, Dictionary<string, string> clipByText,
            float targetHeight, bool anchored)
        {
            string manifest = ReadText(id + "/manifest.txt");
            if (manifest == null) return null;

            Bitmap baseImage = null;
            RectangleF eyes = RectangleF.Empty, mouth = RectangleF.Empty;
            List<string[]> patchLines = new List<string[]>();
            foreach (string line in manifest.Split('\n'))
            {
                string[] f = line.Trim().Split(' ');
                if (f[0] == "base") baseImage = ReadImage(id + "/" + f[1]);
                else if (f[0] == "patch") patchLines.Add(f);
                else if (f[0] == "zone")
                {
                    RectangleF r = new RectangleF(F(f[2]), F(f[3]), F(f[4]), F(f[5]));
                    if (f[1] == "mouth") mouth = r;
                    else if (f[1] == "eyes") eyes = r;
                }
            }
            if (baseImage == null) return null;

            SpriteCharacter c = new SpriteCharacter(id, name, script, clipByText, baseImage, eyes, mouth, targetHeight, anchored);
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
        public override bool Anchored { get { return anchored; } }
        public override float HeadHeight(float S) { return (targetHeight + 4) * S; }
        public override float BodyWidth(float S) { return targetHeight * S * source.Width / source.Height + 60 * S; }
        public override float ShadowWidth { get { return targetHeight * source.Width / source.Height * 0.55f; } }

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
            string before = composedKey;
            ComposeFrame(v);
            UpdateRig(v);
            // Idle motion is slow, so re-warping every other frame is enough; anything lively gets every frame.
            bool lively = v.State == PetState.Drag || v.State == PetState.Happy || v.Speaking || composedKey != before;
            if (lively || (v.Tick & 1) == 0) Warp(v);

            // Feet (the bottom edge of the art) sit at (v.X, v.Y).
            int left = (int)Math.Round(v.X - output.Width / 2f), top = (int)Math.Round(v.Y - output.Height);
            g.DrawImage(output, new Rectangle(left, top, output.Width, output.Height));

            if (v.State == PetState.Eat && !mouthZone.IsEmpty)
            {
                float mx = left + margin + (mouthZone.X + mouthZone.Width) * k + 10 * S;
                float my = top + margin + (mouthZone.Y + mouthZone.Height / 2) * k;
                Ink.Daifuku(g, mx, my, Bites(v), S);
            }
        }

        // ---------- expression ----------

        void ComposeFrame(PetView v)
        {
            string eyes = null, mouth = null;
            switch (v.State)
            {
                case PetState.Sleep: eyes = "eyes_closed"; break;
                case PetState.Happy: eyes = "happy_eyes"; mouth = "happy_mouth"; break;
                case PetState.Drag: eyes = "surprised_eyes"; mouth = "surprised_mouth"; break;
                case PetState.Eat: eyes = "happy_eyes"; mouth = (v.StateTimer / 6) % 2 == 0 ? "mouth_a" : null; break;
                default:
                    if (v.Blink) eyes = v.BlinkPhase == 1 ? "eyes_closed" : "eyes_half";
                    break;
            }
            if (v.Speaking && v.State != PetState.Eat) mouth = MouthPatch(v.Mouth);

            // Recomposing is the expensive part, and the expression only changes every few frames.
            string key = eyes + "|" + mouth;
            if (key == composedKey) return;
            composedKey = key;

            using (Graphics fg = Graphics.FromImage(frame))
            {
                fg.Clear(Color.Transparent);
                fg.DrawImage(scaledBase, new Rectangle(margin, margin, scaledBase.Width, scaledBase.Height));
                DrawPatch(fg, eyes, eyes == "eyes_closed" ? null : "eyes_closed");
                DrawPatch(fg, mouth, mouth == "happy_mouth" ? "mouth_a" : mouth == "surprised_mouth" ? "mouth_o" : null);
            }

            int w = frame.Width, h = frame.Height;
            BitmapData sd = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            Marshal.Copy(sd.Scan0, srcPx, 0, srcPx.Length);
            frame.UnlockBits(sd);
            for (int y = 0; y < h; y++)
            {
                int x0 = w, x1 = -1;
                for (int x = 0; x < w; x++)
                    if (srcPx[y * w + x] != 0) { if (x < x0) x0 = x; x1 = x; }
                rowMin[y] = x0;
                rowMax[y] = x1;
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
            fg.DrawImage(p.Scaled, new Rectangle(margin + (int)Math.Round(p.X * k), margin + (int)Math.Round(p.Y * k), p.Scaled.Width, p.Scaled.Height));
        }

        // ---------- rig ----------

        void UpdateRig(PetView v)
        {
            float t = v.Tick;
            float yawT = v.Gaze.X, pitchT = v.Gaze.Y;
            float rollT = (float)(Math.Sin(t * 0.021) * 1.6 + Math.Sin(t * 0.047 + 1.3) * 0.8); // idle wander
            switch (v.State)
            {
                case PetState.Sleep: yawT = 0; pitchT = 0.7f; rollT = 7f + (float)Math.Sin(t * 0.02); break; // head droops
                case PetState.Happy: rollT = (float)Math.Sin(v.StateTimer * 0.35) * 5f; pitchT = -0.25f; break;
                case PetState.Drag: rollT = Math.Max(-10f, Math.Min(10f, -v.VX * 0.6f)); pitchT = 0.2f; break;
            }
            if (v.Speaking && v.Mouth != Mouth.Closed) pitchT += 0.12f; // little nods while talking

            Spring(ref roll, ref rollVel, rollT, 0.05f, 0.85f);
            Spring(ref yaw, ref yawVel, yawT, 0.06f, 0.82f);
            Spring(ref pitch, ref pitchVel, pitchT, 0.06f, 0.82f);
        }

        static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping)
        {
            velocity = (velocity + (target - value) * stiffness) * damping;
            value += velocity;
        }

        // Inverse-maps every output pixel to where it comes from in the composed frame.
        void Warp(PetView v)
        {
            int w = frame.Width, h = frame.Height;
            float S = v.S, t = v.Tick;
            int reach = margin + 2; // no displacement is bigger than the margin, so pixels farther out stay empty
            Array.Clear(dstPx, 0, dstPx.Length);

            float a = roll * (float)Math.PI / 180f;
            float yawPx = yaw * 7 * S, pitchPx = pitch * 5 * S;
            float breath = (float)Math.Sin(t * 0.07) * 1.6f * S;
            float sway = (float)Math.Sin(t * 0.013) * 1.2f * S;
            float[] hairWave = new float[h];
            for (int y = 0; y < h; y++) hairWave[y] = (float)Math.Sin(t * 0.09 + y * 0.04) * 1.3f * S;

            for (int y = 0; y < h; y++)
            {
                // Only the columns the character can reach on this row (looking a few rows up and down).
                int x0 = w, x1 = -1;
                for (int yy = Math.Max(0, y - reach); yy <= Math.Min(h - 1, y + reach); yy++)
                {
                    if (rowMin[yy] < x0) x0 = rowMin[yy];
                    if (rowMax[yy] > x1) x1 = rowMax[yy];
                }
                if (x1 < 0) continue;
                x0 = Math.Max(0, x0 - reach);
                x1 = Math.Min(w - 1, x1 + reach);

                float up = (float)(h - y) / h; // 0 at the bottom edge, 1 at the top
                float wHead = SmoothStep((neckY + neckBand - y) / (2 * neckBand));
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = sway * up, dy = -breath * up;
                    if (wHead > 0)
                    {
                        // Roll around the neck (small-angle rotation), weighted so the shoulders stay put.
                        float rx = x - faceX, ry = y - neckY, aw = a * wHead;
                        dx += -ry * aw;
                        dy += rx * aw;
                        // Face parallax: the middle of the face shifts toward the gaze, the outline barely moves.
                        float fx = (x - faceX) / faceR, fy = (y - faceY) / faceR, dd = fx * fx + fy * fy;
                        if (dd < 1)
                        {
                            float wf = (1 - dd) * (1 - dd) * wHead;
                            dx += yawPx * wf;
                            dy += pitchPx * wf;
                        }
                    }
                    float side = Math.Abs(x - faceX) - faceR * 1.05f;
                    if (side > 0) dx += hairWave[y] * Math.Min(1f, side / (faceR * 0.6f));

                    dstPx[row + x] = Sample(x - dx, y - dy, w, h);
                }
            }

            BitmapData od = output.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            Marshal.Copy(dstPx, 0, od.Scan0, dstPx.Length);
            output.UnlockBits(od);
        }

        // Bilinear sample of premultiplied ARGB. Below the frame we clamp, so the cut-off body stays solid.
        int Sample(float sx, float sy, int w, int h)
        {
            if (sy > h - 1.001f) sy = h - 1.001f;
            int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
            if (x0 < 0 || y0 < 0 || x0 + 1 >= w) return 0;
            int fx = (int)((sx - x0) * 256), fy = (int)((sy - y0) * 256);
            int i = y0 * w + x0;
            int c00 = srcPx[i], c10 = srcPx[i + 1], c01 = srcPx[i + w], c11 = srcPx[i + w + 1];
            if ((c00 | c10 | c01 | c11) == 0) return 0;
            int w00 = (256 - fx) * (256 - fy), w10 = fx * (256 - fy), w01 = (256 - fx) * fy, w11 = fx * fy;
            int ca = (((c00 >> 24) & 255) * w00 + ((c10 >> 24) & 255) * w10 + ((c01 >> 24) & 255) * w01 + ((c11 >> 24) & 255) * w11) >> 16;
            int cr = (((c00 >> 16) & 255) * w00 + ((c10 >> 16) & 255) * w10 + ((c01 >> 16) & 255) * w01 + ((c11 >> 16) & 255) * w11) >> 16;
            int cg = (((c00 >> 8) & 255) * w00 + ((c10 >> 8) & 255) * w10 + ((c01 >> 8) & 255) * w01 + ((c11 >> 8) & 255) * w11) >> 16;
            int cb = ((c00 & 255) * w00 + (c10 & 255) * w10 + (c01 & 255) * w01 + (c11 & 255) * w11) >> 16;
            return (ca << 24) | (cr << 16) | (cg << 8) | cb;
        }

        static float SmoothStep(float x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x * x * (3 - 2 * x);
        }

        // Pre-scale everything once for the current DPI, and place the rig from the eye/mouth zones.
        void EnsureScaled(float S)
        {
            if (scaledFor == S) return;
            scaledFor = S;
            k = targetHeight * S / source.Height;
            margin = (int)(12 * S);
            scaledBase = Resize(source, k);
            foreach (Patch p in patches.Values) p.Scaled = Resize(p.Image, k);
            // Margin on the top and sides gives the warp room to move; the bottom edge is the cut-off body.
            int w = scaledBase.Width + margin * 2, h = scaledBase.Height + margin;
            frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            output = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            srcPx = new int[w * h];
            dstPx = new int[w * h];
            rowMin = new int[h];
            rowMax = new int[h];
            composedKey = null;

            if (!eyesZone.IsEmpty && !mouthZone.IsEmpty)
            {
                float ex = eyesZone.X + eyesZone.Width / 2, ey = eyesZone.Y + eyesZone.Height / 2;
                float mx = mouthZone.X + mouthZone.Width / 2, my = mouthZone.Y + mouthZone.Height / 2;
                float span = my - ey; // eyes-to-mouth distance sets the scale of the face
                faceX = margin + (ex + mx) / 2 * k;
                faceY = margin + (ey + span * 0.4f) * k;
                faceR = Math.Max(eyesZone.Width * 0.7f, span * 2.2f) * k;
                neckY = margin + (mouthZone.Bottom + span * 1.1f) * k;
            }
            else
            {
                faceX = w / 2f;
                faceY = margin + scaledBase.Height * 0.25f;
                faceR = scaledBase.Width * 0.2f;
                neckY = margin + scaledBase.Height * 0.38f;
            }
            neckBand = faceR * 0.35f;
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
