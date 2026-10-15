using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopPet
{
    // 麻糬: the original mochi cat.
    class Mochi : Character
    {
        static readonly Color BodyTop = Color.FromArgb(255, 252, 250);
        static readonly Color BodyBottom = Color.FromArgb(255, 226, 233);
        static readonly Color EarInner = Color.FromArgb(255, 182, 199);
        static readonly Color CheekColor = Color.FromArgb(130, 255, 140, 170);
        static readonly Color MouthColor = Color.FromArgb(200, 70, 90);

        static readonly PetLines Script = new PetLines
        {
            Greeting = "嗨！我是麻糬～",
            Talk = new[] { "今天也要加油喔！", "喵～", "陪我玩嘛～", "你在寫什麼呀？", "記得喝水喔～", "眼睛休息一下吧", "嘿嘿", "好無聊喔…", "要不要摸摸我？" },
            Click = new[] { "嘿嘿～", "好癢！", "喜歡你～", "再摸一下嘛！", "喵嗚～" },
            Drag = new[] { "哇啊啊～放我下來！", "好高喔！", "要去哪裡呀？" },
            Hungry = new[] { "肚子餓了…", "想吃飯糰…", "咕嚕咕嚕…" },
        };

        public override string Id { get { return "mochi"; } }
        public override string Name { get { return "麻糬"; } }
        public override PetLines Lines { get { return Script; } }
        public override float HeadHeight(float S) { return 96 * S; }

        public override void Draw(Graphics g, PetView v)
        {
            float S = v.S, x = v.X, y = v.Y;
            float sq = Breathe(v);
            float bw = 96 * S * sq, bh = 76 * S / sq;
            float top = y - 4 * S - bh, left = x - bw / 2;

            using (Pen outline = Ink.OutlinePen(S))
            {
                DrawFeet(g, outline, v, bw);
                DrawEars(g, outline, v, top, bw, bh);
                using (GraphicsPath body = Ink.MochiPath(left, top, bw, bh))
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(left, top - 1, bw, bh + 2), BodyTop, BodyBottom, 90f))
                {
                    g.FillPath(fill, body);
                    g.DrawPath(outline, body);
                }
                DrawFace(g, outline, v, top, bw, bh);
                if (v.State == PetState.Eat) Ink.Onigiri(g, x + Facing(v) * bw * 0.52f, top + bh * 0.62f, Bites(v), S);
            }
        }

        void DrawFeet(Graphics g, Pen outline, PetView v, float bw)
        {
            float S = v.S, step = StepLift(v, S);
            float fw = 22 * S, fh = 11 * S;
            using (SolidBrush b = new SolidBrush(BodyBottom))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float fx = v.X + i * bw * 0.24f - fw / 2;
                    float fy = v.Y - fh - S - (i < 0 ? Math.Max(step, 0) : Math.Max(-step, 0));
                    g.FillEllipse(b, fx, fy, fw, fh);
                    g.DrawEllipse(outline, fx, fy, fw, fh);
                }
            }
        }

        void DrawEars(Graphics g, Pen outline, PetView v, float top, float bw, float bh)
        {
            float x = v.X;
            float wiggle = v.State == PetState.Happy ? (float)Math.Sin(v.Tick * 0.5) * 3 * v.S : 0f;
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

        void DrawFace(Graphics g, Pen outline, PetView v, float top, float bw, float bh)
        {
            float S = v.S;
            float fx = v.X + (v.State == PetState.Walk ? Facing(v) * bw * 0.06f : 0f);
            float eyeY = top + bh * 0.46f, ex = bw * 0.19f;

            using (SolidBrush cheek = new SolidBrush(CheekColor))
            {
                g.FillEllipse(cheek, fx - ex - 17 * S, eyeY + 6 * S, 14 * S, 8 * S);
                g.FillEllipse(cheek, fx + ex + 3 * S, eyeY + 6 * S, 14 * S, 8 * S);
            }

            using (SolidBrush dark = new SolidBrush(Ink.Outline))
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float ecx = fx + i * ex;
                    switch (v.State)
                    {
                        case PetState.Sleep: Ink.SleepEye(g, outline, ecx, eyeY, S); break;
                        case PetState.Happy: Ink.HappyEye(g, outline, ecx, eyeY, S); break;
                        case PetState.Drag: Ink.SquintEye(g, outline, ecx, eyeY, S, i); break;
                        default:
                            if (v.Blink)
                                g.DrawLine(outline, ecx - 5 * S, eyeY, ecx + 5 * S, eyeY);
                            else
                            {
                                float px = ecx + v.Look.X, py = eyeY + v.Look.Y;
                                g.FillEllipse(dark, px - 5 * S, py - 6.5f * S, 10 * S, 13 * S);
                                g.FillEllipse(white, px - 3 * S, py - 4.5f * S, 4 * S, 4 * S);
                            }
                            break;
                    }
                }
            }

            float my = eyeY + 9 * S;
            using (Pen mouth = Ink.OutlinePen(S, 2f))
            using (SolidBrush inside = new SolidBrush(MouthColor))
            {
                if (v.Speaking && v.State != PetState.Eat && v.Mouth != Mouth.Closed)
                {
                    float mw = v.Mouth == Mouth.O ? 7 * S : 10 * S, mh = v.Mouth == Mouth.Small ? 4 * S : 9 * S;
                    g.FillEllipse(inside, fx - mw / 2, my - 1 * S, mw, mh);
                    g.DrawEllipse(mouth, fx - mw / 2, my - 1 * S, mw, mh);
                    return;
                }
                switch (v.State)
                {
                    case PetState.Eat:
                        float open = (v.StateTimer / 6) % 2 == 0 ? 9 * S : 4 * S;
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
                        float b = (2.5f + 1.5f * (float)Math.Sin(v.Tick * 0.08)) * S;
                        g.DrawEllipse(mouth, fx - b / 2, my, b, b);
                        break;
                    default:
                        if (v.Hungry)
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

        public override void DrawIcon(Graphics g)
        {
            using (Pen pen = new Pen(Ink.Outline, 2f))
            using (SolidBrush fill = new SolidBrush(BodyBottom))
            using (SolidBrush dark = new SolidBrush(Ink.Outline))
            {
                PointF[] le = { new PointF(5, 14), new PointF(7, 2), new PointF(14, 8) };
                PointF[] re = { new PointF(27, 14), new PointF(25, 2), new PointF(18, 8) };
                g.FillPolygon(fill, le); g.DrawPolygon(pen, le);
                g.FillPolygon(fill, re); g.DrawPolygon(pen, re);
                using (GraphicsPath body = Ink.MochiPath(2, 6, 28, 24)) { g.FillPath(fill, body); g.DrawPath(pen, body); }
                g.FillEllipse(dark, 9, 15, 4, 5);
                g.FillEllipse(dark, 19, 15, 4, 5);
            }
        }
    }
}
