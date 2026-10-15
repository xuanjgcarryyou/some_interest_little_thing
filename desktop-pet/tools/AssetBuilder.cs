// Turns AI-generated character art into what SpriteCharacter needs: a background-free base image
// plus small expression patches (eyes / mouth) found by diffing each variant against the base.
// Usage: AssetBuilder.exe <rawDir> <outDir>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.Tools
{
    class Img
    {
        public readonly int W, H;
        public readonly int[] P; // ARGB

        public Img(int w, int h) { W = w; H = h; P = new int[w * h]; }

        public static Img Load(string path)
        {
            using (Bitmap src = new Bitmap(path))
                return FromBitmap(src, src.Width, src.Height);
        }

        public static Img FromBitmap(Bitmap src, int w, int h)
        {
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, new Rectangle(0, 0, w, h));
                }
                Img img = new Img(w, h);
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(d.Scan0, img.P, 0, img.P.Length);
                bmp.UnlockBits(d);
                return img;
            }
        }

        public Bitmap ToBitmap()
        {
            Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(P, 0, d.Scan0, P.Length);
            bmp.UnlockBits(d);
            return bmp;
        }

        public static int A(int c) { return (c >> 24) & 255; }
        public static int R(int c) { return (c >> 16) & 255; }
        public static int G(int c) { return (c >> 8) & 255; }
        public static int B(int c) { return c & 255; }
        public static int Argb(int a, int r, int g, int b) { return (a << 24) | (r << 16) | (g << 8) | b; }
        public static int Dist(int c1, int c2) { return Math.Abs(R(c1) - R(c2)) + Math.Abs(G(c1) - G(c2)) + Math.Abs(B(c1) - B(c2)); }
        public static int Lum(int c) { return (R(c) * 3 + G(c) * 6 + B(c)) / 10; }
    }

    static class AssetBuilder
    {
        const int StoredHeight = 640;  // the base image is stored at most this tall
        const int Block = 6;           // diff grid cell size, px
        const int PixelThreshold = 60; // summed RGB difference that counts as "changed"

        static readonly string[] Variants = { "eyes_closed", "mouth_small", "mouth_a", "mouth_o", "face_happy", "face_surprised" };

        class Part
        {
            public string Name;
            public Img Source;
            public Rectangle Rect;
        }

        static int Main(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("usage: AssetBuilder <rawDir> <outDir>"); return 2; }
            string raw = args[0], outDir = args[1];
            string basePath = Path.Combine(raw, "base.png");
            if (!File.Exists(basePath)) { Console.Error.WriteLine("missing " + basePath); return 1; }
            Directory.CreateDirectory(outDir);

            Img baseImg = Img.Load(basePath);
            byte[] alpha = RemoveBackground(baseImg);
            Rectangle body = Bounds(alpha, baseImg.W, baseImg.H);
            if (body.IsEmpty) { Console.Error.WriteLine("base.png: could not find the character (background removal left nothing)"); return 1; }
            Console.WriteLine("base.png: {0}x{1}, character at {2}", baseImg.W, baseImg.H, body);

            Dictionary<string, List<Rectangle>> comps = new Dictionary<string, List<Rectangle>>();
            Dictionary<string, Img> aligned = new Dictionary<string, Img>();
            int warnings = 0;
            foreach (string name in Variants)
            {
                string path = Path.Combine(raw, name + ".png");
                if (!File.Exists(path)) { Console.WriteLine("{0}.png: missing, skipped", name); continue; }
                Img v = Img.Load(path);
                if (v.W != baseImg.W || v.H != baseImg.H)
                {
                    Console.WriteLine("{0}.png: size {1}x{2} differs from base, resampling", name, v.W, v.H);
                    using (Bitmap b = v.ToBitmap()) v = Img.FromBitmap(b, baseImg.W, baseImg.H);
                }
                float scale, dx, dy;
                Align(baseImg, v, alpha, body, out scale, out dx, out dy);
                Img av = Warp(v, scale, dx, dy);
                float changed;
                int noise;
                List<Rectangle> found = DiffComponents(baseImg, av, alpha, body, out changed, out noise);
                Console.WriteLine("{0}.png: scale {1:0.###}, offset ({2:0.#},{3:0.#}), {4} changed region(s) (+{5} ignored as noise), {6:P1} of the character changed",
                    name, scale, dx, dy, found.Count, noise, changed);
                if (changed > 0.15f)
                {
                    Console.WriteLine("  WARNING: too much changed — this doesn't look like an edit of base.png. Regenerate it as an edit of the base image.");
                    warnings++;
                    continue;
                }
                if (found.Count == 0) { Console.WriteLine("  WARNING: nothing changed?"); warnings++; continue; }
                comps[name] = found;
                aligned[name] = av;
            }

            // The mouth zone tells the face variants' mouth changes apart from their eye changes.
            Rectangle mouthZone = Rectangle.Empty;
            foreach (string name in new[] { "mouth_small", "mouth_a", "mouth_o" })
                if (comps.ContainsKey(name)) mouthZone = UnionOrFirst(mouthZone, Union(comps[name]));
            if (mouthZone.IsEmpty && comps.ContainsKey("face_happy"))
            {
                foreach (Rectangle r in comps["face_happy"])
                    if (mouthZone.IsEmpty || r.Bottom > mouthZone.Bottom) mouthZone = r; // lowest change = mouth
            }

            List<Part> parts = new List<Part>();
            AddPart(parts, "eyes_closed", aligned, comps, "eyes_closed", null, Rectangle.Empty);
            foreach (string name in new[] { "mouth_small", "mouth_a", "mouth_o" })
                AddPart(parts, name, aligned, comps, name, null, Rectangle.Empty);
            Rectangle zone = Inflate(mouthZone, mouthZone.Width / 4, mouthZone.Height / 4);
            AddPart(parts, "happy_eyes", aligned, comps, "face_happy", false, zone);
            AddPart(parts, "happy_mouth", aligned, comps, "face_happy", true, zone);
            AddPart(parts, "surprised_eyes", aligned, comps, "face_surprised", false, zone);
            AddPart(parts, "surprised_mouth", aligned, comps, "face_surprised", true, zone);

            Write(outDir, baseImg, alpha, body, parts, mouthZone);
            Console.WriteLine(warnings == 0 ? "OK" : warnings + " warning(s)");
            return 0;
        }

        // ---------- background ----------

        // Makes the background transparent and returns the per-pixel alpha.
        // Uses the image's own transparency if it has one, otherwise flood-fills the flat color from the edges.
        static byte[] RemoveBackground(Img img)
        {
            int n = img.W * img.H;
            byte[] a = new byte[n];
            int border = 0, clear = 0;
            Dictionary<int, int> colors = new Dictionary<int, int>();
            ForBorder(img, delegate(int i)
            {
                border++;
                if (Img.A(img.P[i]) < 128) clear++;
                int q = img.P[i] & 0xF0F0F0;
                int c;
                colors.TryGetValue(q, out c);
                colors[q] = c + 1;
            });
            if (clear > border / 2)
            {
                for (int i = 0; i < n; i++) a[i] = (byte)Img.A(img.P[i]);
                Console.WriteLine("background: using the image's transparency");
                return a;
            }

            int bgKey = 0, best = -1;
            foreach (KeyValuePair<int, int> kv in colors) if (kv.Value > best) { best = kv.Value; bgKey = kv.Key; }
            long sr = 0, sg = 0, sb = 0, cnt = 0;
            ForBorder(img, delegate(int i)
            {
                if ((img.P[i] & 0xF0F0F0) != bgKey) return;
                sr += Img.R(img.P[i]); sg += Img.G(img.P[i]); sb += Img.B(img.P[i]); cnt++;
            });
            int bg = Img.Argb(255, (int)(sr / cnt), (int)(sg / cnt), (int)(sb / cnt));
            bool green = Img.G(bg) > 120 && Img.G(bg) > Img.R(bg) + 60 && Img.G(bg) > Img.B(bg) + 60;
            Console.WriteLine("background: flat color #{0:X6}{1}", bg & 0xFFFFFF, green ? " (green screen)" : "");

            bool[] isBg = new bool[n];
            Queue<int> queue = new Queue<int>();
            if (green)
            {
                // The character has no green, so green anywhere is background — including the gaps
                // enclosed by hair and arms that a flood fill from the edges can't reach.
                for (int i = 0; i < n; i++) isBg[i] = IsBackground(img.P[i], bg, true);
            }
            else ForBorder(img, delegate(int i)
            {
                if (!isBg[i] && IsBackground(img.P[i], bg, green)) { isBg[i] = true; queue.Enqueue(i); }
            });
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % img.W, y = i / img.W;
                if (x > 0) Visit(img, isBg, queue, i - 1, bg, green);
                if (x < img.W - 1) Visit(img, isBg, queue, i + 1, bg, green);
                if (y > 0) Visit(img, isBg, queue, i - img.W, bg, green);
                if (y < img.H - 1) Visit(img, isBg, queue, i + img.W, bg, green);
            }

            for (int i = 0; i < n; i++)
            {
                int c = img.P[i];
                if (isBg[i]) { a[i] = 0; img.P[i] = 0; continue; }
                int r = Img.R(c), g = Img.G(c), b = Img.B(c);
                byte alpha = 255;
                if (NextToBackground(img, isBg, i))
                {
                    // Soft edge: the more it looks like the background, the more transparent it gets.
                    float t = Math.Min(1f, Img.Dist(c, bg) / 200f);
                    alpha = (byte)(255 * t);
                    if (green) g = Math.Min(g, Math.Max(r, b)); // remove green spill
                }
                a[i] = alpha;
                img.P[i] = Img.Argb(alpha, r, g, b);
            }
            return a;
        }

        static bool IsBackground(int c, int bg, bool green)
        {
            if (Img.Dist(c, bg) < 90) return true;
            return green && Img.G(c) > 100 && Img.G(c) > Img.R(c) + 50 && Img.G(c) > Img.B(c) + 50;
        }

        static void Visit(Img img, bool[] isBg, Queue<int> q, int i, int bg, bool green)
        {
            if (isBg[i] || !IsBackground(img.P[i], bg, green)) return;
            isBg[i] = true;
            q.Enqueue(i);
        }

        static bool NextToBackground(Img img, bool[] isBg, int i)
        {
            int x = i % img.W, y = i / img.W;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < img.W && ny < img.H && isBg[ny * img.W + nx]) return true;
                }
            return false;
        }

        static void ForBorder(Img img, Action<int> f)
        {
            for (int x = 0; x < img.W; x++) { f(x); f((img.H - 1) * img.W + x); }
            for (int y = 1; y < img.H - 1; y++) { f(y * img.W); f(y * img.W + img.W - 1); }
        }

        static Rectangle Bounds(byte[] alpha, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (alpha[y * w + x] >= 128)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        // ---------- alignment & diff ----------

        // Image editors often redraw the picture slightly shifted or zoomed. Find the scale (about the image
        // center) and offset that map base pixels onto the variant: variant(x') = base(x) with
        // x' = center + (x - center) * scale + offset. Coarse search at quarter resolution, then refine.
        static void Align(Img b, Img v, byte[] alpha, Rectangle body, out float bestS, out float bestDx, out float bestDy)
        {
            const int Q = 4;
            int qw = b.W / Q, qh = b.H / Q;
            int[] lb = Downsample(b, Q), lv = Downsample(v, Q);
            List<int> samples = new List<int>();
            for (int y = body.Top / Q; y < body.Bottom / Q; y += 2)
                for (int x = body.Left / Q; x < body.Right / Q; x += 2)
                    if (alpha[(y * Q + Q / 2) * b.W + x * Q + Q / 2] >= 128) samples.Add(y * qw + x);

            bestS = 1f; bestDx = 0; bestDy = 0;
            long best = long.MaxValue;
            for (int si = -15; si <= 15; si++)
            {
                float s = 1f + si * 0.004f;
                for (int dy = -12; dy <= 12; dy++)
                    for (int dx = -12; dx <= 12; dx++)
                    {
                        long sum = Score(lb, lv, qw, qh, samples, s, dx, dy, best);
                        if (sum < best) { best = sum; bestS = s; bestDx = dx * Q; bestDy = dy * Q; }
                    }
            }

            int[] fb = Downsample(b, 1), fv = Downsample(v, 1);
            samples.Clear();
            for (int y = body.Top; y < body.Bottom; y += 4)
                for (int x = body.Left; x < body.Right; x += 4)
                    if (alpha[y * b.W + x] >= 128) samples.Add(y * b.W + x);
            float cs = bestS, cdx = bestDx, cdy = bestDy;
            best = long.MaxValue;
            for (int si = -5; si <= 5; si++)
                for (int dy = -5; dy <= 5; dy++)
                    for (int dx = -5; dx <= 5; dx++)
                    {
                        float s = cs + si * 0.001f;
                        long sum = Score(fb, fv, b.W, b.H, samples, s, cdx + dx, cdy + dy, best);
                        if (sum < best) { best = sum; bestS = s; bestDx = cdx + dx; bestDy = cdy + dy; }
                    }
        }

        static long Score(int[] lb, int[] lv, int w, int h, List<int> samples, float s, float dx, float dy, long giveUpAt)
        {
            float cx = w / 2f, cy = h / 2f;
            long sum = 0;
            foreach (int i in samples)
            {
                int x = i % w, y = i / w;
                int vx = (int)(cx + (x - cx) * s + dx + 0.5f), vy = (int)(cy + (y - cy) * s + dy + 0.5f);
                sum += (vx < 0 || vy < 0 || vx >= w || vy >= h) ? 255 : Math.Abs(lb[i] - lv[vy * w + vx]);
                if (sum >= giveUpAt) return sum;
            }
            return sum;
        }

        static int[] Downsample(Img img, int q)
        {
            int w = img.W / q, h = img.H / q;
            int[] o = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sum = 0;
                    for (int yy = 0; yy < q; yy++)
                        for (int xx = 0; xx < q; xx++) sum += Img.Lum(img.P[(y * q + yy) * img.W + x * q + xx]);
                    o[y * w + x] = sum / (q * q);
                }
            return o;
        }

        // Resamples the variant into the base's frame (bilinear), undoing the scale/offset found by Align.
        static Img Warp(Img v, float s, float dx, float dy)
        {
            Img o = new Img(v.W, v.H);
            float cx = v.W / 2f, cy = v.H / 2f;
            for (int y = 0; y < v.H; y++)
                for (int x = 0; x < v.W; x++)
                {
                    float fx = cx + (x - cx) * s + dx, fy = cy + (y - cy) * s + dy;
                    int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                    if (x0 < 0 || y0 < 0 || x0 + 1 >= v.W || y0 + 1 >= v.H) continue;
                    float tx = fx - x0, ty = fy - y0;
                    int c00 = v.P[y0 * v.W + x0], c10 = v.P[y0 * v.W + x0 + 1], c01 = v.P[(y0 + 1) * v.W + x0], c11 = v.P[(y0 + 1) * v.W + x0 + 1];
                    o.P[y * v.W + x] = Img.Argb(255,
                        Lerp2(Img.R(c00), Img.R(c10), Img.R(c01), Img.R(c11), tx, ty),
                        Lerp2(Img.G(c00), Img.G(c10), Img.G(c01), Img.G(c11), tx, ty),
                        Lerp2(Img.B(c00), Img.B(c10), Img.B(c01), Img.B(c11), tx, ty));
                }
            return o;
        }

        static int Lerp2(int a, int b, int c, int d, float tx, float ty)
        {
            return (int)(a * (1 - tx) * (1 - ty) + b * tx * (1 - ty) + c * (1 - tx) * ty + d * tx * ty + 0.5f);
        }

        // Groups the changed pixels into blobs (e.g. two eyes + a mouth) and returns their bounding boxes.
        // AI edits also redraw tiny bits everywhere (hair strands, shading); blobs much smaller than the
        // biggest one are treated as that noise and dropped.
        static List<Rectangle> DiffComponents(Img b, Img v, byte[] alpha, Rectangle body, out float changedRatio, out int noise)
        {
            // A redrawn image differs along every line by a sub-pixel; a real edit (closed eye, open mouth)
            // is a solid patch. Eroding the change mask removes the thin lines and keeps the patches.
            bool[] diff = new bool[b.W * b.H];
            for (int y = body.Top; y < body.Bottom; y++)
                for (int x = body.Left; x < body.Right; x++)
                {
                    int i = y * b.W + x;
                    diff[i] = alpha[i] >= 128 && Img.Dist(b.P[i], v.P[i]) > PixelThreshold;
                }
            bool[] solid = Erode(diff, b.W, b.H, body, 2);

            int gw = (b.W + Block - 1) / Block, gh = (b.H + Block - 1) / Block;
            int[] changed = new int[gw * gh], total = new int[gw * gh];
            long changedPx = 0, bodyPx = 0;
            for (int y = body.Top; y < body.Bottom; y++)
                for (int x = body.Left; x < body.Right; x++)
                {
                    int i = y * b.W + x;
                    if (alpha[i] < 128) continue;
                    int cell = (y / Block) * gw + x / Block;
                    total[cell]++;
                    bodyPx++;
                    if (solid[i]) { changed[cell]++; changedPx++; }
                }
            changedRatio = bodyPx == 0 ? 0 : (float)changedPx / bodyPx;

            bool[] on = new bool[gw * gh];
            for (int c = 0; c < on.Length; c++) on[c] = changed[c] >= 4 && changed[c] * 5 >= total[c];

            // Connected components, treating cells one apart as touching so an eye and its lashes stay together.
            int[] label = new int[gw * gh];
            List<Rectangle> rects = new List<Rectangle>();
            List<int> masses = new List<int>();
            int next = 0;
            for (int start = 0; start < on.Length; start++)
            {
                if (!on[start] || label[start] != 0) continue;
                next++;
                int x0 = gw, y0 = gh, x1 = -1, y1 = -1, count = 0, mass = 0;
                Stack<int> stack = new Stack<int>();
                stack.Push(start);
                label[start] = next;
                while (stack.Count > 0)
                {
                    int c = stack.Pop(), cx = c % gw, cy = c / gw;
                    count++;
                    mass += changed[c];
                    x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx); y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                            int n = ny * gw + nx;
                            if (on[n] && label[n] == 0) { label[n] = next; stack.Push(n); }
                        }
                }
                if (count < 2) continue; // a stray cell is noise
                Rectangle r = Rectangle.FromLTRB(x0 * Block - 4, y0 * Block - 4, (x1 + 1) * Block + 4, (y1 + 1) * Block + 4);
                r.Intersect(new Rectangle(0, 0, b.W, b.H));
                rects.Add(r);
                masses.Add(mass);
            }

            int biggest = 0;
            foreach (int m in masses) biggest = Math.Max(biggest, m);
            List<Rectangle> result = new List<Rectangle>();
            for (int i = 0; i < rects.Count; i++)
                if (masses[i] * 5 >= biggest) result.Add(rects[i]);
            noise = rects.Count - result.Count;
            return result;
        }

        // A pixel survives only if its whole (2r+1)x(2r+1) neighborhood is set. Separable: rows, then columns.
        static bool[] Erode(bool[] m, int w, int h, Rectangle area, int r)
        {
            bool[] rows = new bool[m.Length], o = new bool[m.Length];
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int run = 0;
                for (int x = area.Left; x < area.Right; x++)
                {
                    run = m[y * w + x] ? run + 1 : 0;
                    if (run >= 2 * r + 1) rows[y * w + x - r] = true;
                }
            }
            for (int x = area.Left; x < area.Right; x++)
            {
                int run = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    run = rows[y * w + x] ? run + 1 : 0;
                    if (run >= 2 * r + 1) o[(y - r) * w + x] = true;
                }
            }
            return o;
        }

        // ---------- output ----------

        // mouthSide: null = all regions, true = only regions in the mouth zone, false = only regions outside it.
        static void AddPart(List<Part> parts, string name, Dictionary<string, Img> aligned, Dictionary<string, List<Rectangle>> comps,
            string variant, bool? mouthSide, Rectangle zone)
        {
            if (!comps.ContainsKey(variant)) return;
            List<Rectangle> picked = new List<Rectangle>();
            foreach (Rectangle r in comps[variant])
            {
                // Chibi eyes sit close to the mouth, so judge each region by its center, not its edges.
                bool inMouth = zone.Contains(r.X + r.Width / 2, r.Y + r.Height / 2);
                if (mouthSide == null || zone.IsEmpty || inMouth == mouthSide.Value) picked.Add(r);
            }
            if (picked.Count == 0) return;
            Part p = new Part();
            p.Name = name;
            p.Source = aligned[variant];
            p.Rect = Union(picked);
            parts.Add(p);
        }

        static void Write(string outDir, Img baseImg, byte[] alpha, Rectangle body, List<Part> parts, Rectangle mouthZone)
        {
            Rectangle crop = Inflate(body, 6, 6);
            crop.Intersect(new Rectangle(0, 0, baseImg.W, baseImg.H));
            float f = crop.Height > StoredHeight ? (float)StoredHeight / crop.Height : 1f;

            StringBuilder manifest = new StringBuilder();
            using (Bitmap full = baseImg.ToBitmap())
                SaveScaled(full, crop, f, Path.Combine(outDir, "base.png"));
            manifest.AppendLine("base base.png");

            foreach (Part p in parts)
            {
                using (Bitmap patch = PatchBitmap(baseImg, p.Source, alpha, p.Rect))
                    SaveScaled(patch, new Rectangle(0, 0, patch.Width, patch.Height), f, Path.Combine(outDir, p.Name + ".png"));
                manifest.AppendLine(string.Format(CultureInfo.InvariantCulture, "patch {0} {0}.png {1:0.##} {2:0.##}",
                    p.Name, (p.Rect.X - crop.X) * f, (p.Rect.Y - crop.Y) * f));
                Console.WriteLine("  part {0}: {1}x{2} at ({3},{4})", p.Name, p.Rect.Width, p.Rect.Height, p.Rect.X, p.Rect.Y);
            }
            if (!mouthZone.IsEmpty)
                manifest.AppendLine(string.Format(CultureInfo.InvariantCulture, "zone mouth {0:0.##} {1:0.##} {2:0.##} {3:0.##}",
                    (mouthZone.X - crop.X) * f, (mouthZone.Y - crop.Y) * f, mouthZone.Width * f, mouthZone.Height * f));
            File.WriteAllText(Path.Combine(outDir, "manifest.txt"), manifest.ToString());

            WritePreview(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outDir)), "preview.png"), outDir, parts);
        }

        // Only the pixels that actually changed (grown a little and feathered) so the patch blends into the base.
        static Bitmap PatchBitmap(Img baseImg, Img variant, byte[] alpha, Rectangle r)
        {
            float[] m = new float[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                {
                    int i = (r.Y + y) * baseImg.W + r.X + x;
                    if (Img.Dist(baseImg.P[i], variant.P[i]) > 25) m[y * r.Width + x] = 1f;
                }
            m = Blur(Grow(m, r.Width, r.Height, 2), r.Width, r.Height);

            Img patch = new Img(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                {
                    int i = (r.Y + y) * baseImg.W + r.X + x, c = variant.P[i];
                    int a = (int)(alpha[i] * m[y * r.Width + x]);
                    patch.P[y * r.Width + x] = Img.Argb(a, Img.R(c), Img.G(c), Img.B(c));
                }
            return patch.ToBitmap();
        }

        static float[] Grow(float[] m, int w, int h, int radius)
        {
            float[] o = new float[m.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (m[y * w + x] == 0) continue;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h) o[ny * w + nx] = 1f;
                        }
                }
            return o;
        }

        static float[] Blur(float[] m, int w, int h)
        {
            float[] o = new float[m.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h) { sum += m[ny * w + nx]; n++; }
                        }
                    o[y * w + x] = sum / n;
                }
            return o;
        }

        static void SaveScaled(Bitmap src, Rectangle from, float f, string path)
        {
            int w = Math.Max(1, (int)Math.Round(from.Width * f)), h = Math.Max(1, (int)Math.Round(from.Height * f));
            using (Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, w, h), from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel, ia);
                }
                dst.Save(path, ImageFormat.Png);
            }
        }

        // A contact sheet: the base, then the base with each patch applied, for eyeballing the result.
        static void WritePreview(string path, string outDir, List<Part> parts)
        {
            string manifest = File.ReadAllText(Path.Combine(outDir, "manifest.txt"));
            using (Bitmap b = new Bitmap(Path.Combine(outDir, "base.png")))
            {
                int cellW = b.Width / 2, cellH = b.Height / 2, cols = parts.Count + 1;
                using (Bitmap sheet = new Bitmap(cellW * cols, cellH + 20))
                using (Graphics g = Graphics.FromImage(sheet))
                using (Font font = new Font("Segoe UI", 9))
                {
                    g.Clear(Color.FromArgb(225, 225, 230));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    for (int c = 0; c < cols; c++)
                    {
                        string name = c == 0 ? "base" : parts[c - 1].Name;
                        using (Bitmap cell = new Bitmap(b.Width, b.Height))
                        {
                            using (Graphics cg = Graphics.FromImage(cell))
                            {
                                cg.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height));
                                if (c > 0)
                                {
                                    foreach (string line in manifest.Split('\n'))
                                    {
                                        string[] f = line.Trim().Split(' ');
                                        if (f[0] != "patch" || f[1] != name) continue;
                                        using (Bitmap patch = new Bitmap(Path.Combine(outDir, f[2])))
                                            cg.DrawImage(patch, new RectangleF(float.Parse(f[3], CultureInfo.InvariantCulture),
                                                float.Parse(f[4], CultureInfo.InvariantCulture), patch.Width, patch.Height));
                                    }
                                }
                            }
                            g.DrawImage(cell, new Rectangle(c * cellW, 20, cellW, cellH));
                        }
                        g.DrawString(name, font, Brushes.Black, c * cellW + 4, 2);
                    }
                    sheet.Save(path, ImageFormat.Png);
                }
            }
            Console.WriteLine("preview: " + path);
        }

        // ---------- rect helpers ----------

        static Rectangle Union(List<Rectangle> rs)
        {
            Rectangle u = Rectangle.Empty;
            foreach (Rectangle r in rs) u = UnionOrFirst(u, r);
            return u;
        }

        static Rectangle UnionOrFirst(Rectangle a, Rectangle b) { return a.IsEmpty ? b : Rectangle.Union(a, b); }

        static Rectangle Inflate(Rectangle r, int dx, int dy)
        {
            if (r.IsEmpty) return r;
            r.Inflate(dx, dy);
            return r;
        }
    }
}
