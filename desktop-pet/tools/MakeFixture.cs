// Renders Mochi as stand-in "AI art" (green screen, 1024x1536, plus expression edits) so the asset
// pipeline and the sprite character can be tested before the real art exists.
// Build: csc /main:DesktopPet.Tools.MakeFixture ... src\*.cs tools\MakeFixture.cs
// Usage: MakeFixture.exe <outDir>
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DesktopPet.Tools
{
    static class MakeFixture
    {
        static void Main(string[] args)
        {
            string dir = args[0];
            Directory.CreateDirectory(dir);
            Mochi m = new Mochi();
            Render(m, dir, "base", delegate(PetView v) { }, 0, 0);
            Render(m, dir, "eyes_closed", delegate(PetView v) { v.Blink = true; }, 0, 0);
            Render(m, dir, "mouth_small", delegate(PetView v) { v.Speaking = true; v.Mouth = Mouth.Small; }, 0, 0);
            Render(m, dir, "mouth_a", delegate(PetView v) { v.Speaking = true; v.Mouth = Mouth.A; }, 2, 1);
            Render(m, dir, "mouth_o", delegate(PetView v) { v.Speaking = true; v.Mouth = Mouth.O; }, 0, 0);
            // Shifted on purpose: real image editors often nudge the picture a few pixels.
            Render(m, dir, "face_happy", delegate(PetView v) { v.State = PetState.Happy; }, 5, -3);
        }

        static void Render(Character c, string dir, string name, Action<PetView> setup, int shiftX, int shiftY)
        {
            using (Bitmap bmp = new Bitmap(1024, 1536, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0, 255, 0));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PetView v = new PetView();
                v.X = 512 + shiftX;
                v.Y = 1400 + shiftY;
                v.S = 9f;
                v.State = PetState.Idle;
                v.Dir = 1;
                setup(v);
                c.Draw(g, v);
                bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
        }
    }
}
