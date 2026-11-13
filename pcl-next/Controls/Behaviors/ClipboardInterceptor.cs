using System;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using ThrottleDebounce;
// Author: uye (owner of the MaaAssistantArknights team)
// Original Source: MaaAssistantArknights project - https://github.com/MaaAssistantArknights/MaaAssistantArknights
// License: Apache License 2.0 (this file only)
// 
// This file is originally developed for MaaAssistantArknights and licensed under AGPL v3.0.
// As the original author and copyright holder,
// I hereby re-license this file under the Apache License 2.0 for use in the Plain Craft Launcher project.
// 
// This file is contributed under the spirit of good faith and open cooperation.
// It does not implement any core launcher functions of PCL.
// 
// Description:
// Provides a WPF clipboard handling fix to avoid OpenClipboard exceptions
// in TextBox, RichTextBox, and DataGrid under Windows focus issues or clipboard hooks.
// 
// Date: 2025-07-03

namespace PCL.Controls.Behaviors
{
    public sealed class ClipboardInterceptor
    {
        private ClipboardInterceptor()
        {
        }

        public static readonly DependencyProperty EnableSafeClipboardProperty = DependencyProperty.RegisterAttached("EnableSafeClipboard", typeof(bool), typeof(ClipboardInterceptor), new PropertyMetadata(false, OnEnableSafeClipboardChanged));

        public static void SetEnableSafeClipboard(DependencyObject element, bool value)
        {
            element.SetValue(EnableSafeClipboardProperty, value);
        }

        public static bool GetEnableSafeClipboard(DependencyObject element)
        {
            return Conversions.ToBoolean(element.GetValue(EnableSafeClipboardProperty));
        }

        private static void OnEnableSafeClipboardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox && Conversions.ToBoolean(e.NewValue))
            {
                AddCommandBindingsToTextBox((TextBox)d);
            }
            else if (d is RichTextBox && Conversions.ToBoolean(e.NewValue))
            {
                AddCommandBindingsToRichTextBox((RichTextBox)d);
            }
            else if (d is DataGrid && Conversions.ToBoolean(e.NewValue))
            {
                AddCommandBindingsToDataGrid((DataGrid)d);
            }
        }

        private static void AddCommandBindingsToTextBox(TextBox tb)
        {
            tb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyTextBox));
            tb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, OnCutTextBox, CanEditTextBox));
            tb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPasteTextBox, CanEditTextBox));
        }

        private static void AddCommandBindingsToRichTextBox(RichTextBox rtb)
        {
            rtb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyRichTextBox));
            rtb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, OnCutRichTextBox));
            rtb.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPasteRichTextBox));
        }

        private static void AddCommandBindingsToDataGrid(DataGrid dg)
        {
            dg.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyDataGrid));
        }

        private static void SetClipboardText(string text)
        {
            System.Windows.Forms.Clipboard.SetDataObject(text, false, retryTimes: 10, retryDelay: 100); // 实际的复制
            try
            {
                Clipboard.Flush(); // 将数据持久化到系统剪贴板，避免应用程序退出后数据丢失
            }
            catch (Exception ex) // 持久化失败不抛出异常
            {
                Logger.Warn(ex, $"剪贴板持久化失败（错误码：0x{ex.HResult:X8}）");
            }
        }

        private static void OnCopyTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb is null || tb.SelectionLength <= 0)
                return;

            try
            {
                SetClipboardText(tb.SelectedText);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"复制失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
            }

            e.Handled = true;
        }

        private static void CanEditTextBox(object sender, CanExecuteRoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            e.CanExecute = tb is not null && !tb.IsReadOnly && (!ReferenceEquals(e.Command, ApplicationCommands.Cut) || tb.SelectionLength > 0);
            e.Handled = true;
        }

        private static void OnCutTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            e.Handled = true;
            if (tb is null || tb.IsReadOnly || tb.SelectionLength <= 0)
                return;

            try
            {
                SetClipboardText(tb.SelectedText);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"剪切失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
                return;
            }

            tb.SelectedText = string.Empty;
        }

        private static void OnPasteTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            e.Handled = true;
            if (tb is null || tb.IsReadOnly)
                return;

            try
            {
                // VB 原文：Dim pasteText = Retrier.Attempt(Function(Attempt) As String ... , maxAttempts:=10, delay:=Function() ...)
                // VB 的 lambda 返回 String，重载决议选中 Retrier 的同步泛型重载 Attempt(Of String)，同步拿到 String。
                // 转换器误选了 Func(Of Integer, Task(Of String)) 重载并包了 Task.FromResult，故把结果赋给 String 报 CS0029；
                // 显式指定泛型实参即回落到同步重载，执行方式与 VB 完全一致（阻塞到重试成功/放弃为止）。
                string pasteText = Retrier.Attempt<string>(Attempt => System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null, maxAttempts: 10, delay: _ => TimeSpan.FromMilliseconds(100L));
                if (pasteText is not null)
                {

                    // 将换行符替换为空格（#7825）
                    if (!tb.AcceptsReturn)
                        pasteText = pasteText.ReplaceLineEndings(" ", mergeMultiple: true);

                    int start = tb.SelectionStart;

                    tb.SelectedText = pasteText;
                    tb.CaretIndex = start + pasteText.Length;
                    tb.SelectionLength = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"粘贴失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
            }
        }

        private static void OnCopyRichTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            RichTextBox rtb = sender as RichTextBox;
            if (rtb is null)
                return;

            var textRange = new TextRange(rtb.Selection.Start, rtb.Selection.End);
            if (string.IsNullOrEmpty(textRange.Text))
                return;

            try
            {
                SetClipboardText(textRange.Text);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"复制失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
            }

            e.Handled = true;
        }

        private static void OnCutRichTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            RichTextBox rtb = sender as RichTextBox;
            if (rtb is null)
                return;

            var selection = new TextRange(rtb.Selection.Start, rtb.Selection.End);
            if (string.IsNullOrEmpty(selection.Text))
                return;
            e.Handled = true;

            try
            {
                SetClipboardText(selection.Text);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"剪切失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
                return;
            }

            selection.Text = string.Empty;
        }

        private static void OnPasteRichTextBox(object sender, ExecutedRoutedEventArgs e)
        {
            RichTextBox rtb = sender as RichTextBox;
            if (rtb is null)
                return;

            try
            {
                // VB 原文：Retrier.Attempt(Function(Attempt) As String ... , maxAttempts:=2, delay:=Function() TimeSpan.FromMilliseconds(200))
                // 同 OnPasteTextBox：VB 走同步泛型重载，转换器误选了 Task(Of String) 重载，这里显式指定泛型实参回到同步重载。
                string pasteText = Retrier.Attempt<string>(Attempt => System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null, maxAttempts: 2, delay: _ => TimeSpan.FromMilliseconds(200L));
                if (pasteText is null)
                    return;
                var selection = rtb.Selection;

                selection.Text = pasteText;

                var caretPos = selection.End;
                rtb.CaretPosition = caretPos;
                rtb.Selection.Select(caretPos, caretPos);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"粘贴失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
            }

            e.Handled = true;
        }

        private static void OnCopyDataGrid(object sender, ExecutedRoutedEventArgs e)
        {
            DataGrid dg = sender as DataGrid;
            if (dg is null || dg.SelectedCells is null || dg.SelectedCells.Count == 0)
                return;

            var sb = new StringBuilder();
            var rowGroups = dg.SelectedCells.GroupBy(c => c.Item);

            foreach (var row in rowGroups)
            {
                string rowText = string.Join("\t", row.Select(cell =>
{
    TextBlock tb = cell.Column.GetCellContent(cell.Item) as TextBlock;
    return tb is not null ? tb.Text : "";
}));
                sb.AppendLine(rowText);
            }

            string sbStr = sb.ToString().TrimEnd(ControlChars.Cr, ControlChars.Lf);

            try
            {
                SetClipboardText(sbStr);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"复制失败，请稍后再试（错误码：0x{ex.HResult:X8}）", LogBehavior.Toast);
            }

            e.Handled = true;
        }
    }
}