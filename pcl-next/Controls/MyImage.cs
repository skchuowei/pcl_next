using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public class MyImage : Image
    {

        #region 公开属性

        /// <summary>
    /// 网络图片的缓存有效期。
    /// 在这个时间后，才会重新尝试下载图片。
    /// </summary>
        public TimeSpan FileCacheExpiredTime = new TimeSpan(7, 0, 0, 0); // 7 天

        /// <summary>
    /// 是否允许将网络图片存储到本地用作缓存。
    /// </summary>
        public bool EnableCache
        {
            get
            {
                return Conversions.ToBoolean(GetValue(EnableCacheProperty));
            }
            set
            {
                SetValue(EnableCacheProperty, value);
            }
        }
        public static readonly DependencyProperty EnableCacheProperty = DependencyProperty.Register("EnableCache", typeof(bool), typeof(MyImage), new PropertyMetadata(true)); // 基类 Image 无同名成员，原 new 关键字多余（CS0109），故去掉

        /// <summary>
    /// 与 Image 的 Source 类似。
    /// 若输入以 http 开头的字符串，则会尝试下载图片然后显示；若 EnableCache 设为 True，图片还会保存为本地缓存。
    /// 支持 WebP 格式的图片。
    /// </summary>
        public new string Source // 覆写 Image 的 Source 属性
        {
            get
            {
                return _Source;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    value = null;
                if ((_Source ?? "") == (value ?? ""))
                    return;
                _Source = value;
                if (!IsInitialized)
                    return; // 属性读取顺序修正：在完成 XAML 属性读取后再触发图片加载（#4868）
                Load();
            }
        }
        private string _Source = "";
        public static new readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(string), typeof(MyImage), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((MyImage)sender).Source = e.NewValue.ToString(); })));

        /// <summary>
    /// 若 Source 是一个网络图片，该地址将作为第二图片源。
    /// </summary>
        public string FallbackSource
        {
            get
            {
                return _FallbackSource;
            }
            set
            {
                _FallbackSource = value;
            }
        }
        private string _FallbackSource = null;

        /// <summary>
    /// 正在下载网络图片时显示的本地图片。
    /// </summary>
        public string LoadingSource
        {
            get
            {
                return _LoadingSource;
            }
            set
            {
                _LoadingSource = value;
            }
        }
        private string _LoadingSource = "pack://application:,,,/images/Icons/NoIcon.png";

        #endregion

        /// <summary>
    /// 实际被呈现的图片地址。
    /// </summary>
        public string ActualSource
        {
            get
            {
                return _ActualSource;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    value = null;
                if ((_ActualSource ?? "") == (value ?? ""))
                    return;
                _ActualSource = value;
                try
                {
                    ImageSource Bitmap = value is null ? null : new MyBitmap(value); // 在这里先触发可能的文件读取，尽量避免在 UI 线程中读取文件
                    ModBase.RunInUi(() => base.Source = Bitmap);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"加载图片失败（{value}）");
                    try
                    {
                        if (value.StartsWithF(ModBase.PathTemp, true))
                            FileUtils.Delete(value);
                    }
                    catch
                    {
                    }
                }
            }
        }
        private string _ActualSource = null;

        public MyImage()
        {
            Initialized += (_, __) => Load();
        }

        private void Load()
        // 属性读取顺序修正：在完成 XAML 属性读取后再触发图片加载（#4868）
        {
            // 空
            if (Source is null)
            {
                ActualSource = null;
                return;
            }
            // 本地图片
            if (!Source.StartsWithF("http"))
            {
                ActualSource = Source;
                return;
            }
            // 从缓存加载网络图片
            bool EnableCache = this.EnableCache;
            string TempPath = $@"{ModBase.PathTemp}MyImage\{Source.GetStableHashCode()}.png{(EnableCache ? "" : global::PCL.ModBase.GetUuid())}"; // 不启用缓存时在末尾加上随机字符串，避免冲突
            var TempFile = FileUtils.GetInfo(TempPath);
            if (EnableCache && TempFile.Exists)
            {
                ActualSource = TempPath; // 显示缓存的图片
                if (DateTime.Now - TempFile.LastWriteTime < FileCacheExpiredTime)
                    return; // 无需刷新缓存
            }
            else
            {
                ActualSource = LoadingSource;
            } // 显示加载中图片
            ModBase.RunInNewThread(() =>
        {
            bool IsLocalFallback = !string.IsNullOrEmpty(FallbackSource) && !FallbackSource.StartsWithF("http");
            try
            {
                // 下载
                ModNet.NetDownloadByLoader(FallbackSource is not null && FallbackSource.StartsWithF("http") && (Source ?? "") != (FallbackSource ?? "") ? (new[] { Source, FallbackSource }) : (new[] { Source }), TempPath, SimulateBrowserHeaders: true);
                ActualSource = TempPath;
                if (!EnableCache)
                    FileUtils.Delete(TempPath);
            }
            catch (Exception ex)
            {
                try
                {
                    if (TempPath is not null)
                        FileUtils.Delete(TempPath);
                }
                catch
                {
                }
                // 加载本地备用图片
                if (IsLocalFallback)
                {
                    try
                    {
                        ActualSource = FallbackSource;
                        Logger.Warn(ex, $"下载图片失败，使用本地备用图片（{Source}）");
                        return;
                    }
                    catch (Exception exx)
                    {
                        Logger.Error(ex, $"下载图片失败（{Source}）", LogBehavior.Toast);
                        Logger.Error(exx, $"加载备用图片失败（{FallbackSource}）", LogBehavior.Toast);
                    }
                }
                else
                {
                    Logger.Error(ex, $"下载图片失败（{Source}，备用：{FallbackSource}）", LogBehavior.Toast);
                }
            }
        }, "MyImage PicLoader " + ModBase.GetUuid() + "#", ThreadPriority.BelowNormal);
        }

    }
}