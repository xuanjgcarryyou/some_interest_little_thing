using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopPet
{
    // 魔人普烏 (chibi fan art, drawn entirely in code). Form 1 = 胖普烏, form 2 = 超級普烏.
    class Buu : Character
    {
        static readonly Color Pink = Color.FromArgb(255, 160, 196);
        static readonly Color Vest = Color.FromArgb(78, 48, 104);
        static readonly Color Gold = Color.FromArgb(242, 192, 64);
        static readonly Color Black = Color.FromArgb(44, 38, 48);
        static readonly Color Cloth = Color.FromArgb(252, 252, 250);
        static readonly Color EyeRed = Color.FromArgb(214, 38, 64);
        static readonly Color MouthColor = Color.FromArgb(170, 50, 70);

        static readonly PetLines FatScript = new PetLines
        {
            Greeting = "普烏！普烏是魔人普烏！",
            Talk = new[] { "普烏～", "好無聊，來玩嘛！", "普烏最強！", "想吃巧克力～", "嘿嘿嘿～", "你是好人！普烏喜歡你", "普烏要睡午覺～" },
            Click = new[] { "嘿嘿！好癢！", "普烏開心！", "再一次！再一次！", "普烏～" },
            Drag = new[] { "哇！普烏飛起來了！", "放開普烏！", "普烏要生氣囉！" },
            Hungry = new[] { "普烏肚子餓了…", "糖果…想吃糖果…", "給普烏吃點心！" },
            FoodMenu = "餵食糖果",
            Eating = "嚼嚼…好甜！",
            Ate = "糖果最棒了！",
            Full = "普烏吃飽了～",
            Hurt = "好痛！普烏生氣了！",
            Sleepy = "普烏要睡覺了…",
            Wake = "普烏醒了！",
            Annoyed = "普烏…要生氣囉…",
            TransformShout = "普烏…生氣了！！！",
            Transformed = "哼哼…超級普烏，登場。",
            TransformMenu = "變身！",
        };

        static readonly PetLines SuperScript = new PetLines
        {
            Greeting = "哼，超級普烏在此。",
            Talk = new[] { "哼，無聊。", "誰是下一個獵物？", "我是最強的。", "把你變成巧克力吧。", "嘿嘿嘿…", "這個世界真脆弱。" },
            Click = new[] { "別碰我。", "哼。", "想被變成糖果嗎？", "…還不錯。" },
            Drag = new[] { "放我下來，人類！", "你膽子不小。" },
            Hungry = new[] { "肚子餓了…把你變成糖果吃掉吧。", "我要甜食。" },
            FoodMenu = "餵食糖果（會變回來）",
            Eating = "…嗯，還算可以。",
            Ate = "…好吃。",
            Full = "不需要了。",
            Hurt = "哼，這點程度。",
            Sleepy = "…睡一下。",
            Wake = "誰吵醒我？",
            TransformShout = "呼…好甜…",
            Transformed = "普烏變回來了！普烏～",
            TransformMenu = "變回胖普烏",
        };

        int form = 1;

        public override string Id { get { return "buu"; } }
        public override string Name { get { return "魔人普烏"; } }
        public override PetLines Lines { get { return form == 1 ? FatScript : SuperScript; } }
        public override bool CanTransform { get { return true; } }
        public override int Form { get { return form; } set { form = value == 2 ? 2 : 1; } }
        public override float SpeedFactor { get { return form == 1 ? 0.8f : 1.6f; } }
        public override bool AlwaysSteaming { get { return form == 2; } }
        public override float ShadowWidth { get { return form == 1 ? 90f : 66f; } }
        public override float HeadHeight(float S) { return 126 * S; }

        public override PointF[] SteamVents(float S)
        {
            if (form == 1) return new[] { new PointF(-18 * S, -82 * S), new PointF(18 * S, -82 * S) };
            return new[]
            {
                new PointF(-13 * S, -98 * S), new PointF(13 * S, -98 * S),
                new PointF(-24 * S, -78 * S), new PointF(24 * S, -78 * S)
            };
        }

        public override void Draw(Graphics g, PetView v)
        {
            if (form == 1) DrawFat(g, v); else DrawSuper(g, v);
        }

        // ---------- form 1: 胖普烏 ----------

        void DrawFat(Graphics g, PetView v)
        {
            float S = v.S, x = v.X, y = v.Y;
            float sq = Breathe(v);
            float bw = 88 * S * sq, bh = 58 * S / sq;
            float bodyBottom = y - 20 * S, top = bodyBottom - bh;
            float lift = StepLift(v, S);
            int back = -Facing(v); // antenna and cape trail behind

            using (Pen o = Ink.OutlinePen(S))
            using (Pen thin = Ink.OutlinePen(S, 2f))
            using (SolidBrush pink = new SolidBrush(Pink))
            using (SolidBrush cloth = new SolidBrush(Cloth))
            using (SolidBrush gold = new SolidBrush(Gold))
            using (SolidBrush black = new SolidBrush(Black))
            {
                // Cape, behind everything.
                float flutter = (float)Math.Sin(v.Tick * 0.12) * 3 * S + (v.State == PetState.Walk ? back * 6 * S : 0f);
                using (GraphicsPath cape = new GraphicsPath())
                {
                    PointF a = new PointF(x - bw * 0.32f, top + 8 * S), b = new PointF(x + bw * 0.32f, top + 8 * S);
                    PointF c = new PointF(x + bw * 0.62f + flutter, y - 8 * S), d = new PointF(x - bw * 0.62f + flutter, y - 8 * S);
                    cape.AddLine(a, b);
                    cape.AddLine(b, c);
                    cape.AddBezier(c, new PointF(x + bw * 0.2f + flutter, y - 2 * S), new PointF(x - bw * 0.2f + flutter, y - 14 * S), d);
                    cape.CloseFigure();
                    g.FillPath(cloth, cape);
                    g.DrawPath(o, cape);
                }

                // Puffy trousers + boots.
                for (int i = -1; i <= 1; i += 2)
                {
                    float l = i < 0 ? Math.Max(lift, 0) : Math.Max(-lift, 0);
                    RectangleF pants = new RectangleF(x + i * 17 * S - 17 * S, y - 32 * S - l, 34 * S, 26 * S);
                    g.FillEllipse(cloth, pants);
                    g.DrawEllipse(o, pants);
                    using (GraphicsPath boot = Ink.RoundedRect(x + i * 17 * S - 11 * S, y - 10 * S - l, 22 * S, 10 * S, 4 * S))
                    {
                        g.FillPath(gold, boot);
                        g.DrawPath(o, boot);
                    }
                }

                // Round body: open vest with gold trim, belt.
                RectangleF br = new RectangleF(x - bw / 2, top, bw, bh);
                using (GraphicsPath body = new GraphicsPath())
                {
                    body.AddEllipse(br);
                    using (SolidBrush vest = new SolidBrush(Vest)) g.FillPath(vest, body);
                    GraphicsState clip = g.Save();
                    g.SetClip(body, CombineMode.Intersect);
                    RectangleF belly = new RectangleF(x - bw * 0.27f, top + bh * 0.1f, bw * 0.54f, bh * 1.05f);
                    g.FillEllipse(pink, belly);
                    using (Pen trim = new Pen(Gold, 3 * S)) g.DrawEllipse(trim, belly);
                    g.FillRectangle(black, x - bw / 2, bodyBottom - bh * 0.26f, bw, bh * 0.16f);
                    g.Restore(clip);
                    g.DrawPath(o, body);
                }
                DrawBuckle(g, thin, gold, x, bodyBottom - bh * 0.18f, S);

                // Arms with white gloves and gold cuffs.
                for (int i = -1; i <= 1; i += 2)
                {
                    PointF sh = new PointF(x + i * bw * 0.40f, top + bh * 0.30f);
                    PointF hand;
                    switch (v.State)
                    {
                        case PetState.Happy: hand = new PointF(x + i * bw * 0.62f, top - 6 * S); break;
                        case PetState.Drag: hand = new PointF(x + i * bw * 0.62f, top - 4 * S + (float)Math.Sin(v.Tick * 0.6 + i) * 6 * S); break;
                        case PetState.Transform: hand = new PointF(x + i * bw * 0.58f, top + bh * 0.25f); break;
                        default: hand = new PointF(x + i * bw * 0.60f, top + bh * 0.66f + (v.State == PetState.Walk ? i * lift : 0f)); break;
                    }
                    Ink.Limb(g, S, Pink, sh, hand, 11 * S);
                    Ink.Ball(g, o, gold, Ink.Lerp(sh, hand, 0.72f), 7 * S);
                    Ink.Ball(g, o, cloth, hand, 7.5f * S);
                }

                // Head with the antenna and steam holes.
                float hd = 48 * S, hcx = x, hcy = top - 4 * S;
                float wig = (float)Math.Sin(v.Tick * 0.15) * 2 * S;
                Ink.Tentacle(g, S, Pink,
                    new PointF(hcx, hcy - hd * 0.25f),
                    new PointF(hcx + back * 2 * S, hcy - hd * 0.95f),
                    new PointF(hcx + back * 20 * S + wig, hcy - hd * 1.1f),
                    new PointF(hcx + back * 24 * S + wig, hcy - hd * 0.62f), 8 * S);
                RectangleF head = new RectangleF(hcx - hd / 2, hcy - hd * 0.48f, hd, hd * 0.9f);
                g.FillEllipse(pink, head);
                g.DrawEllipse(o, head);
                for (int i = -1; i <= 1; i += 2) g.FillEllipse(black, hcx + i * hd * 0.38f - 2 * S, hcy - 2 * S, 4 * S, 5 * S);

                DrawFatFace(g, o, thin, v, hcx + (v.State == PetState.Walk ? Facing(v) * 2 * S : 0f), hcy + hd * 0.02f);
                if (v.State == PetState.Eat) Ink.Candy(g, hcx + Facing(v) * hd * 0.62f, hcy + hd * 0.3f, Bites(v), S);
            }
        }

        static void DrawBuckle(Graphics g, Pen thin, Brush gold, float x, float y, float S)
        {
            using (GraphicsPath buckle = Ink.RoundedRect(x - 8 * S, y - 7 * S, 16 * S, 13 * S, 3 * S))
            {
                g.FillPath(gold, buckle);
                g.DrawPath(thin, buckle);
            }
            using (Pen m = new Pen(Black, 2f * S))
            {
                m.LineJoin = LineJoin.Round;
                g.DrawLines(m, new[]
                {
                    new PointF(x - 5 * S, y + 3.5f * S), new PointF(x - 5 * S, y - 4 * S), new PointF(x, y + 1 * S),
                    new PointF(x + 5 * S, y - 4 * S), new PointF(x + 5 * S, y + 3.5f * S)
                });
            }
        }

        void DrawFatFace(Graphics g, Pen o, Pen thin, PetView v, float x, float y)
        {
            float S = v.S, ex = 8 * S;
            using (SolidBrush dark = new SolidBrush(Black))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush inside = new SolidBrush(MouthColor))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float ecx = x + i * ex;
                    switch (v.State)
                    {
                        case PetState.Sleep: Ink.SleepEye(g, o, ecx, y, S); break;
                        case PetState.Happy: Ink.HappyEye(g, o, ecx, y, S); break;
                        case PetState.Drag: Ink.SquintEye(g, o, ecx, y, S, i); break;
                        default:
                            if (v.Blink)
                                g.DrawLine(o, ecx - 4.5f * S, y, ecx + 4.5f * S, y);
                            else
                            {
                                g.FillEllipse(white, ecx - 4.5f * S, y - 5 * S, 9 * S, 10 * S);
                                g.DrawEllipse(thin, ecx - 4.5f * S, y - 5 * S, 9 * S, 10 * S);
                                float px = ecx + v.Look.X * 0.6f, py = y + v.Look.Y * 0.6f;
                                g.FillEllipse(dark, px - 2.5f * S, py - 3 * S, 5 * S, 6 * S);
                            }
                            if (v.State == PetState.Transform)
                                g.DrawLine(o, x + i * 2 * S, y - 7 * S, x + i * 11 * S, y - 11 * S); // angry brows
                            break;
                    }
                }

                float my = y + 9 * S;
                switch (v.State)
                {
                    case PetState.Happy:
                        g.FillPie(inside, x - 8 * S, my - 6 * S, 16 * S, 12 * S, 0, 180);
                        g.DrawPie(thin, x - 8 * S, my - 6 * S, 16 * S, 12 * S, 0, 180);
                        break;
                    case PetState.Eat:
                        float open = (v.StateTimer / 6) % 2 == 0 ? 8 * S : 3 * S;
                        g.FillEllipse(inside, x - 5 * S, my - 2 * S, 10 * S, open);
                        g.DrawEllipse(thin, x - 5 * S, my - 2 * S, 10 * S, open);
                        break;
                    case PetState.Drag:
                        g.DrawEllipse(thin, x - 3.5f * S, my - 2 * S, 7 * S, 8 * S);
                        break;
                    case PetState.Sleep:
                        g.DrawEllipse(thin, x - 2 * S, my - 1 * S, 4 * S, 4 * S);
                        break;
                    case PetState.Transform:
                        DrawTeeth(g, thin, white, x, my, S);
                        break;
                    default:
                        if (v.Hungry) g.DrawArc(thin, x - 6 * S, my - 1 * S, 12 * S, 8 * S, 200, 140);
                        else g.DrawArc(thin, x - 9 * S, my - 7 * S, 18 * S, 10 * S, 20, 140);
                        break;
                }
            }
        }

        static void DrawTeeth(Graphics g, Pen thin, Brush white, float x, float y, float S)
        {
            using (GraphicsPath teeth = Ink.RoundedRect(x - 7 * S, y - 3 * S, 14 * S, 7 * S, 2 * S))
            {
                g.FillPath(white, teeth);
                g.DrawPath(thin, teeth);
            }
            using (Pen line = new Pen(Ink.Outline, 1.2f * S))
            {
                g.DrawLine(line, x - 7 * S, y + 0.5f * S, x + 7 * S, y + 0.5f * S);
                for (int k = -1; k <= 1; k++) g.DrawLine(line, x + k * 3.5f * S, y - 3 * S, x + k * 3.5f * S, y + 4 * S);
            }
        }

        // ---------- form 2: 超級普烏 ----------

        void DrawSuper(Graphics g, PetView v)
        {
            float S = v.S, x = v.X, y = v.Y;
            float sq = Breathe(v);
            float tw = 58 * S * sq, th = 50 * S / sq;
            float waist = y - 34 * S, top = waist - th;
            float lift = StepLift(v, S);
            int back = -Facing(v);
            float hw = 32 * S, hh = 34 * S, hcx = x, hcy = top - 14 * S;

            using (Pen o = Ink.OutlinePen(S))
            using (Pen thin = Ink.OutlinePen(S, 2f))
            using (SolidBrush pink = new SolidBrush(Pink))
            using (SolidBrush cloth = new SolidBrush(Cloth))
            using (SolidBrush gold = new SolidBrush(Gold))
            using (SolidBrush black = new SolidBrush(Black))
            {
                // Long antenna hanging down the back, drawn first so it sits behind.
                float wig = (float)Math.Sin(v.Tick * 0.12) * 3 * S;
                Ink.Tentacle(g, S, Pink,
                    new PointF(hcx, hcy - hh * 0.3f),
                    new PointF(hcx + back * 4 * S, hcy - hh * 1.05f),
                    new PointF(hcx + back * 30 * S, hcy - hh * 0.95f + wig),
                    new PointF(hcx + back * 36 * S, hcy + hh * 0.45f + wig), 7 * S);

                // Baggy trousers + black boots with gold tops.
                for (int i = -1; i <= 1; i += 2)
                {
                    float l = i < 0 ? Math.Max(lift, 0) : Math.Max(-lift, 0);
                    RectangleF pants = new RectangleF(x + i * 13 * S - 14 * S, y - 44 * S - l, 28 * S, 38 * S);
                    g.FillEllipse(cloth, pants);
                    g.DrawEllipse(o, pants);
                    using (GraphicsPath boot = Ink.RoundedRect(x + i * 13 * S - 10 * S, y - 12 * S - l, 20 * S, 12 * S, 4 * S))
                    {
                        g.FillPath(black, boot);
                        g.DrawPath(o, boot);
                    }
                    g.FillRectangle(gold, x + i * 13 * S - 8 * S, y - 12 * S - l, 16 * S, 3 * S);
                }

                // V-shaped torso with a black vest.
                using (GraphicsPath torso = new GraphicsPath())
                {
                    torso.AddLine(x - tw * 0.34f, waist, x + tw * 0.34f, waist);
                    torso.AddBezier(x + tw * 0.34f, waist, x + tw * 0.45f, waist - th * 0.4f, x + tw * 0.56f, top + th * 0.15f, x + tw * 0.45f, top);
                    torso.AddLine(x + tw * 0.45f, top, x - tw * 0.45f, top);
                    torso.AddBezier(x - tw * 0.45f, top, x - tw * 0.56f, top + th * 0.15f, x - tw * 0.45f, waist - th * 0.4f, x - tw * 0.34f, waist);
                    torso.CloseFigure();
                    g.FillPath(pink, torso);

                    GraphicsState clip = g.Save();
                    g.SetClip(torso, CombineMode.Intersect);
                    g.FillRectangle(black, x - tw, top - 2 * S, tw * 0.72f, th + 4 * S);
                    g.FillRectangle(black, x + tw * 0.28f, top - 2 * S, tw * 0.72f, th + 4 * S);
                    using (Pen trim = new Pen(Gold, 2.5f * S))
                    {
                        g.DrawLine(trim, x - tw * 0.28f, top, x - tw * 0.28f, waist);
                        g.DrawLine(trim, x + tw * 0.28f, top, x + tw * 0.28f, waist);
                    }
                    g.FillRectangle(black, x - tw, waist - 7 * S, tw * 2, 7 * S);
                    g.Restore(clip);
                    g.DrawPath(o, torso);
                }
                using (Pen muscle = new Pen(Color.FromArgb(120, Ink.Outline), 1.5f * S))
                {
                    g.DrawArc(muscle, x - 13 * S, top + 6 * S, 12 * S, 9 * S, 20, 140);
                    g.DrawArc(muscle, x + 1 * S, top + 6 * S, 12 * S, 9 * S, 20, 140);
                    g.DrawLine(muscle, x, top + 18 * S, x, waist - 10 * S);
                }

                // Long arms with black wristbands.
                for (int i = -1; i <= 1; i += 2)
                {
                    PointF sh = new PointF(x + i * tw * 0.46f, top + 7 * S);
                    PointF hand;
                    switch (v.State)
                    {
                        case PetState.Happy: hand = new PointF(x + i * tw * 0.72f, top - 16 * S); break;
                        case PetState.Drag: hand = new PointF(x + i * tw * 0.75f, top - 10 * S + (float)Math.Sin(v.Tick * 0.6 + i) * 6 * S); break;
                        case PetState.Transform: hand = new PointF(x + i * tw * 0.62f, top + th * 0.3f); break;
                        default: hand = new PointF(x + i * tw * 0.66f, waist + 2 * S + (v.State == PetState.Walk ? i * lift : 0f)); break;
                    }
                    Ink.Limb(g, S, Pink, sh, hand, 10 * S);
                    Ink.Ball(g, o, black, Ink.Lerp(sh, hand, 0.74f), 7 * S);
                    Ink.Ball(g, o, pink, hand, 7.5f * S);
                }

                // Head.
                RectangleF head = new RectangleF(hcx - hw / 2, hcy - hh / 2, hw, hh);
                g.FillEllipse(pink, head);
                g.DrawEllipse(o, head);
                for (int i = -1; i <= 1; i += 2) g.FillEllipse(black, hcx + i * hw * 0.38f - 2 * S, hcy - 3 * S, 4 * S, 5 * S);

                DrawSuperFace(g, o, thin, v, hcx + (v.State == PetState.Walk ? Facing(v) * 2 * S : 0f), hcy + 1 * S);
                if (v.State == PetState.Eat) Ink.Candy(g, hcx + Facing(v) * hw * 0.8f, hcy + hh * 0.35f, Bites(v), S);
            }
        }

        void DrawSuperFace(Graphics g, Pen o, Pen thin, PetView v, float x, float y)
        {
            float S = v.S;
            using (SolidBrush black = new SolidBrush(Black))
            using (SolidBrush red = new SolidBrush(EyeRed))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush inside = new SolidBrush(MouthColor))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float ecx = x + i * 7.5f * S;
                    switch (v.State)
                    {
                        case PetState.Sleep: Ink.SleepEye(g, o, ecx, y, S); break;
                        case PetState.Happy: Ink.HappyEye(g, o, ecx, y, S); break;
                        case PetState.Drag: Ink.SquintEye(g, o, ecx, y, S, i); break;
                        default:
                            if (v.Blink)
                                g.DrawLine(o, x + i * 3 * S, y, x + i * 12 * S, y - 2 * S);
                            else
                            {
                                // Black sclera, red iris: the classic evil-Buu stare.
                                PointF[] eye =
                                {
                                    new PointF(x + i * 2.5f * S, y - 2 * S), new PointF(x + i * 12 * S, y - 4.5f * S),
                                    new PointF(x + i * 11 * S, y + 3 * S), new PointF(x + i * 3.5f * S, y + 3 * S)
                                };
                                g.FillPolygon(black, eye);
                                float px = x + i * 7.5f * S + v.Look.X * 0.5f, py = y + 0.5f * S + v.Look.Y * 0.4f;
                                g.FillEllipse(red, px - 2.2f * S, py - 2.2f * S, 4.4f * S, 4.4f * S);
                            }
                            break;
                    }
                    if (v.State != PetState.Sleep)
                        g.DrawLine(o, x + i * 2 * S, y - 5 * S, x + i * 13 * S, y - 9 * S); // permanent frown
                }

                float my = y + 10 * S;
                switch (v.State)
                {
                    case PetState.Happy:
                        g.FillPie(white, x - 8 * S, my - 5 * S, 16 * S, 9 * S, 0, 180);
                        g.DrawPie(thin, x - 8 * S, my - 5 * S, 16 * S, 9 * S, 0, 180);
                        break;
                    case PetState.Eat:
                        float open = (v.StateTimer / 6) % 2 == 0 ? 7 * S : 3 * S;
                        g.FillEllipse(inside, x - 4 * S, my - 2 * S, 8 * S, open);
                        g.DrawEllipse(thin, x - 4 * S, my - 2 * S, 8 * S, open);
                        break;
                    case PetState.Drag:
                    case PetState.Transform:
                        DrawTeeth(g, thin, white, x, my, S);
                        break;
                    case PetState.Sleep:
                        g.DrawLine(thin, x - 3 * S, my, x + 3 * S, my);
                        break;
                    default:
                        if (v.Hungry) g.DrawArc(thin, x - 5 * S, my - 1 * S, 10 * S, 7 * S, 200, 140);
                        else g.DrawArc(thin, x - 3 * S, my - 5 * S, 12 * S, 7 * S, 10, 110); // smirk
                        break;
                }
            }
        }

        public override void DrawIcon(Graphics g)
        {
            using (Pen pen = new Pen(Ink.Outline, 2f))
            using (SolidBrush pink = new SolidBrush(Pink))
            using (SolidBrush black = new SolidBrush(Black))
            using (SolidBrush red = new SolidBrush(EyeRed))
            {
                using (Pen ao = new Pen(Ink.Outline, 6f))
                using (Pen af = new Pen(Pink, 3.5f))
                {
                    ao.StartCap = ao.EndCap = af.StartCap = af.EndCap = LineCap.Round;
                    g.DrawBezier(ao, 16, 8, 15, 1, 6, 0, 5, 7);
                    g.DrawBezier(af, 16, 8, 15, 1, 6, 0, 5, 7);
                }
                g.FillEllipse(pink, 3, 6, 26, 24);
                g.DrawEllipse(pen, 3, 6, 26, 24);
                if (form == 1)
                {
                    g.FillEllipse(black, 10, 15, 4, 5);
                    g.FillEllipse(black, 18, 15, 4, 5);
                    g.DrawArc(pen, 11, 18, 10, 6, 20, 140);
                }
                else
                {
                    g.FillPolygon(black, new[] { new PointF(15, 16), new PointF(8, 13), new PointF(9, 19), new PointF(14, 19) });
                    g.FillPolygon(black, new[] { new PointF(17, 16), new PointF(24, 13), new PointF(23, 19), new PointF(18, 19) });
                    g.FillEllipse(red, 10.5f, 15.5f, 3, 3);
                    g.FillEllipse(red, 18.5f, 15.5f, 3, 3);
                }
            }
        }
    }
}
