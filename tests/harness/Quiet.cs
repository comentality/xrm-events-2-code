using System;
using System.Drawing;
using System.Windows.Forms;

namespace Events2Code.Harness
{
    /// <summary>
    /// The window a harness hosts the control in. Shared by both harnesses.
    ///
    /// These are real windows on a real desktop, and nothing here needs them to be seen:
    /// <see cref="Capture"/> asks the window for its own pixels rather than reading the screen,
    /// and the gestures are performed on the controls rather than typed at them. So they open
    /// past the right edge of every monitor and never take the keyboard, and the machine stays
    /// usable while a suite runs.
    ///
    /// Off screen rather than hidden: a window that was never shown has no layout and no painted
    /// pixels to photograph, and PrintWindow does not care where a shown window is.
    ///
    /// Set E2C_HARNESS_ONSCREEN=1 to put them back in front. That is for watching a scenario play
    /// out - and it is also the only arrangement in which Capture's screen grab fallback can mean
    /// anything, since off screen there is nothing at those coordinates to grab.
    /// </summary>
    public class QuietForm : Form
    {
        public static bool OnScreen
        {
            get { return Environment.GetEnvironmentVariable("E2C_HARNESS_ONSCREEN") == "1"; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return !OnScreen; }
        }

        public QuietForm()
        {
            StartPosition = FormStartPosition.Manual;

            if (OnScreen)
            {
                Location = new Point(0, 0);
                TopMost = true;         // the screen capture fallback grabs whatever is on top
            }
            else
            {
                var desktop = SystemInformation.VirtualScreen;
                Location = new Point(desktop.Right + 64, desktop.Top + 64);
                ShowInTaskbar = false;
            }
        }
    }
}
