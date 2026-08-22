using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Events2Code.Harness
{
    /// <summary>A window's own pixels, on disk. Shared by both harnesses.</summary>
    public static class Capture
    {
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Asks the window for its own pixels rather than reading them off the screen: scraping the
        /// screen fails outright on a locked workstation, and picks up whatever happens to be in
        /// front otherwise. Screen capture stays as a fallback for anything PrintWindow refuses.
        /// </summary>
        public static void Of(Form form, string outPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var bmp = new Bitmap(form.Bounds.Width, form.Bounds.Height))
            {
                var printed = false;
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try { printed = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }

                if (!printed)
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(form.Bounds.Location, Point.Empty, form.Bounds.Size);

                bmp.Save(outPath, ImageFormat.Png);
            }
        }
    }
}
