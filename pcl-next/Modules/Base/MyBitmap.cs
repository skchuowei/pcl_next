using System;
using System.Collections.Concurrent;
// 一个万能的自动图片类型转换工具类

using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeloongCore;
using MeloongCore.Extensions;

namespace PCL
{

    public class MyBitmap
    {

        /// <summary>
    /// 存储的图片
    /// </summary>
        public System.Drawing.Bitmap Pic;

        // 自动类型转换
        // 支持的类：Image，ImageSource，Bitmap，ImageBrush，BitmapSource
        public static implicit operator MyBitmap(System.Drawing.Image Image)
        {
            if (Image is null)
                return null;
            return new MyBitmap(Image);
        }
        public static implicit operator System.Drawing.Image(MyBitmap Image)
        {
            if (Image is null)
                return null;
            return Image.Pic;
        }
        public static implicit operator MyBitmap(ImageSource Image)
        {
            if (Image is null)
                return null;
            return new MyBitmap(Image);
        }
        public static implicit operator ImageSource(MyBitmap Image)
        {
            if (Image is null)
                return null;
            var BitmapPic = Image.Pic;
            var rect = new System.Drawing.Rectangle(0, 0, BitmapPic.Width, BitmapPic.Height);
            var bitmapData = BitmapPic.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var Result = BitmapSource.Create(BitmapPic.Width, BitmapPic.Height, (double)BitmapPic.HorizontalResolution, (double)BitmapPic.VerticalResolution, PixelFormats.Bgra32, null, bitmapData.Scan0, rect.Width * rect.Height * 4, bitmapData.Stride);
                Result.Freeze();
                return Result;
            }
            finally
            {
                BitmapPic.UnlockBits(bitmapData);
            }
        }
        public static implicit operator MyBitmap(System.Drawing.Bitmap Image)
        {
            if (Image is null)
                return null;
            return new MyBitmap(Image);
        }
        public static implicit operator System.Drawing.Bitmap(MyBitmap Image)
        {
            if (Image is null)
                return null;
            return Image.Pic;
        }
        public static implicit operator MyBitmap(ImageBrush Image)
        {
            if (Image is null)
                return null;
            return new MyBitmap(Image);
        }
        public static implicit operator ImageBrush(MyBitmap Image)
        {
            if (Image is null)
                return null;
            return new ImageBrush(new MyBitmap(Image.Pic));
        }

        // 构造函数
        public MyBitmap()
        {
        }
        // 使用缓存
        // VB 原文是过程级 Static（全实例共享一份缓存）；转换器把它抬成了实例字段，
        // 于是每次 new MyBitmap 都拿到空字典、缓存永不命中。恢复静态共享，
        // 并改用 TryGetValue 消除共享之后的「先查再取」竞态。
        private static readonly ConcurrentDictionary<string, MyBitmap> _Cache = new ConcurrentDictionary<string, MyBitmap>();
        public MyBitmap(string FilePathOrResourceName)
        {
            do
            {
                try
                {
                    // 兼容旧版 PCL（Plain Craft Launcher 2）保存在实例配置里的 Logo URI：
                    // 程序集名已改为 pcl-next，旧的 "...;component/Images/..." 不会被下面的小写替换命中，
                    // 会一路落到 FileUtils.ReadAsStream 被当成磁盘路径，抛 DirectoryNotFoundException。
                    const string ComponentImages = ";component/Images/";
                    int ComponentIndex = FilePathOrResourceName.IndexOf(ComponentImages, StringComparison.OrdinalIgnoreCase);
                    if (ComponentIndex >= 0 && FilePathOrResourceName.StartsWithF("pack://application:,,,/", true))
                    {
                        FilePathOrResourceName = ModBase.PathImage + FilePathOrResourceName.Substring(ComponentIndex + ComponentImages.Length);
                    }
                    else
                    {
                        FilePathOrResourceName = FilePathOrResourceName.Replace("pack://application:,,,/images/", ModBase.PathImage);
                    }
                    if (FilePathOrResourceName.StartsWithF(ModBase.PathImage))
                    {
                        if (_Cache.TryGetValue(FilePathOrResourceName, out var Cached))
                        {
                            Pic = Cached.Pic;
                        }
                        else
                        {
                            Pic = new MyBitmap((ImageSource)new ImageSourceConverter().ConvertFromString(FilePathOrResourceName));
                            _Cache.TryAdd(FilePathOrResourceName, Pic);
                        }
                    }
                    else
                    {
                        // 使用这种自己接管 FileStream 的方法加载才能解除文件占用
                        using (var InputStream = FileUtils.ReadAsStream(FilePathOrResourceName))
                        {
                            // 判断是否为 WebP 文件头
                            var Header = new byte[2];
                            // Stream.Read 不保证一次读满；短于 2 字节的文件按原语义当作非 WebP，
                            // 交给下面的 Bitmap 构造去报「图片格式不支持」。
                            int HeaderRead = 0;
                            while (HeaderRead < Header.Length)
                            {
                                int Read = InputStream.Read(Header, HeaderRead, Header.Length - HeaderRead);
                                if (Read == 0) break;
                                HeaderRead += Read;
                            }
                            InputStream.Seek(0L, SeekOrigin.Begin);
                            if (HeaderRead == Header.Length && Header[0] == 82 && Header[1] == 73)
                            {
                                // WebP 必须读满整个文件：单次 Read 未读满时尾部补 0，解码会损坏或失败。
                                var FileBytes = new byte[(int)InputStream.Length];
                                InputStream.ReadExactly(FileBytes, 0, FileBytes.Length);
                                Pic = WebPDecoder.DecodeFromBytes(FileBytes); // 将代码隔离在另外一个类中，这样只要不走进这个分支就不会加载 Imazen.WebP.dll
                            }
                            else
                            {
                                Pic = new System.Drawing.Bitmap(InputStream);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Pic = (System.Drawing.Bitmap)My.Application.TryFindResource(FilePathOrResourceName);
                    if (Pic is null)
                    {
                        Pic = new System.Drawing.Bitmap(1, 1);
                        if (ex is ArgumentException)
                        {
                            throw new Exception($"图片格式不支持，或图片文件损坏（{FilePathOrResourceName}）", ex);
                        }
                        else
                        {
                            throw new Exception($"加载 MyBitmap 意外失败（{FilePathOrResourceName}）", ex);
                        }
                    }
                    else
                    {
                        Logger.Warn(ex, $"指定类型有误的 MyBitmap 加载（{FilePathOrResourceName}）");
                        break;
                    }
                }
            }
            while (false);
        }
        public MyBitmap(ImageSource Image)
        {
            using (var MS = new MemoryStream())
            {
                var Encoder = new PngBitmapEncoder();
                Encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Image));
                Encoder.Save(MS);
                Pic = new System.Drawing.Bitmap(MS);
            }
        }
        public MyBitmap(System.Drawing.Image Image)
        {
            Pic = (System.Drawing.Bitmap)Image;
        }
        public MyBitmap(System.Drawing.Bitmap Image)
        {
            Pic = Image;
        }
        public MyBitmap(ImageBrush Image)
        {
            using (var MS = new MemoryStream())
            {
                var Encoder = new BmpBitmapEncoder();
                Encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Image.ImageSource));
                Encoder.Save(MS);
                Pic = new System.Drawing.Bitmap(MS);
            }
        }
        private class WebPDecoder // 将代码隔离在另外一个类中，这样只要不调用这个方法就不会加载 Imazen.WebP.dll
        {
            private static bool NativeLibraryReady;
            public static System.Drawing.Bitmap DecodeFromBytes(byte[] Bytes)
            {
                // Imazen.WebP 依赖原生 libwebp，首次解码前先把内嵌的 64 位库释放到启动器目录
                if (!NativeLibraryReady)
                {
                    NativeLibraryReady = true;
                    Application.ExtractLibwebp();
                }
                return new Imazen.WebP.SimpleDecoder().DecodeFromBytes(Bytes, Bytes.Length);
            }
        }

        /// <summary>
    /// 获取裁切的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    /// </summary>
        public MyBitmap Clip(int X, int Y, int Width, int Height)
        {
            var bmp = new System.Drawing.Bitmap(Width, Height, Pic.PixelFormat);
            bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.TranslateTransform(-X, -Y);
                g.DrawImage(Pic, new System.Drawing.Rectangle(0, 0, Pic.Width, Pic.Height));
            }
            return bmp;
        }

        /// <summary>
    /// 获取旋转或翻转后的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    /// </summary>
        public MyBitmap RotateFlip(System.Drawing.RotateFlipType Type)
        {
            var bmp = new System.Drawing.Bitmap(Pic);
            bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution);
            bmp.RotateFlip(Type);
            return bmp;
        }

        /// <summary>
    /// 将图像保存到文件。
    /// </summary>
        public void Save(string FilePath)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)this));
            using (var fileStream = FileUtils.CreateAsStream(FilePath))
            {
                encoder.Save(fileStream);
            }
        }

    }
}