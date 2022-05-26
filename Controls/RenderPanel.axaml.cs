using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

namespace rgit.Controls;

public partial class RenderPanel : UserControl
{
    public delegate void OnRenderDelegate(ISkiaDrawingContextImpl context);

    private CustomDrawOp drawOp;
    public event OnRenderDelegate? OnRender;

    public RenderPanel()
    {
        InitializeComponent();
        this.drawOp = new CustomDrawOp(this);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        this.drawOp.Bounds = new Rect(0, 0, this.Bounds.Width, this.Bounds.Height);
        context.Custom(this.drawOp);
    }

    private class CustomDrawOp : ICustomDrawOperation
    {
        private readonly RenderPanel parent;

        public CustomDrawOp(RenderPanel parent)
        {
            this.parent = parent;
        }

        public void Render(IDrawingContextImpl context)
        {
            if (context is not ISkiaDrawingContextImpl skiaContext)
                throw new NotImplementedException("Non skia not implemented.");
            
            parent.OnRender?.Invoke(skiaContext);
        }

        public Rect Bounds { get; set; }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        public void Dispose()
        {
            // Nothing to do.
        }
    }
}