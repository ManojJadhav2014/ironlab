﻿// Copyright (c) 2010 Joe Moorhouse

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;

namespace IronPlot
{
    /// <summary>
    /// Position of annotation elements applied to PlotPanel.
    /// </summary>
    public enum Position { Left, Right, Top, Bottom }

    public partial class PlotPanel : Panel
    {
        // Canvas for plot content:
        internal Canvas Canvas;

        // Also a background Canvas
        internal Canvas BackgroundCanvas;
        // This is present because a Direct2D surface can also be added and it is desirable to make the
        // canvas above transparent in this case. 

        // Also a Direct2DControl: a control which can use Direct2D for fast plotting.
        internal Direct2DControl direct2DControl = null;

        internal Axes2D axes;

        // Annotation regions
        internal StackPanel annotationsLeft;
        internal StackPanel annotationsRight;
        internal StackPanel annotationsTop;
        internal StackPanel annotationsBottom;

        protected DispatcherTimer marginChangeTimer;

        // Arrangement
        // whether or not legend is shown:
        bool showAnnotationsLeft = false;
        bool showAnnotationsRight = false;
        bool showAnnotationsTop = false;
        bool showAnnotationsBottom = false;

        protected Size axesRegionSize;
        // The location of canvas:
        protected Rect canvasLocation;
        // Width and height of legends, axes and canvas combined: 
        double entireWidth = 0, entireHeight = 0;
        // Offset of combination of legends, axes and canvas in available area:
        double offsetX = 0, offsetY = 0;

        public static readonly DependencyProperty EqualAxesProperty =
            DependencyProperty.Register("EqualAxesProperty",
            typeof(bool), typeof(PlotPanel),
            new PropertyMetadata(false, OnEqualAxesChanged));

        public static readonly DependencyProperty UseDirect2DProperty =
            DependencyProperty.Register("UseDirect2DProperty",
            typeof(bool), typeof(PlotPanel),
            new PropertyMetadata(false, OnUseDirect2DChanged));

        internal bool UseDirect2D
        {
            set
            {
                SetValue(UseDirect2DProperty, value);
            }
            get { return (bool)GetValue(UseDirect2DProperty); }
        }

        protected static void OnEqualAxesChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            PlotPanel plotPanelLocal = ((PlotPanel)obj);
            plotPanelLocal.InvalidateMeasure();
        }

        protected static void OnUseDirect2DChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            PlotPanel plotPanelLocal = ((PlotPanel)obj);
            if (plotPanelLocal.direct2DControl == null && plotPanelLocal.UseDirect2D)
            {
                // Create Direct2DControl:
                try
                {
                    plotPanelLocal.direct2DControl = new Direct2DControl();
                    plotPanelLocal.Children.Add(plotPanelLocal.direct2DControl);
                    plotPanelLocal.direct2DControl.SetValue(Grid.ZIndexProperty, 75);
                }
                catch (Exception)
                {
                    plotPanelLocal.direct2DControl = null;
                    plotPanelLocal.UseDirect2D = false;
                }
                return;
            }
            if (plotPanelLocal.UseDirect2D) plotPanelLocal.direct2DControl.Visibility = Visibility.Visible;
            else plotPanelLocal.direct2DControl.Visibility = Visibility.Collapsed;
            plotPanelLocal.InvalidateMeasure();
        }

        public PlotPanel()
        {
            ClipToBounds = true;
            // Add Canvas objects
            this.Background = Brushes.White; this.HorizontalAlignment = HorizontalAlignment.Center; this.VerticalAlignment = VerticalAlignment.Center;
            Canvas = new Canvas();
            BackgroundCanvas = new Canvas();
            this.Children.Add(Canvas);
            this.Children.Add(BackgroundCanvas);
            //
            Canvas.ClipToBounds = true;
            Canvas.SetValue(Grid.ZIndexProperty, 100);
            BackgroundCanvas.SetValue(Grid.ZIndexProperty, 50);
            axes = new Axes2D(this);
            this.Children.Add(axes);
            axes.SetValue(Grid.ZIndexProperty, 300);
            // note that individual axes have index of 200

            LinearGradientBrush background = new LinearGradientBrush();
            background.StartPoint = new Point(0, 0); background.EndPoint = new Point(1, 1);
            background.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            background.GradientStops.Add(new GradientStop(Colors.LightGray, 1.0));
            Canvas.Background = Brushes.Transparent;
            BackgroundCanvas.Background = background;
            direct2DControl = null;
            //
            this.CreateLegends();
            if (!(this is ColourBarPanel)) this.AddInteractionEvents();
            this.AddSelectionRectangle();
            this.InitialiseChildenCollection();
            marginChangeTimer = new DispatcherTimer(TimeSpan.FromSeconds(0.0), DispatcherPriority.Normal, marginChangeTimer_Tick, this.Dispatcher);
        }

        Size sizeOnMeasure;// = new Size();
        Size sizeAfterMeasure;// = new Size();

        protected override Size MeasureOverride(Size availableSize)
        {
            Stopwatch watch = new Stopwatch(); watch.Start();
            sizeOnMeasure = availableSize;
            var allAxes = axes.XAxes.Concat(axes.YAxes);
            axes.Measure(availableSize);
            foreach (Axis2D axis in allAxes)
            {
                axis.UpdateAndMeasureLabels();
            }

            //watch.Start();
            double test1 = watch.ElapsedMilliseconds;
            //watch.Stop();
            annotationsLeft.Measure(availableSize);
            annotationsRight.Measure(availableSize);
            annotationsTop.Measure(availableSize);
            annotationsBottom.Measure(availableSize);
            //
            availableSize.Height = Math.Min(availableSize.Height, 10000);
            availableSize.Width = Math.Min(availableSize.Width, 10000);
            double test2 = watch.ElapsedMilliseconds;
            MeasureAxes(availableSize);
            //
            Canvas.Measure(new Size(canvasLocation.Width, canvasLocation.Height));
            BackgroundCanvas.Measure(new Size(canvasLocation.Width, canvasLocation.Height));
            availableSize.Height = axesRegionSize.Height + annotationsTop.DesiredSize.Height + annotationsBottom.DesiredSize.Height;
            availableSize.Width = axesRegionSize.Width + annotationsLeft.DesiredSize.Width + annotationsRight.DesiredSize.Width;
            sizeAfterMeasure = availableSize;
            double test3 = watch.ElapsedMilliseconds;
            return availableSize;
        }

        /// <summary>
        /// Render the Axes according to the room available.
        /// </summary>
        /// <param name="availableSize"></param>
        protected void MeasureAxes(Size availableSize)
        {
            // Allow legends their widths.
            axes.Measure(availableSize);
            showAnnotationsLeft = false;
            showAnnotationsRight = false;
            showAnnotationsTop = false;
            showAnnotationsBottom = false;
            double startX = 0; double startY = 0;
            double endX = availableSize.Width; double endY = availableSize.Height;
            entireWidth = 0; entireHeight = 0;
            offsetX = 0; offsetY = 0;
            if ((endX - startX) > (annotationsLeft.DesiredSize.Width + 1))
            {
                showAnnotationsLeft = true;
                startX += annotationsLeft.DesiredSize.Width;
                entireWidth += annotationsLeft.DesiredSize.Width;
                offsetX += annotationsLeft.DesiredSize.Width;
            }
            if ((endX - startX) > (annotationsRight.DesiredSize.Width + 1))
            {
                showAnnotationsRight = true;
                endX -= annotationsRight.DesiredSize.Width;
                entireWidth += annotationsRight.DesiredSize.Width;
            }
            if ((endY - startY) > (annotationsTop.DesiredSize.Height + 1))
            {
                showAnnotationsTop = true;
                startY += annotationsTop.DesiredSize.Height;
                entireHeight += annotationsTop.DesiredSize.Height;
                offsetY += annotationsTop.DesiredSize.Height;
            }
            if ((endY - startY) > (annotationsBottom.DesiredSize.Height + 1))
            {
                showAnnotationsBottom = true;
                endY -= annotationsBottom.DesiredSize.Height;
                entireHeight += annotationsBottom.DesiredSize.Height;
            }
            Rect available = new Rect(startX, 0, endX - startX, endY - startY);
            bool axesEqual = (bool)this.GetValue(EqualAxesProperty);
            // Calculates the axes positions, positions labels
            // and updates the graphToAxesCanvas transform.
            Rect canvasLocationWithinAxes;
            if (dragging)
                axes.UpdateAxisPositionsOffsetOnly(available, out canvasLocation, out axesRegionSize);
            else
            {
                axes.MeasureAxesFull(new Size(available.Width, available.Height), out canvasLocationWithinAxes, out axesRegionSize);
                canvasLocation = canvasLocationWithinAxes;
                //axesCanvasLocation = new Rect(new Point(0, 0), requiredSize);
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Stopwatch watch = new Stopwatch(); watch.Start();
            if (!(finalSize == sizeOnMeasure || finalSize == sizeAfterMeasure))
            {
                MeasureAxes(finalSize);
            }
            canvasLocation = new Rect(canvasLocation.X, canvasLocation.Y, canvasLocation.Width, canvasLocation.Height);
            Rect axesRegionLocation = new Rect(0, 0, axesRegionSize.Width, axesRegionSize.Height);
            double entireWidth = this.entireWidth;
            double entireHeight = this.entireHeight;
            double offsetX = this.offsetX;
            double offsetY = this.offsetY;
            entireWidth += axesRegionSize.Width;
            entireHeight += axesRegionSize.Height;
            offsetX += (finalSize.Width - entireWidth) / 2;
            offsetY += (finalSize.Height - entireHeight) / 2;
            axesRegionLocation.X += offsetX;
            canvasLocation.X += offsetX;
            axesRegionLocation.Y += offsetY;
            canvasLocation.Y += offsetY;

            axes.RenderEachAxis();
            BeforeArrange();

            axes.Arrange(canvasLocation);
            axes.InvalidateVisual();
            // We also arrange each Axis in the same location.
            foreach (Axis2D axis in axes.XAxes) axis.Arrange(axesRegionLocation);
            foreach (Axis2D axis in axes.YAxes) axis.Arrange(axesRegionLocation);

            BackgroundCanvas.Arrange(canvasLocation);
            Canvas.Arrange(canvasLocation);
            if (direct2DControl != null) direct2DControl.Arrange(canvasLocation);
            BackgroundCanvas.InvalidateVisual();
            Canvas.InvalidateVisual();

            if (showAnnotationsLeft)
            {
                Rect annotationsLeftRect = new Rect(new Point(axesRegionLocation.Left - annotationsLeft.DesiredSize.Width, axesRegionLocation.Top),
                    new Point(axesRegionLocation.Left, axesRegionLocation.Bottom));
                annotationsLeft.Arrange(annotationsLeftRect);
            }
            if (showAnnotationsRight)
            {
                Rect annotationsRightRect = new Rect(new Point(axesRegionLocation.Right, axesRegionLocation.Top),
                    new Point(axesRegionLocation.Right + annotationsRight.DesiredSize.Width, axesRegionLocation.Bottom));
                annotationsRight.Arrange(annotationsRightRect);
            }
            else annotationsRight.Arrange(new Rect());
            if (showAnnotationsTop)
            {
                Rect annotationsTopRect = new Rect(new Point(axesRegionLocation.Left, axesRegionLocation.Top - annotationsTop.DesiredSize.Height),
                    new Point(axesRegionLocation.Right, axesRegionLocation.Top));
                annotationsTop.Arrange(annotationsTopRect);
            }
            if (showAnnotationsBottom)
            {
                Rect annotationsBottomRect = new Rect(new Point(axesRegionLocation.Left, axesRegionLocation.Bottom),
                    new Point(axesRegionLocation.Right, axesRegionLocation.Bottom + annotationsBottom.DesiredSize.Height));
                annotationsBottom.Arrange(annotationsBottomRect);
            }
            // Finally redraw axes lines
            watch.Stop();
            return finalSize;
        }

        // Called just before arrange. Uses include giving children a chance to
        // rearrange their geometry in the light of the updated transforms.
        protected virtual void BeforeArrange()
        {
            foreach (Plot2DItem child in plotItems)
            {
                child.BeforeArrange();
            }
        }
    }
}