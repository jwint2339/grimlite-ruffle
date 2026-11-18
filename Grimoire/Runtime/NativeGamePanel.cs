using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Grimoire.Runtime
{
    internal sealed class NativeGamePanel : Panel
    {
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        protected override void OnResize(EventArgs e) { base.OnResize(e); FitChild(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0210 && (message.WParam.ToInt64() & 0xFFFF) == 1) FitChild();
        }

        private void FitChild()
        {
            if (!IsHandleCreated) return;
            IntPtr child = GetWindow(Handle, 5);
            if (child != IntPtr.Zero)
                SetWindowPos(child, IntPtr.Zero, 0, 0, ClientSize.Width, ClientSize.Height, 0x0004 | 0x0010);
        }
    }
}
