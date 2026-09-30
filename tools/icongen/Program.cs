using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGen
{
    /// <summary>
    /// 一次性猫爪印图标生成器——输出多尺寸 ICO（16/24/32/48/64/128/256）。
    /// 用法：icongen &lt;输出 ico 路径&gt;
    /// </summary>
    public static class Program
    {
        /// <summary>生成尺寸序列——小到大逐档绘制（不靠缩放出小图）</summary>
        private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

        /// <summary>
        /// 入口——逐尺寸绘制 PNG 帧 → 组装 ICO 容器（ICONDIR + ICONDIRENTRY × N + PNG 数据）。
        /// </summary>
        /// <param name="args">args[0] = 输出 ico 路径</param>
        /// <returns>退出码</returns>
        public static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "app.ico";
            // 可选第二输出——前端 favicon 副本（一处生成、两处落位，杜绝手工拷贝分叉）
            string extraPath = args.Length > 1 ? args[1] : "";
            int count = Sizes.Length;
            byte[][] frames = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                frames[i] = RenderPng(Sizes[i]);
                Console.WriteLine("帧 " + Sizes[i].ToString() + " → " + frames[i].Length.ToString() + " 字节");
            }
            using (MemoryStream ico = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(ico);
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)count);
                int offset = 6 + 16 * count;
                for (int i = 0; i < count; i++)
                {
                    int px = Sizes[i];
                    writer.Write((byte)(px >= 256 ? 0 : px));
                    writer.Write((byte)(px >= 256 ? 0 : px));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset = offset + frames[i].Length;
                }
                for (int i = 0; i < count; i++)
                {
                    writer.Write(frames[i]);
                }
                writer.Flush();
                byte[] icoBytes = ico.ToArray();
                File.WriteAllBytes(outPath, icoBytes);
                if (extraPath.Length > 0)
                {
                    // 前端 favicon 副本——与 exe 图标同源（同一份字节）
                    File.WriteAllBytes(extraPath, icoBytes);
                    Console.WriteLine("副本 " + extraPath);
                }
            }
            Console.WriteLine("OK " + outPath);
            // 预览图（人/机自查用）——最大帧另存 PNG，便于直接看图核对配色与几何
            string previewPath = outPath + ".preview.png";
            File.WriteAllBytes(previewPath, frames[count - 1]);
            Console.WriteLine("预览 " + previewPath);
            return 0;
        }

        /// <summary>
        /// 绘制单帧——深色圆底 + 橙色猫爪印（四趾弧排 + 大肉垫）。
        /// 小尺寸加宽趾肉间隙，避免缩到 16px 时糊成一团。
        /// </summary>
        /// <param name="size">边长（像素）</param>
        /// <returns>PNG 字节</returns>
        private static byte[] RenderPng(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float s = size;
                    float toeD = s * 0.20f;
                    float ringR = s * 0.40f;
                    if (size <= 24)
                    {
                        // 小尺寸：趾垫略缩 + 环绕半径略放——保住相邻趾垫的可见间隙
                        toeD = s * 0.17f;
                        ringR = s * 0.42f;
                    }
                    // [段1] 底盘——军绿实心圆（留 1px 防裁剪）
                    using (SolidBrush back = new SolidBrush(Color.FromArgb(255, 111, 129, 69)))
                    {
                        g.FillEllipse(back, 0f, 0f, s - 1f, s - 1f);
                    }
                    // [段2] 爪印——肉色（偏白肤色）：下 1 大肉垫 + 上 4 小趾垫
                    using (SolidBrush paw = new SolidBrush(Color.FromArgb(255, 240, 210, 192)))
                    {
                        // 大肉垫——下方椭圆
                        g.FillEllipse(paw, s * 0.25f, s * 0.53f, s * 0.50f, s * 0.34f);
                        // [段3] 四趾——以肉垫中心为圆心、等角距环绕（±17.5° / ±52.5°，自正上方起算）——相邻间距一致
                        DrawToeAt(g, paw, s * 0.50f, s * 0.70f, ringR, -52.5f, toeD);
                        DrawToeAt(g, paw, s * 0.50f, s * 0.70f, ringR, -17.5f, toeD);
                        DrawToeAt(g, paw, s * 0.50f, s * 0.70f, ringR, 17.5f, toeD);
                        DrawToeAt(g, paw, s * 0.50f, s * 0.70f, ringR, 52.5f, toeD);
                    }
                }
                using (MemoryStream png = new MemoryStream())
                {
                    bmp.Save(png, ImageFormat.Png);
                    return png.ToArray();
                }
            }
        }

        /// <summary>
        /// 按极坐标画一个趾垫——以肉垫中心为圆心，给定角度（自正上方起算，正=偏右）与环绕半径定位。
        /// 等角距 = 等弦长 = 相邻趾垫间距一致（几何均匀，不靠手调坐标）。
        /// </summary>
        /// <param name="g">画布</param>
        /// <param name="brush">笔刷</param>
        /// <param name="cx">圆心 X（像素）</param>
        /// <param name="cy">圆心 Y（像素）</param>
        /// <param name="radius">环绕半径（像素）</param>
        /// <param name="angleDeg">角度（度——0=正上方，正=偏右）</param>
        /// <param name="d">趾垫直径（像素）</param>
        private static void DrawToeAt(Graphics g, Brush brush, float cx, float cy, float radius, float angleDeg, float d)
        {
            double rad = angleDeg * Math.PI / 180.0;
            float x = cx + radius * (float)Math.Sin(rad) - d / 2f;
            float y = cy - radius * (float)Math.Cos(rad) - d / 2f;
            g.FillEllipse(brush, x, y, d, d);
        }
    }
}
