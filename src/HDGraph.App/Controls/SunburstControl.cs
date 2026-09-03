using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HDGraph.Core;
using HDGraph.Geometry;

namespace HDGraph.App.Controls;

/// <summary>Draws a <see cref="DirectoryNode"/> tree as concentric rings and reports what the pointer is over.
/// Geometry comes from <see cref="SunburstLayout"/>; this class only paints and translates pointer events.</summary>
public sealed class SunburstControl : Control
{
    public static readonly StyledProperty<DirectoryNode?> RootProperty =
        AvaloniaProperty.Register<SunburstControl, DirectoryNode?>(nameof(Root));

    public static readonly StyledProperty<int> RingsProperty =
        AvaloniaProperty.Register<SunburstControl, int>(nameof(Rings), 5);

    /// <summary>Clockwise rotation of the whole picture, degrees. Labels stay upright.</summary>
    public static readonly StyledProperty<double> RotationProperty =
        AvaloniaProperty.Register<SunburstControl, double>(nameof(Rotation));

    public static readonly StyledProperty<bool> ShowSizesProperty =
        AvaloniaProperty.Register<SunburstControl, bool>(nameof(ShowSizes), true);

    public static readonly StyledProperty<DirectoryNode?> HoveredNodeProperty =
        AvaloniaProperty.Register<SunburstControl, DirectoryNode?>(nameof(HoveredNode), defaultBindingMode: BindingMode.OneWayToSource);

    /// <summary>Node under the pointer when the right button went down; what a context menu should act on.</summary>
    public static readonly StyledProperty<DirectoryNode?> ContextNodeProperty =
        AvaloniaProperty.Register<SunburstControl, DirectoryNode?>(nameof(ContextNode), defaultBindingMode: BindingMode.OneWayToSource);

    /// <summary>Executed with the clicked directory of a ring.</summary>
    public static readonly StyledProperty<ICommand?> ActivateCommandProperty =
        AvaloniaProperty.Register<SunburstControl, ICommand?>(nameof(ActivateCommand));

    /// <summary>Executed without a parameter when the centre disc is clicked. Without it the centre click
    /// falls back to <see cref="ActivateCommand"/> with the centre's parent.</summary>
    public static readonly StyledProperty<ICommand?> UpCommandProperty =
        AvaloniaProperty.Register<SunburstControl, ICommand?>(nameof(UpCommand));

    private const double Padding = 8;
    private const double LabelFontSize = 12;

    /// <summary>Distance the stripes of a scanning slice travel between frames, as a fraction of their period.</summary>
    private const double StripeStep = 0.025;

    private readonly DispatcherTimer _stripeTimer;
    private SunburstLayout? _layout;
    private Avalonia.Media.Geometry[] _geometries = [];
    private bool _hasScanningSlice;
    private double _stripePhase;
    private Point? _lastPointer;

    static SunburstControl()
    {
        AffectsRender<SunburstControl>(RootProperty, RingsProperty, RotationProperty, ShowSizesProperty, HoveredNodeProperty);
        RootProperty.Changed.AddClassHandler<SunburstControl>(static (c, _) => c.RebuildLayout());
        RingsProperty.Changed.AddClassHandler<SunburstControl>(static (c, _) => c.RebuildLayout());
    }

    public SunburstControl()
    {
        ClipToBounds = true;
        _stripeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _stripeTimer.Tick += (_, _) =>
        {
            _stripePhase = (_stripePhase + StripeStep) % 1;
            InvalidateVisual();
        };
    }

    public DirectoryNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public int Rings
    {
        get => GetValue(RingsProperty);
        set => SetValue(RingsProperty, value);
    }

    public double Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public bool ShowSizes
    {
        get => GetValue(ShowSizesProperty);
        set => SetValue(ShowSizesProperty, value);
    }

    public DirectoryNode? HoveredNode
    {
        get => GetValue(HoveredNodeProperty);
        set => SetValue(HoveredNodeProperty, value);
    }

    public DirectoryNode? ContextNode
    {
        get => GetValue(ContextNodeProperty);
        set => SetValue(ContextNodeProperty, value);
    }

    public ICommand? ActivateCommand
    {
        get => GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    public ICommand? UpCommand
    {
        get => GetValue(UpCommandProperty);
        set => SetValue(UpCommandProperty, value);
    }

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    private double ChartRadius => Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - Padding);

    /// <summary>Lays out the current root anew and re-derives what is under the pointer, so a tree that
    /// changes under a still pointer (a scan that grows while the user looks at it) keeps the hover
    /// consistent without a flicker to nothing. Runs when the root, the ring count or the size changes,
    /// always outside the render pass: a change of <see cref="HoveredNode"/> invalidates this visual (and,
    /// through its binding, the details pane), which Avalonia forbids while it renders.</summary>
    private void RebuildLayout()
    {
        _layout = null;
        _geometries = [];
        BuildLayout();
        RefreshHover();
    }

    private void RefreshHover() => SetHover(_lastPointer is { } pointer ? HitTest(pointer) : null);

    /// <summary>The layout for the current root and size, built unless the cached one still fits. Changes
    /// no property, so it is safe to call from <see cref="Render"/>.</summary>
    private SunburstLayout? BuildLayout()
    {
        var root = Root;
        if (root is null)
        {
            _layout = null;
            _geometries = [];
            AnimateStripes(false);
            return null;
        }

        var radius = ChartRadius;
        if (_layout is not null && ReferenceEquals(_layout.Root, root) && Math.Abs(_layout.Radius - radius) < 0.01)
            return _layout;

        _layout = SunburstLayout.Build(root, radius, new SunburstLayoutOptions { Rings = Math.Max(1, Rings) });
        _geometries = new Avalonia.Media.Geometry[_layout.Arcs.Count];
        _hasScanningSlice = false;
        for (var i = 0; i < _geometries.Length; i++)
        {
            _geometries[i] = BuildArcGeometry(_layout.Arcs[i]);
            _hasScanningSlice |= _layout.Arcs[i].Node.Kind == NodeKind.Scanning;
        }

        AnimateStripes(_hasScanningSlice);
        return _layout;
    }

    /// <summary>What <see cref="Render"/> draws. Normally the cache is current, since every change that
    /// invalidates it goes through <see cref="RebuildLayout"/>; should it not be, the geometry is built here
    /// and the hover, which must not change during the render pass, catches up right after it.</summary>
    private SunburstLayout? EnsureLayoutForRender()
    {
        var cached = _layout;
        var layout = BuildLayout();
        if (!ReferenceEquals(layout, cached))
            Dispatcher.UIThread.Post(RefreshHover, DispatcherPriority.Input);
        return layout;
    }

    private void AnimateStripes(bool on)
    {
        if (on == _stripeTimer.IsEnabled) return;
        if (on) _stripeTimer.Start();
        else _stripeTimer.Stop();
    }

    private void SetHover(DirectoryNode? node)
    {
        if (!ReferenceEquals(node, HoveredNode))
            SetCurrentValue(HoveredNodeProperty, node);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var layout = EnsureLayoutForRender();
        var dark = ActualThemeVariant == ThemeVariant.Dark;

        if (layout is null || layout.Radius <= 0)
        {
            DrawHint(context, dark);
            return;
        }

        var center = Center;
        var hovered = HoveredNode;
        var separator = new Pen(new SolidColorBrush(SunburstPalette.Separator(dark)), 1);

        // Sectors are laid out around the origin; rotate and move them into place in one transform.
        var transform = Matrix.CreateRotation(Rotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(transform))
        {
            var arcs = layout.Arcs;
            for (var i = 0; i < arcs.Count; i++)
            {
                var arc = arcs[i];
                var isHovered = hovered is not null && ReferenceEquals(arc.Node, hovered);
                IBrush fill = arc.Node.Kind == NodeKind.Scanning
                    ? StripeBrush(SunburstPalette.ScanningStripes(dark, isHovered), _stripePhase)
                    : new SolidColorBrush(SunburstPalette.Fill(arc, dark, isHovered));
                // A hairline sector is thinner than its own outline; outlining it would paint it white.
                var outline = arc.MidArcLength < 2 ? null : separator;
                context.DrawGeometry(fill, outline, _geometries[i]);
            }

            var centerHovered = hovered is not null && ReferenceEquals(hovered, layout.Root);
            var centerColor = SunburstPalette.CenterFill(dark);
            if (centerHovered)
                centerColor = dark ? Color.FromRgb(0x50, 0x50, 0x50) : Color.FromRgb(0xD4, 0xD4, 0xD4);
            context.DrawEllipse(new SolidColorBrush(centerColor), separator, new Point(0, 0), layout.CenterRadius, layout.CenterRadius);
        }

        DrawLabels(context, layout, center, dark);
    }

    /// <summary>Diagonal stripes that march as <paramref name="phase"/> advances: the "still working" look of
    /// the slice a running scan fills in. Absolute units keep the stripe width the same in every sector.</summary>
    private static LinearGradientBrush StripeBrush((Color A, Color B) colors, double phase)
    {
        const double period = 9;
        var offset = phase * period * 2;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(offset, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(offset + period, period, RelativeUnit.Absolute),
            SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops =
            {
                new GradientStop(colors.A, 0),
                new GradientStop(colors.A, 0.5),
                new GradientStop(colors.B, 0.5),
                new GradientStop(colors.B, 1),
            },
        };
    }

    private void DrawLabels(DrawingContext context, SunburstLayout layout, Point center, bool dark)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this));
        var brush = new SolidColorBrush(SunburstPalette.Label(dark));
        var culture = CultureInfo.CurrentUICulture;
        var showSizes = ShowSizes;
        var lineHeight = LabelFontSize * 1.3;
        var twoLines = layout.RingThickness >= lineHeight * 2 + 6;

        if (layout.RingThickness < lineHeight + 4) return;

        foreach (var arc in layout.Arcs)
        {
            var available = LabelRoom(arc, layout.RingThickness, Rotation);
            if (available < 24) continue;

            var (x, y) = SunburstLayout.ToPoint(arc.MidAngle + Rotation, arc.MidRadius);
            var anchor = new Point(center.X + x, center.Y + y);

            var name = Measure(arc.Node.Name, typeface, brush, culture, available);
            var size = showSizes && twoLines ? Measure(SizeFormatter.Format(arc.Node.TotalSize), typeface, brush, culture, available) : null;

            var totalHeight = name.Height + (size?.Height ?? 0);
            var top = anchor.Y - totalHeight / 2;
            context.DrawText(name, new Point(anchor.X - available / 2, top));
            if (size is not null)
                context.DrawText(size, new Point(anchor.X - available / 2, top + name.Height));
        }

        var rootName = Measure(layout.Root.Name, typeface, brush, culture, layout.CenterRadius * 1.8);
        var rootSize = Measure(SizeFormatter.Format(layout.Root.TotalSize), typeface, brush, culture, layout.CenterRadius * 1.8);
        var rootTop = center.Y - (rootName.Height + rootSize.Height) / 2;
        context.DrawText(rootName, new Point(center.X - layout.CenterRadius * 0.9, rootTop));
        context.DrawText(rootSize, new Point(center.X - layout.CenterRadius * 0.9, rootTop + rootName.Height));
    }

    /// <summary>Horizontal room for an upright label centred in a sector. Along the ring the limit is the
    /// arc length; across it, the ring thickness. Where the sector sits decides which one applies: at the
    /// sides a horizontal label runs radially, at the top and bottom it runs along the ring.</summary>
    private static double LabelRoom(in Arc arc, double ringThickness, double rotation)
    {
        var angle = (arc.MidAngle + rotation) * Math.PI / 180;
        var radialRoom = (ringThickness - 4) / Math.Max(Math.Abs(Math.Sin(angle)), 0.2);
        var tangentialRoom = arc.MidArcLength * 0.9;
        return Math.Min(radialRoom, tangentialRoom);
    }

    private static FormattedText Measure(string text, Typeface typeface, IBrush brush, CultureInfo culture, double maxWidth) =>
        new(text, culture, FlowDirection.LeftToRight, typeface, LabelFontSize, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

    private void DrawHint(DrawingContext context, bool dark)
    {
        var text = new FormattedText(
            "Scan a folder or a drive to see where the space went.",
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this)), 14,
            new SolidColorBrush(SunburstPalette.Label(dark), 0.6));
        var center = Center;
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static Avalonia.Media.Geometry BuildArcGeometry(in Arc arc)
    {
        // A full turn has coincident end points and no direction; a hairline short of it draws as a ring.
        var sweep = Math.Min(arc.SweepAngle, 359.99);
        var isLargeArc = sweep > 180;
        var (x0, y0) = SunburstLayout.ToPoint(arc.StartAngle, arc.OuterRadius);
        var (x1, y1) = SunburstLayout.ToPoint(arc.StartAngle + sweep, arc.OuterRadius);
        var (x2, y2) = SunburstLayout.ToPoint(arc.StartAngle + sweep, arc.InnerRadius);
        var (x3, y3) = SunburstLayout.ToPoint(arc.StartAngle, arc.InnerRadius);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x0, y0), isFilled: true);
            ctx.ArcTo(new Point(x1, y1), new Size(arc.OuterRadius, arc.OuterRadius), 0, isLargeArc, SweepDirection.Clockwise);
            ctx.LineTo(new Point(x2, y2));
            ctx.ArcTo(new Point(x3, y3), new Size(arc.InnerRadius, arc.InnerRadius), 0, isLargeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private DirectoryNode? HitTest(Point position)
    {
        if (_layout is null) return null;
        var center = Center;
        return _layout.HitTest(position.X - center.X, position.Y - center.Y, Rotation);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _lastPointer = position;
        var node = HitTest(position);
        SetHover(node);
        Cursor = node is { Kind: NodeKind.Directory } ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _lastPointer = null;
        SetHover(null);
        Cursor = Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var node = HitTest(e.GetPosition(this));
        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsRightButtonPressed)
        {
            SetCurrentValue(ContextNodeProperty, node);
            return;
        }

        if (!properties.IsLeftButtonPressed || _layout is null || node is null) return;

        if (ReferenceEquals(node, _layout.Root) && UpCommand is { } up)
        {
            if (up.CanExecute(null))
            {
                up.Execute(null);
                e.Handled = true;
            }
            return;
        }

        var target = ReferenceEquals(node, _layout.Root) ? node.Parent : node;
        if (target is not { Kind: NodeKind.Directory }) return;
        if (ActivateCommand is { } command && command.CanExecute(target))
        {
            command.Execute(target);
            e.Handled = true;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RebuildLayout();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AnimateStripes(false);
    }
}
