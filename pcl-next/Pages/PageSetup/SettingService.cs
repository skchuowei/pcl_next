using System.Linq;
using System.Windows;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public class SettingService
    {

        #region 属性

        /// <summary>
    /// 指定该控件所对应的设置项。
    /// </summary>
        public static string GetKey(DependencyObject obj)
        {
            if (obj is null)
                return null;
            return Conversions.ToString(obj.GetValue(KeyProperty));
        }
        public static void SetKey(DependencyObject obj, string value)
        {
            obj.SetValue(KeyProperty, value);
        }
        public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(SettingService), new PropertyMetadata(null));

        /// <summary>
    /// 指定该控件所对应的设置值。
    /// </summary>
        public static string GetValue(DependencyObject obj)
        {
            if (obj is null)
                return null;
            return Conversions.ToString(obj.GetValue(ValueProperty));
        }
        public static void SetValue(DependencyObject obj, string value)
        {
            obj.SetValue(ValueProperty, value);
        }
        public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached("Value", typeof(string), typeof(SettingService), new PropertyMetadata(null));

        #endregion

        /// <summary>
    /// 重置该控件以及它所有的子控件的设置值。
    /// </summary>
        public static void ResetSettings(DependencyObject Target)
        {
            LogicalTreeHelper.GetChildren(Target).OfType<DependencyObject>().ForAll(c => ResetSettings(c)); // 处理子控件
            string Key = GetKey(Target);
            if (Key is null)
                return;
            Settings.Reset(Key, Instance: PageInstanceLeft.Instance);
        }

        /// <summary>
    /// 更新该控件以及它所有的子控件当前显示的设置值。
    /// </summary>
        public static void RefreshSettings(DependencyObject Target)
        {
            LogicalTreeHelper.GetChildren(Target).OfType<DependencyObject>().ForAll(c => RefreshSettings(c)); // 处理子控件
            string Key = GetKey(Target);
            if (Key is null)
                return;
            var NewValue = Settings.Get(Key, Instance: PageInstanceLeft.Instance);
            if (Target is ISettingControl)
                ((ISettingControl)Target).RefreshSetting(Conversions.ToString(NewValue));
        }

        /// <summary>
    /// 当控件对应的设置改变时，将内容保存到设置中。
    /// </summary>
        public static void SaveSetting(object sender)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (!(sender is ISettingControl))
                return;
            string Key = GetKey((DependencyObject)sender);
            if (Key is null)
                return;
            string NewValue = ((ISettingControl)sender).GetCurrentSetting();
            if (NewValue is null)
                return;
            Settings.Set(Key, NewValue, Instance: PageInstanceLeft.Instance);
        }

    }

    public interface ISettingControl
    {

        /// <summary>
    /// 更新该控件当前所显示的设置值。
    /// </summary>
        void RefreshSetting(string NewValue);
        /// <summary>
    /// 获取该控件当前设定的设置值。
    /// 若不应当关联该设置值，则返回 Nothing。
    /// </summary>
        string GetCurrentSetting();

    }
}