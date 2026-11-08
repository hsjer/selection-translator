// 生成 app.ico：蓝色渐变圆角方块 + 白色扳手（画法在 AppIcon.cs，与托盘同一份）。
//
// 由 build.ps1 在 app.ico 不存在时自动编译运行：
//   csc /target:exe /r:System.dll /r:System.Drawing.dll AppIcon.cs tools\MakeIcon.cs
//   MakeIcon.exe <输出路径>
//
// 采用标准 BMP(DIB) 帧而不是 PNG 帧，兼容 .NET Framework 4.x 自带的
// csc.exe 的 /win32icon 参数（老编译器不认 PNG 压缩的 ICO 帧）。
using System;
using System.IO;

namespace SelectionTranslator
{
    internal static class MakeIcon
    {
        private static int Main(string[] args)
        {
            string path = args.Length > 0 ? args[0] : "app.ico";
            try
            {
                AppIcon.WriteIco(path);
                Console.WriteLine("Wrote " + path + " (" +
                    new FileInfo(path).Length + " bytes, " +
                    AppIcon.IcoSizes.Length + " sizes)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
