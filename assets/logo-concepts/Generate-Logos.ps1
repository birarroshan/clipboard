param([string]$OutputDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class ClipBoardLogoConceptRenderer
{
    static Color Ink(string hex) => ColorTranslator.FromHtml(hex);

    static GraphicsPath Rounded(float left, float top, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(left, top, diameter, diameter, 180, 90);
        path.AddArc(left + width - diameter, top, diameter, diameter, 270, 90);
        path.AddArc(left + width - diameter, top + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(left, top + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void Box(Graphics canvas, string color, float left, float top, float width, float height, float radius)
    {
        using (var path = Rounded(left, top, width, height, radius))
        using (var brush = new SolidBrush(Ink(color)))
            canvas.FillPath(brush, path);
    }

    static void Line(Graphics canvas, string color, float width, float startX, float startY, float endX, float endY)
    {
        using (var pen = new Pen(Ink(color), width))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            canvas.DrawLine(pen, startX, startY, endX, endY);
        }
    }

    static void Tile(Graphics canvas, string topColor, string bottomColor)
    {
        using (var path = Rounded(24, 24, 464, 464, 104))
        using (var brush = new LinearGradientBrush(new Rectangle(24, 24, 464, 464), Ink(topColor), Ink(bottomColor), 65f))
            canvas.FillPath(brush, path);
    }

    static Bitmap Render(int concept)
    {
        var image = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb);
        using (var canvas = Graphics.FromImage(image))
        {
            canvas.Clear(Color.Transparent);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            canvas.ScaleTransform(2, 2);
            if (concept == 1)
            {
                Tile(canvas, "#087D72", "#075248");
                Box(canvas, "#003F3A", 137, 130, 218, 288, 26);
                Box(canvas, "#FFFFFF", 128, 114, 218, 288, 26);
                Box(canvas, "#064F48", 183, 96, 108, 59, 19);
                Box(canvas, "#B7F3D9", 193, 97, 88, 44, 14);
                Line(canvas, "#548A80", 16, 175, 207, 292, 207);
                Line(canvas, "#548A80", 16, 175, 252, 256, 252);
                Line(canvas, "#548A80", 16, 175, 297, 221, 297);
                using (var fill = new SolidBrush(Ink("#B7F3D9")))
                    canvas.FillEllipse(fill, 264, 273, 112, 112);
                using (var ring = new Pen(Ink("#073D37"), 22))
                    canvas.DrawEllipse(ring, 264, 273, 112, 112);
                Line(canvas, "#073D37", 29, 362, 373, 409, 420);
            }
            else if (concept == 2)
            {
                Tile(canvas, "#F6F8FE", "#E7ECF7");
                var state = canvas.Save();
                canvas.TranslateTransform(236, 252);
                canvas.RotateTransform(-16);
                Box(canvas, "#E6B72C", -108, -139, 200, 254, 25);
                canvas.Restore(state);
                state = canvas.Save();
                canvas.TranslateTransform(257, 262);
                canvas.RotateTransform(-7);
                Box(canvas, "#8BAAF1", -103, -130, 204, 260, 25);
                canvas.Restore(state);
                Box(canvas, "#1644B4", 177, 153, 204, 268, 26);
                Box(canvas, "#2D61E3", 169, 145, 204, 268, 26);
                Box(canvas, "#FFFFFF", 222, 126, 100, 47, 15);
                Line(canvas, "#FFFFFF", 16, 211, 225, 331, 225);
                Line(canvas, "#FFFFFF", 16, 211, 269, 331, 269);
                Line(canvas, "#FFFFFF", 16, 211, 313, 291, 313);
                Box(canvas, "#FFD459", 211, 352, 52, 16, 8);
            }
            else
            {
                Tile(canvas, "#303439", "#1D2024");
                using (var pen = new Pen(Ink("#FF795F"), 58))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    canvas.DrawArc(pen, 117, 114, 272, 280, 48, 264);
                }
                Box(canvas, "#111519", 280, 213, 126, 156, 19);
                Box(canvas, "#F9FBFD", 271, 199, 126, 156, 19);
                Box(canvas, "#FF795F", 306, 181, 57, 36, 12);
                Line(canvas, "#424950", 12, 300, 250, 367, 250);
                Line(canvas, "#424950", 12, 300, 282, 367, 282);
                Line(canvas, "#424950", 12, 300, 314, 341, 314);
            }
        }
        return image;
    }

    public static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] files = { "01-find-clip.png", "02-clip-stack.png", "03-clip-mark.png" };
        string[] names = { "01  Find Clip", "02  Clip Stack", "03  Clip Mark" };
        string[] descriptions = { "Search-first / emerald", "History-first / cobalt + yellow", "Monogram / coral + charcoal" };
        using (var sheet = new Bitmap(1320, 630, PixelFormat.Format32bppArgb))
        using (var canvas = Graphics.FromImage(sheet))
        using (var heading = new Font("Bahnschrift", 26, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var title = new Font("Bahnschrift", 24, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var detail = new Font("Bahnschrift", 17, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush(Ink("#21262D")))
        using (var muted = new SolidBrush(Ink("#69727E")))
        using (var divider = new Pen(Ink("#DADFE6"), 1))
        {
            canvas.Clear(Ink("#F7F8FA"));
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.DrawString("ClipBoard", heading, ink, 48, 30);
            canvas.DrawString("APPLICATION ICON CONCEPTS", detail, muted, 965, 38);
            canvas.DrawLine(divider, 48, 82, 1272, 82);
            for (int index = 0; index < files.Length; index++)
            {
                using (var image = Render(index + 1))
                {
                    image.Save(Path.Combine(directory, files[index]), ImageFormat.Png);
                    int left = 48 + index * 440;
                    canvas.DrawImage(image, new Rectangle(left + 24, 108, 320, 320));
                    canvas.DrawString(names[index], title, ink, left + 24, 461);
                    canvas.DrawString(descriptions[index], detail, muted, left + 24, 497);
                    canvas.DrawImage(image, new Rectangle(left + 24, 548, 48, 48));
                    canvas.DrawImage(image, new Rectangle(left + 91, 556, 32, 32));
                    canvas.DrawImage(image, new Rectangle(left + 142, 564, 16, 16));
                }
            }
            sheet.Save(Path.Combine(directory, "comparison.png"), ImageFormat.Png);
        }
    }
}
'@

[ClipBoardLogoConceptRenderer]::Generate($OutputDirectory)
Get-ChildItem $OutputDirectory -Filter *.png | Select-Object Name, Length