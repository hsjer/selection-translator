using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 悬停在任务栏状态条上时弹出的详情卡片。和状态条一样用
    /// 分层窗口贴屏幕、鼠标穿透、不抢焦点，只负责把 TaskbarStats
    /// 格式化好的行画成一张圆角卡片。移出状态条 / 卡片区域即由
    /// TaskbarStatsForm 收起。
    /// </summary>
    internal sealed class TaskbarStatsDetailForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly float _scale;

        public TaskbarStatsDetailForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            AutoScaleMode = AutoScaleMode.None;

            float scale = 1f;
            try
            {
                using (Graphics graphics = CreateGraphics())
                {
                    scale = graphics.DpiX / 96f;
                }
            }
            catch
            {
                scale = 1f;
            }
            _scale = Math.Max(1f, Math.Min(2.5f, scale));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_LAYERED |
                    WS_EX_TRANSPARENT |
                    WS_EX_TOOLWINDOW |
                    WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>
        /// 生成圆角卡片位图：每行「标签 …… 数值」，标签左对齐、
        /// 数值右对齐。预览与自检也走这里。
        /// </summary>
        public static Bitmap RenderBitmap(
            IList<TaskbarStats.DetailRow> rows,
            float scale,
            bool lightTheme)
        {
            if (rows == null || rows.Count == 0)
            {
                rows = new List<TaskbarStats.DetailRow>();
            }
            if (scale < 1f)
            {
                scale = 1f;
            }
            if (scale > 2.5f)
            {
                scale = 2.5f;
            }

            Color background = lightTheme
                ? Color.FromArgb(250, 250, 251)
                : Color.FromArgb(36, 38, 41);
            Color labelColor = lightTheme
                ? Color.FromArgb(110, 116, 124)
                : Color.FromArgb(158, 164, 172);
            Color valueColor = lightTheme
                ? Color.FromArgb(26, 28, 32)
                : Color.FromArgb(240, 242, 246);
            Color borderColor = lightTheme
                ? Color.FromArgb(110, 150, 158, 168)
                : Color.FromArgb(90, 255, 255, 255);

            float fontSize = 12f * scale;
            int paddingX = (int)Math.Round(12 * scale);
            int paddingY = (int)Math.Round(9 * scale);
            int rowGap = (int)Math.Round(6 * scale);
            int labelValueGap = (int)Math.Round(18 * scale);
            int corner = (int)Math.Round(10 * scale);

            using (Font font = new Font(
                "Microsoft YaHei UI",
                fontSize,
                FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (Bitmap probe = new Bitmap(1, 1,
                PixelFormat.Format32bppArgb))
            using (Graphics measure = Graphics.FromImage(probe))
            {
                StringFormat format = StringFormat.GenericTypographic;

                int maxLabel = 0;
                int maxValue = 0;
                int rowHeight = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    TaskbarStats.DetailRow row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.Value))
                    {
                        continue;
                    }
                    SizeF labelSize = measure.MeasureString(
                        row.Label ?? "", font, 4096, format);
                    SizeF valueSize = measure.MeasureString(
                        row.Value, font, 4096, format);
                    if ((int)Math.Ceiling(labelSize.Width) > maxLabel)
                    {
                        maxLabel = (int)Math.Ceiling(labelSize.Width);
                    }
                    if ((int)Math.Ceiling(valueSize.Width) > maxValue)
                    {
                        maxValue = (int)Math.Ceiling(valueSize.Width);
                    }
                    int lineHeight = (int)Math.Ceiling(
                        Math.Max(labelSize.Height, valueSize.Height));
                    if (lineHeight > rowHeight)
                    {
                        rowHeight = lineHeight;
                    }
                }
                if (rows.Count == 0)
                {
                    rowHeight = (int)Math.Ceiling(fontSize + 4);
                    maxLabel = (int)Math.Ceiling(fontSize * 4);
                }

                int contentWidth = maxLabel + labelValueGap + maxValue;
                int width = contentWidth + paddingX * 2;
                int visibleRows = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null &&
                        !string.IsNullOrEmpty(rows[i].Value))
                    {
                        visibleRows++;
                    }
                }
                if (visibleRows == 0)
                {
                    visibleRows = 1;
                }
                int height = paddingY * 2 +
                    visibleRows * rowHeight +
                    (visibleRows - 1) * rowGap;
                if (width < 1)
                {
                    width = 1;
                }
                if (height < 1)
                {
                    height = 1;
                }

                Bitmap bitmap = new Bitmap(
                    width, height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint =
                        TextRenderingHint.AntiAliasGridFit;
                    graphics.SmoothingMode =
                        SmoothingMode.AntiAlias;

                    RectangleF card = new RectangleF(
                        0.5f, 0.5f, width - 1f, height - 1f);
                    using (GraphicsPath path =
                        RoundedRect(card, corner))
                    using (Brush cardBrush =
                        new SolidBrush(background))
                    using (Pen borderPen = new Pen(
                        borderColor, 1f))
                    {
                        graphics.FillPath(cardBrush, path);
                        graphics.DrawPath(borderPen, path);
                    }

                    using (Brush labelBrush =
                        new SolidBrush(labelColor))
                    using (Brush valueBrush =
                        new SolidBrush(valueColor))
                    {
                        int y = paddingY;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            TaskbarStats.DetailRow row = rows[i];
                            if (row == null ||
                                string.IsNullOrEmpty(row.Value))
                            {
                                continue;
                            }
                            graphics.DrawString(
                                row.Label ?? "", font, labelBrush,
                                paddingX, y, format);
                            SizeF valueSize = measure.MeasureString(
                                row.Value, font, 4096, format);
                            graphics.DrawString(
                                row.Value, font, valueBrush,
                                width - paddingX - valueSize.Width,
                                y, format);
                            y += rowHeight + rowGap;
                        }
                    }
                }
                return bitmap;
            }
        }

        private static GraphicsPath RoundedRect(
            RectangleF bounds, int radius)
        {
            float diameter = radius * 2f;
            RectangleF arc = new RectangleF(
                bounds.Location, new SizeF(diameter, diameter));
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 把卡片贴到状态条上方（任务栏在顶部时贴下方）。
        /// 返回卡片实际屏幕矩形，供悬停检测判断光标是否还在卡片内。
        /// </summary>
        public Rectangle ShowAt(
            Rectangle barBounds,
            Rectangle taskbar,
            IList<TaskbarStats.DetailRow> rows,
            bool lightTheme)
        {
            using (Bitmap bitmap = RenderBitmap(
                rows, _scale, lightTheme))
            {
                int margin = (int)Math.Round(6 * _scale);
                int x = barBounds.Right - bitmap.Width;
                int y;
                bool taskbarOnTop =
                    taskbar.Height > 0 &&
                    taskbar.Top <= barBounds.Top &&
                    barBounds.Top < taskbar.Bottom + 2;
                if (taskbarOnTop)
                {
                    y = barBounds.Bottom + margin;
                }
                else
                {
                    y = barBounds.Top - bitmap.Height - margin;
                }

                // 不越出屏幕工作区。
                Rectangle working = Screen.FromPoint(
                    barBounds.Location).WorkingArea;
                if (x + bitmap.Width > working.Right)
                {
                    x = working.Right - bitmap.Width;
                }
                if (x < working.Left)
                {
                    x = working.Left;
                }
                if (y + bitmap.Height > working.Bottom)
                {
                    y = working.Bottom - bitmap.Height;
                }
                if (y < working.Top)
                {
                    y = working.Top;
                }

                // UpdateLayeredWindow 会移动原生窗口，但不会同步
                // WinForms 的 Bounds；这里显式对齐，悬停检测才有准数。
                if (!Visible)
                {
                    SetBounds(
                        x, y, bitmap.Width, bitmap.Height);
                    Show();
                }
                LayeredWindow.Draw(Handle, bitmap, new Point(x, y));
                LayeredWindow.KeepTopMost(Handle);
                return new Rectangle(
                    x, y, bitmap.Width, bitmap.Height);
            }
        }
    }
}
