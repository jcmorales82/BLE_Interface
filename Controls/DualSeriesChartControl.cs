using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BLE_Interface.Controls
{
    /// <summary>
    /// Chart displaying two series: raw (scatter points) + processed (line)
    /// </summary>
    public class DualSeriesChartControl : UserControl
    {
        private readonly SKElement _skElement;
        private DualRingBuffer _data;
        private SKPaint _rawPaint;
        private SKPaint _processedPaint;
        private SKPaint _gridPaint;
        private SKPaint _textPaint;

        private double _minY = 0;
        private double _maxY = 5000;
        private double _windowSize = 1500;
        private bool _autoScaleY = true;
        private double _currentDisplayMinY = double.MaxValue;
        private double _currentDisplayMaxY = double.MinValue;
        private int _framesSinceLastScale = 0;
        private double _sampleRate = 25.0;
        private double _currentIncrement = 100;
        private double[] _yAxisIncrements = new double[] { 100, 200, 300, 500, 1000, 2000, 5000, 10000 };

        public double WindowSize
        {
            get => _windowSize;
            set { _windowSize = value; _skElement?.InvalidateVisual(); }
        }

        public double SampleRate
        {
            get => _sampleRate;
            set { _sampleRate = value; _skElement?.InvalidateVisual(); }
        }

        public double MinY
        {
            get => _minY;
            set { _minY = value; _skElement?.InvalidateVisual(); }
        }

        public double MaxY
        {
            get => _maxY;
            set { _maxY = value; _skElement?.InvalidateVisual(); }
        }

        public bool AutoScaleY
        {
            get => _autoScaleY;
            set { _autoScaleY = value; _skElement?.InvalidateVisual(); }
        }

        public double[] YAxisIncrements
        {
            get => _yAxisIncrements;
            set { _yAxisIncrements = value; }
        }

        public SKColor RawColor { get; set; } = SKColors.Red;
        public SKColor ProcessedColor { get; set; } = SKColors.Cyan;
        public bool ShowGrid { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public string Title { get; set; } = "Breath Data";

        public DualSeriesChartControl()
        {
            _skElement = new SKElement();
            _skElement.PaintSurface += OnPaintSurface;
            Content = _skElement;

            _data = new DualRingBuffer(10000);

            _rawPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = RawColor,
                IsAntialias = true
            };

            _processedPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                Color = ProcessedColor,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            _gridPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                Color = SKColors.Gray.WithAlpha(50),
                IsAntialias = false
            };

            _textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 13,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
        }

        public void AddPoint(double x, double rawY, double processedY)
        {
            _data.Add(x, rawY, processedY);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _skElement.InvalidateVisual();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        public void Clear()
        {
            _data.Clear();
            _framesSinceLastScale = 0;
            _currentDisplayMinY = double.MaxValue;
            _currentDisplayMaxY = double.MinValue;
            _currentIncrement = _yAxisIncrements[0];

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _skElement.InvalidateVisual();
            }));
        }

        public int Count => _data.Count;

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var surface = e.Surface;
            var canvas = surface.Canvas;
            var info = e.Info;

            canvas.Clear(SKColors.Transparent);

            if (_data.Count < 2) return;

            var (rawPoints, procPoints, minX, maxX, minY, maxY, actualMaxX) = _data.GetVisibleData(_windowSize);

            if (rawPoints.Length < 2) return;

            // Adaptive Y-axis scaling
            if (_autoScaleY)
            {
                if (_currentDisplayMinY == double.MaxValue)
                {
                    _currentDisplayMinY = minY;
                    _currentDisplayMaxY = maxY;
                    _framesSinceLastScale = 0;
                }

                _framesSinceLastScale++;
                bool needsImmediateScale = (minY < _minY || maxY > _maxY);

                if (_framesSinceLastScale >= 10 || _minY == 0 || needsImmediateScale)
                {
                    _framesSinceLastScale = 0;

                    _currentDisplayMinY = minY;
                    _currentDisplayMaxY = maxY;

                    double range = _currentDisplayMaxY - _currentDisplayMinY;
                    if (range < 1) range = 1;

                    double[] increments = _yAxisIncrements;
                    double selectedIncrement = increments[0];

                    foreach (var inc in increments)
                    {
                        selectedIncrement = inc;
                        if (range <= inc * 10)
                        {
                            break;
                        }
                    }

                    _currentIncrement = selectedIncrement;
                    _minY = Math.Floor(_currentDisplayMinY / _currentIncrement) * _currentIncrement;

                    double rangeNeeded = _currentDisplayMaxY - _minY;
                    int ticksNeeded = (int)Math.Ceiling(rangeNeeded / _currentIncrement);
                    if (ticksNeeded < 5) ticksNeeded = 5;
                    if (ticksNeeded > 10) ticksNeeded = 10;

                    _maxY = _minY + (_currentIncrement * ticksNeeded);
                }
            }
            else
            {
                // For fixed scale, use appropriate increment
                _currentIncrement = _yAxisIncrements[0];
            }

            float marginLeft = ShowLabels ? 60f : 10f;
            float marginRight = 10f;
            float marginTop = 20f; // Extra space for title
            float marginBottom = ShowLabels ? 30f : 10f;

            float chartWidth = info.Width - marginLeft - marginRight;
            float chartHeight = info.Height - marginTop - marginBottom;
            float chartLeft = marginLeft;
            float chartTop = marginTop;

            // Draw title
            using (var font = new SKFont())
            {
                font.Size = 14;
                font.Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
                var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawText(Title, chartLeft, 15, font, titlePaint);
            }

            if (ShowGrid)
            {
                DrawGrid(canvas, chartLeft, chartTop, chartWidth, chartHeight);
            }

            _rawPaint.Color = RawColor;
            _processedPaint.Color = ProcessedColor;

            // Draw processed line first (background)
            using (var path = new SKPath())
            {
                bool first = true;
                for (int i = 0; i < procPoints.Length; i++)
                {
                    var point = procPoints[i];
                    float screenX = MapValue(point.X, minX, maxX, chartLeft, chartLeft + chartWidth);
                    float screenY = MapValue(point.ProcessedY, _minY, _maxY, chartTop + chartHeight, chartTop);

                    if (first)
                    {
                        path.MoveTo(screenX, screenY);
                        first = false;
                    }
                    else
                    {
                        path.LineTo(screenX, screenY);
                    }
                }
                canvas.DrawPath(path, _processedPaint);
            }

            // Draw raw scatter points on top
            for (int i = 0; i < rawPoints.Length; i++)
            {
                var point = rawPoints[i];
                float screenX = MapValue(point.X, minX, maxX, chartLeft, chartLeft + chartWidth);
                float screenY = MapValue(point.RawY, _minY, _maxY, chartTop + chartHeight, chartTop);
                canvas.DrawCircle(screenX, screenY, 3, _rawPaint);
            }

            if (ShowLabels)
            {
                DrawLabels(canvas, chartLeft, chartTop, chartWidth, chartHeight, minX, maxX, actualMaxX);
            }
        }

        private void DrawGrid(SKCanvas canvas, float left, float top, float width, float height)
        {
            for (int i = 0; i <= 5; i++)
            {
                float x = left + (width * i / 5);
                canvas.DrawLine(x, top, x, top + height, _gridPaint);
            }

            for (int i = 0; i <= 5; i++)
            {
                float y = top + (height * i / 5);
                canvas.DrawLine(left, y, left + width, y, _gridPaint);
            }
        }

        private void DrawLabels(SKCanvas canvas, float left, float top, float width, float height, double minX, double maxX, double actualMaxX)
        {
            using (var font = new SKFont())
            {
                font.Size = _textPaint.TextSize;
                font.Typeface = _textPaint.Typeface;

                // Y-axis labels
                double yStart = Math.Ceiling(_minY / _currentIncrement) * _currentIncrement;
                for (double value = yStart; value <= _maxY; value += _currentIncrement)
                {
                    float y = MapValue(value, _minY, _maxY, top + height, top);
                    string label = value.ToString("F0");
                    float textWidth = font.MeasureText(label, _textPaint);
                    canvas.DrawText(label, left - textWidth - 5, y + 4, font, _textPaint);
                }

                // X-axis labels
                double timeIncrement = 10;
                double counterIncrement = timeIncrement * _sampleRate;
                bool staticMode = actualMaxX <= _windowSize;

                if (staticMode)
                {
                    for (double counter = 0; counter <= _windowSize; counter += counterIncrement)
                    {
                        float x = MapValue(counter, minX, maxX, left, left + width);
                        string label = CounterToTimeString(counter);
                        float textWidth = font.MeasureText(label, _textPaint);
                        canvas.DrawText(label, x - textWidth / 2, top + height + 18, font, _textPaint);
                    }
                }
                else
                {
                    double firstCounter = Math.Floor(minX / counterIncrement) * counterIncrement;
                    for (double counter = firstCounter; counter <= actualMaxX; counter += counterIncrement)
                    {
                        if (counter >= minX)
                        {
                            float x = MapValue(counter, minX, maxX, left, left + width);
                            string label = CounterToTimeString(counter);
                            float textWidth = font.MeasureText(label, _textPaint);
                            canvas.DrawText(label, x - textWidth / 2, top + height + 18, font, _textPaint);
                        }
                    }
                }
            }
        }

        private string CounterToTimeString(double counter)
        {
            double seconds = counter / _sampleRate;
            if (seconds < 60) return $"{(int)seconds}s";
            else if (seconds < 3600)
            {
                int mins = (int)(seconds / 60);
                int secs = (int)(seconds % 60);
                return $"{mins}:{secs:D2}";
            }
            else
            {
                int hours = (int)(seconds / 3600);
                int mins = (int)((seconds % 3600) / 60);
                int secs = (int)(seconds % 60);
                return $"{hours}:{mins:D2}:{secs:D2}";
            }
        }

        private float MapValue(double value, double fromMin, double fromMax, float toMin, float toMax)
        {
            if (fromMax == fromMin) return toMin;
            return (float)(toMin + (value - fromMin) * (toMax - toMin) / (fromMax - fromMin));
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _skElement.InvalidateVisual();
        }

        public void Dispose()
        {
            _rawPaint?.Dispose();
            _processedPaint?.Dispose();
            _gridPaint?.Dispose();
            _textPaint?.Dispose();
        }

        private class DualRingBuffer
        {
            private readonly DualDataPoint[] _buffer;
            private int _head = 0;
            private int _count = 0;
            private readonly object _lock = new object();

            public int Count => _count;

            public struct DualDataPoint
            {
                public double X;
                public double RawY;
                public double ProcessedY;
            }

            public DualRingBuffer(int capacity)
            {
                _buffer = new DualDataPoint[capacity];
            }

            public void Add(double x, double rawY, double processedY)
            {
                lock (_lock)
                {
                    _buffer[_head] = new DualDataPoint { X = x, RawY = rawY, ProcessedY = processedY };
                    _head = (_head + 1) % _buffer.Length;
                    if (_count < _buffer.Length)
                        _count++;
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _head = 0;
                    _count = 0;
                }
            }

            public (DualDataPoint[] rawPoints, DualDataPoint[] procPoints, double minX, double maxX, double minY, double maxY, double actualMaxX) GetVisibleData(double windowSize)
            {
                lock (_lock)
                {
                    if (_count == 0)
                        return (Array.Empty<DualDataPoint>(), Array.Empty<DualDataPoint>(), 0, 0, 0, 0, 0);

                    int lastIndex = (_head - 1 + _buffer.Length) % _buffer.Length;
                    double actualMaxX = _buffer[lastIndex].X;
                    double maxX = actualMaxX;

                    double minX;
                    if (actualMaxX <= windowSize)
                    {
                        minX = 0;
                        maxX = windowSize;
                    }
                    else
                    {
                        minX = actualMaxX - windowSize;
                    }

                    int visibleCount = 0;
                    double minY = double.MaxValue;
                    double maxY = double.MinValue;

                    for (int i = 0; i < _count; i++)
                    {
                        int index = (_head - _count + i + _buffer.Length) % _buffer.Length;
                        var point = _buffer[index];

                        if (point.X >= minX)
                        {
                            visibleCount++;
                            if (point.RawY < minY) minY = point.RawY;
                            if (point.RawY > maxY) maxY = point.RawY;
                            if (point.ProcessedY < minY) minY = point.ProcessedY;
                            if (point.ProcessedY > maxY) maxY = point.ProcessedY;
                        }
                    }

                    if (visibleCount == 0)
                        return (Array.Empty<DualDataPoint>(), Array.Empty<DualDataPoint>(), minX, maxX, 0, 0, actualMaxX);

                    var result = new DualDataPoint[visibleCount];
                    int resultIndex = 0;

                    for (int i = 0; i < _count; i++)
                    {
                        int index = (_head - _count + i + _buffer.Length) % _buffer.Length;
                        var point = _buffer[index];

                        if (point.X >= minX)
                        {
                            result[resultIndex++] = point;
                        }
                    }

                    return (result, result, minX, maxX, minY, maxY, actualMaxX);
                }
            }
        }
    }
}