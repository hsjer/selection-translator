using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SelectionTranslator
{
    /// <summary>
    /// UpdateLayeredWindow 的通用封装：把一张带每像素透明的位图
    /// 贴到指定屏幕位置。任务栏状态条和悬浮详情面板共用。
    /// </summary>
    internal static class LayeredWindow
    {
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const int ULW_ALPHA = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION blend,
            int flags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOZORDER = 0x0004;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        /// <summary>
        /// 把分层窗口放回 topmost 层顶部。点击任务栏后 Explorer 会把
        /// 任务栏提到同样 topmost 的我们之上，状态条文字就被盖住了；
        /// 重新置顶即可恢复，且 SWP_NOACTIVATE 保证不抢焦点、不改位置尺寸。
        /// 只在「确实被上面某个窗口盖住」时才调用（见 IsBarObscured）——
        /// 无脑每帧调用会让状态条与别的窗口互抢 Z 序，那才是跳动的来源。
        /// </summary>
        public static void KeepTopMost(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }
            SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE |
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        public static void Draw(
            IntPtr hWnd, Bitmap bitmap, Point position)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);

                POINT destination = new POINT();
                destination.X = position.X;
                destination.Y = position.Y;
                POINT source = new POINT();
                SIZE size = new SIZE();
                size.cx = bitmap.Width;
                size.cy = bitmap.Height;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;

                UpdateLayeredWindow(
                    hWnd,
                    IntPtr.Zero,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
