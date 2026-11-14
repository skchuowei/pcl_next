using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shapes;

namespace PCL
{
    /// <summary>
    /// 轻量 lucide 风格描边图标：仅承载 24×24 网格下的 path / circle / rect 元素定义。
    /// 渲染由 <see cref="MyListItem"/> 按描边线稿方式绘制（圆头端点、圆角连接）。
    /// </summary>
    public static class LucideIcon
    {
        public enum Kind { Path, Circle, Rect }

        public sealed class Element
        {
            public Kind Kind;
            /// <summary>Path 的几何路径 d。</summary>
            public string Data;
            public double Cx, Cy, R;
            public double X, Y, Width, Height, Radius;
            /// <summary>Circle 是否为实心填充（lucide 内色点）；否则为描边空心。</summary>
            public bool Filled;
        }

        /// <summary>
        /// 名称 → 元素列表。坐标统一为 24×24 网格，与 lucide 官方 SVG 一致。
        /// </summary>
        public static readonly Dictionary<string, List<Element>> Icons = new Dictionary<string, List<Element>>
        {
            ["play"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z" }
            },
            ["bubbles"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M7.001 15.085A1.5 1.5 0 0 1 9 16.5" },
                new Element { Kind = Kind.Circle, Cx = 18.5, Cy = 8.5, R = 3.5 },
                new Element { Kind = Kind.Circle, Cx = 7.5, Cy = 16.5, R = 5.5 },
                new Element { Kind = Kind.Circle, Cx = 7.5, Cy = 4.5, R = 2.5 }
            },
            ["palette"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z" },
                new Element { Kind = Kind.Circle, Cx = 13.5, Cy = 6.5, R = 0.5, Filled = true },
                new Element { Kind = Kind.Circle, Cx = 17.5, Cy = 10.5, R = 0.5, Filled = true },
                new Element { Kind = Kind.Circle, Cx = 6.5, Cy = 12.5, R = 0.5, Filled = true },
                new Element { Kind = Kind.Circle, Cx = 8.5, Cy = 7.5, R = 0.5, Filled = true }
            },
            ["image"] = new List<Element>
            {
                new Element { Kind = Kind.Rect, X = 3, Y = 3, Width = 18, Height = 18, Radius = 2 },
                new Element { Kind = Kind.Circle, Cx = 9, Cy = 9, R = 2 },
                new Element { Kind = Kind.Path, Data = "m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21" }
            },
            ["layout-dashboard"] = new List<Element>
            {
                new Element { Kind = Kind.Rect, X = 3, Y = 3, Width = 7, Height = 9, Radius = 1 },
                new Element { Kind = Kind.Rect, X = 14, Y = 3, Width = 7, Height = 5, Radius = 1 },
                new Element { Kind = Kind.Rect, X = 14, Y = 12, Width = 7, Height = 9, Radius = 1 },
                new Element { Kind = Kind.Rect, X = 3, Y = 16, Width = 7, Height = 5, Radius = 1 }
            },
            ["download"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M12 15V3" },
                new Element { Kind = Kind.Path, Data = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" },
                new Element { Kind = Kind.Path, Data = "m7 10 5 5 5-5" }
            },
            ["message-circle"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M2.992 16.342a2 2 0 0 1 .094 1.167l-1.065 3.29a1 1 0 0 0 1.236 1.168l3.413-.998a2 2 0 0 1 1.099.092 10 10 0 1 0-4.777-4.719" }
            },
            ["user"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2" },
                new Element { Kind = Kind.Circle, Cx = 12, Cy = 7, R = 4 }
            },
            ["flask-conical"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2" },
                new Element { Kind = Kind.Path, Data = "M6.453 15h11.094" },
                new Element { Kind = Kind.Path, Data = "M8.5 2h7" }
            },
            ["grid-2x2"] = new List<Element>
            {
                new Element { Kind = Kind.Path, Data = "M12 3v18" },
                new Element { Kind = Kind.Path, Data = "M3 12h18" },
                new Element { Kind = Kind.Rect, X = 3, Y = 3, Width = 18, Height = 18, Radius = 2 }
            }
        };

        /// <summary>按元素定义构建 24×24 描边容器；找不到名称返回 null。</summary>
        public static Canvas Build(string name, Binding foregroundBinding)
        {
            if (!Icons.TryGetValue(name, out var elements) || elements == null)
                return null;

            var canvas = new Canvas
            {
                Width = 24d,
                Height = 24d,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            foreach (var el in elements)
            {
                Shape shape;
                switch (el.Kind)
                {
                    case Kind.Path:
                        shape = new System.Windows.Shapes.Path
                        {
                            Data = (Geometry)new GeometryConverter().ConvertFromString(el.Data)
                        };
                        break;
                    case Kind.Circle:
                        var ellipse = new Ellipse
                        {
                            Width = el.R * 2d,
                            Height = el.R * 2d
                        };
                        Canvas.SetLeft(ellipse, el.Cx - el.R);
                        Canvas.SetTop(ellipse, el.Cy - el.R);
                        if (el.Filled)
                            ellipse.SetBinding(Shape.FillProperty, foregroundBinding);
                        shape = ellipse;
                        break;
                    case Kind.Rect:
                        var rect = new Rectangle
                        {
                            Width = el.Width,
                            Height = el.Height,
                            RadiusX = el.Radius,
                            RadiusY = el.Radius
                        };
                        Canvas.SetLeft(rect, el.X);
                        Canvas.SetTop(rect, el.Y);
                        shape = rect;
                        break;
                    default:
                        continue;
                }

                if (!el.Filled)
                {
                    shape.Fill = null;
                    shape.SetBinding(Shape.StrokeProperty, foregroundBinding);
                    shape.StrokeThickness = 2d;
                    shape.StrokeLineJoin = PenLineJoin.Round;
                    shape.StrokeStartLineCap = PenLineCap.Round;
                    shape.StrokeEndLineCap = PenLineCap.Round;
                }
                canvas.Children.Add(shape);
            }

            return canvas;
        }
    }
}
