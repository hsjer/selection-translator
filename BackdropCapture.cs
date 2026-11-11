using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SelectionTranslator
{
    /// <summary>
    /// 抓一块桌面背景（壁纸 + 桌面图标），给桌面整理面板做「底板半透明」。
    ///
    /// 为什么不用 Form.Opacity：那是**整窗**连文件名一起变透明，滑到 30%
    /// 时文件名字都糊了。用户要的是「面板底透出桌面、文件照样实心清楚」。
    /// WinForms 里没有「只让背景 alpha 低」的开关（普通窗口的 GDI 绘制不
    /// 支持逐像素 alpha，而分层窗口又得把整块 UI 自绘到位图里，子控件
    /// 全失效）。
    ///
    /// 所以这里走伪透明：抓一块桌面截图，和面板底色按透明比例混合成一
    /// 张图当底板，文件行、图标、文字再用不透明的方式画在上面。因为按
    /// 屏幕坐标抓、位置严格对齐，看上去就是面板底透出了桌面。
    ///
    /// 必须抓**桌面窗口**的 DC 而不是屏幕 DC：屏幕 DC 会把面板自己抓进
    /// 去（自己盖住自己），越滚越黑；桌面窗口 DC 只有壁纸和桌面图标，
    /// 天然不含本程序和其它窗口。
    /// </summary>
    internal static class BackdropCapture
    {
        private const int SRCCOPY = 0x00CC0020;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(
            IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dst, int x, int y,
            int w, int h, IntPtr src, int sx, int sy, int rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(
            IntPtr hWnd, uint affinity);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        private const uint WDA_NONE = 0x00000000;
        /// <summary>抓屏时把自己排除掉（Win10 2004+）。</summary>
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        /// <summary>
        /// 抓屏幕上 <paramref name="screenRect"/> 这块桌面内容。
        /// <paramref name="excludeWindow"/> 是要从结果里排除掉的窗口
        /// （也就是面板自己，不然会抓到自己 → 越叠越黑）。
        /// 抓不到（无桌面、权限、远端会话等）时返回 null，调用方退回纯色底。
        /// </summary>
        internal static Bitmap Capture(Rectangle screenRect, IntPtr excludeWindow)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0)
            {
                return null;
            }
            IntPtr desktop = GetDesktopWindow();
            if (desktop == IntPtr.Zero)
            {
                return null;
            }
            IntPtr screenDc = GetDC(desktop);
            if (screenDc == IntPtr.Zero)
            {
                return null;
            }

            // 抓屏这一小会儿把面板从画面里排除掉：抓完立刻恢复，用户自己
            // 截图、录屏完全不受影响。
            bool excluded = false;
            if (excludeWindow != IntPtr.Zero)
            {
                excluded = SetWindowDisplayAffinity(
                    excludeWindow, WDA_EXCLUDEFROMCAPTURE);
                if (excluded)
                {
                    // 等 DWM 把新属性合成出去，否则这一帧抓到的还是自己。
                    DwmFlush();
                }
                else
                {
                    // 排除不掉就干脆别抓：抓到的画面里含面板自己，和底色
                    // 一叠会越叠越黑，比直接给纯色底还难看。
                    ReleaseDC(desktop, screenDc);
                    return null;
                }
            }

            IntPtr memDc = IntPtr.Zero;
            IntPtr hBmp = IntPtr.Zero;
            try
            {
                memDc = CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                {
                    return null;
                }
                hBmp = CreateCompatibleBitmap(
                    screenDc, screenRect.Width, screenRect.Height);
                if (hBmp == IntPtr.Zero)
                {
                    return null;
                }
                IntPtr old = SelectObject(memDc, hBmp);
                try
                {
                    // 刚把窗口排除掉时，DWM 合成的第一帧里可能还留着面板
                    // 残影（表现为第一次抓的底板比之后几次暗一截）。先
                    // 抓一帧丢掉、flush 一次，第二帧才是稳态的纯桌面。
                    BitBlt(memDc, 0, 0,
                        screenRect.Width, screenRect.Height,
                        screenDc, screenRect.X, screenRect.Y, SRCCOPY);
                    DwmFlush();
                    if (!BitBlt(memDc, 0, 0,
                            screenRect.Width, screenRect.Height,
                            screenDc, screenRect.X, screenRect.Y, SRCCOPY))
                    {
                        return null;
                    }
                }
                finally
                {
                    SelectObject(memDc, old);
                }

                using (Bitmap raw = Image.FromHbitmap(hBmp))
                {
                    return raw.Clone(
                        new Rectangle(0, 0,
                            screenRect.Width, screenRect.Height),
                        PixelFormat.Format32bppPArgb);
                }
            }
            catch (Exception ex)
            {
                Log.Error("抓取桌面背景失败", ex);
                return null;
            }
            finally
            {
                if (hBmp != IntPtr.Zero)
                {
                    DeleteObject(hBmp);
                }
                if (memDc != IntPtr.Zero)
                {
                    DeleteDC(memDc);
                }
                ReleaseDC(desktop, screenDc);
                if (excluded)
                {
                    SetWindowDisplayAffinity(excludeWindow, WDA_NONE);
                    DwmFlush();
                }
            }
        }

        /// <summary>
        /// 采 12×12 个点看看这张图是不是全黑。部分机器（尤其远程桌面 /
        /// 某些显卡驱动）桌面 DC 能 BitBlt 成功但内容全 0，直接用会让面板
        /// 变成一块黑板，比不透明还难看——这种就当抓取失败，退回纯色。
        /// </summary>
        internal static bool LooksUsable(Bitmap bmp)
        {
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
            {
                return false;
            }
            int sum = 0;
            int count = 0;
            try
            {
                int stepX = Math.Max(1, bmp.Width / 12);
                int stepY = Math.Max(1, bmp.Height / 12);
                for (int y = stepY / 2; y < bmp.Height; y += stepY)
                {
                    for (int x = stepX / 2; x < bmp.Width; x += stepX)
                    {
                        Color c = bmp.GetPixel(x, y);
                        sum += c.R + c.G + c.B;
                        count++;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return count > 0 && sum / count > 12;
        }
    }
}
