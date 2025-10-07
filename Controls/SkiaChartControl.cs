using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BLE_Interface.Controls
{
    public class SkiaChartControl : UserControl
    {
        private readonly SKElement _skElement;
        private RingBuffer _data;
        private SKPaint _linePaint;
        private SKPaint _gridPaint;
        private SKPaint _textPaint;

        private double _minY = 0;
        private double _maxY = 5000;
        private double _windowSize = 1500;
        private bool _autoScaleY = false;
        private double _currentDisplayMinY = double.MaxValue;
        private double _currentDisplayMaxY = double.MinValue;
        private int _framesSinceLastScale = 0;
        private double _sampleRate = 25.0;
        private double _currentIncrement = 100;

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

        public double WindowSize
        {
            get => _windowSize;
            set { _windowSize = value; _skElement?.InvalidateVisual(); }
        }

        public bool AutoScaleY
        {
            get => _autoScaleY;
            set { _autoScaleY = value; _skElement?.InvalidateVisual(); }
        }

        public double SampleRate
        {
            get => _sampleRate;
            set { _sampleRate = value; _skElement?.InvalidateVisual(); }
        }

        public SKColor LineColor { get; set; } = SKColors.LightYellow;
        public float LineWidth { get; set; } = 2f;
        public bool ShowGrid { get; set; } = true;
        public bool ShowLabels { get; set; } = true;

        public SkiaChartControl()
        {
            _skElement = new SKElement();
            _skElement.PaintSurface += OnPaintSurface;
            Content = _skElement;

            _data = new RingBuffer(10000);

            _linePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = LineWidth,
                Color = LineColor,
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

        public void AddPoint(double x, double y)
        {
            _data.Add(x, y);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _skElement.InvalidateVisual();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        public void Clear()
        {
            _data.Clear();
            _framesSinceLastScale = 0;

            if (_autoScaleY)
            {
                _currentDisplayMinY = double.MaxValue;
                _currentDisplayMaxY = double.MinValue;
                _currentIncrement = 100;
            }

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

            var (points, minX, maxX, minY, maxY, actualMaxX) = _data.GetVisibleData(_windowSize);

            if (points.Length < 2) return;

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

                // Check if data exceeds current bounds - scale immediately if so
                bool needsImmediateScale = (minY < _minY || maxY > _maxY);

                if (_framesSinceLastScale >= 10 || _minY == 0 || needsImmediateScale)
                {
                    _framesSinceLastScale = 0;

                    _currentDisplayMinY = minY;
                    _currentDisplayMaxY = maxY;

                    double range = _currentDisplayMaxY - _currentDisplayMinY;
                    if (range < 1) range = 1;

                    double[] increments = { 100, 200, 300, 500, 1000, 2000, 5000, 10000 };
                    double selectedIncrement = 100;

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
                _currentDisplayMinY = _minY;
                _currentDisplayMaxY = _maxY;
            }

            float marginLeft = ShowLabels ? 60f : 10f;
            float marginRight = 10f;
            float marginTop = 10f;
            float marginBottom = ShowLabels ? 30f : 10f;

            float chartWidth = info.Width - marginLeft - marginRight;
            float chartHeight = info.Height - marginTop - marginBottom;
            float chartLeft = marginLeft;
            float chartTop = marginTop;

            if (ShowGrid)
            {
                DrawGrid(canvas, chartLeft, chartTop, chartWidth, chartHeight, minX, maxX);
            }

            _linePaint.Color = LineColor;
            _linePaint.StrokeWidth = LineWidth;

            using (var path = new SKPath())
            {
                bool first = true;

                for (int i = 0; i < points.Length; i++)
                {
                    var point = points[i];
                    float screenX = MapValue(point.X, minX, maxX, chartLeft, chartLeft + chartWidth);
                    float screenY = MapValue(point.Y, _minY, _maxY, chartTop + chartHeight, chartTop);

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

                canvas.DrawPath(path, _linePaint);
            }

            if (ShowLabels)
            {
                DrawLabels(canvas, chartLeft, chartTop, chartWidth, chartHeight, minX, maxX, actualMaxX);
            }
        }

        private void DrawGrid(SKCanvas canvas, float left, float top, float width, float height, double minX, double maxX)
        {
            int numVerticalLines = 5;
            for (int i = 0; i <= numVerticalLines; i++)
            {
                float x = left + (width * i / numVerticalLines);
                canvas.DrawLine(x, top, x, top + height, _gridPaint);
            }

            int numHorizontalLines = 5;
            for (int i = 0; i <= numHorizontalLines; i++)
            {
                float y = top + (height * i / numHorizontalLines);
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
                    float textWidth = _textPaint.MeasureText(label);
                    canvas.DrawText(label, left - textWidth - 5, y + 4, font, _textPaint);
                }

                // X-axis labels with static/sliding mode
                double windowSeconds = _windowSize / _sampleRate;

                double timeIncrement;
                if (windowSeconds <= 60) timeIncrement = 10;
                else if (windowSeconds <= 300) timeIncrement = 30;
                else if (windowSeconds <= 600) timeIncrement = 60;
                else timeIncrement = 300;

                double counterIncrement = timeIncrement * _sampleRate;
                bool staticMode = actualMaxX <= _windowSize;

                if (staticMode)
                {
                    // Static mode: show all labels from 0 to window size
                    for (double counter = 0; counter <= _windowSize; counter += counterIncrement)
                    {
                        float x = MapValue(counter, minX, maxX, left, left + width);
                        string label = CounterToTimeString(counter);
                        float textWidth = _textPaint.MeasureText(label);
                        canvas.DrawText(label, x - textWidth / 2, top + height + 18, font, _textPaint);
                    }
                }
                else
                {
                    // Sliding mode: show labels for current window
                    double firstCounter = Math.Floor(minX / counterIncrement) * counterIncrement;

                    for (double counter = firstCounter; counter <= actualMaxX; counter += counterIncrement)
                    {
                        if (counter >= minX)
                        {
                            float x = MapValue(counter, minX, maxX, left, left + width);
                            string label = CounterToTimeString(counter);
                            float textWidth = _textPaint.MeasureText(label);
                            canvas.DrawText(label, x - textWidth / 2, top + height + 18, font, _textPaint);
                        }
                    }
                }
            }
        }

        private string CounterToTimeString(double counter)
        {
            double seconds = counter / _sampleRate;

            if (seconds < 60)
            {
                return $"{(int)seconds}s";
            }
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
            _linePaint?.Dispose();
            _gridPaint?.Dispose();
            _textPaint?.Dispose();
        }

        private class RingBuffer
        {
            private readonly DataPoint[] _buffer;
            private int _head = 0;
            private int _count = 0;
            private readonly object _lock = new object();

            public int Count => _count;

            public struct DataPoint
            {
                public double X;
                public double Y;
            }

            public RingBuffer(int capacity)
            {
                _buffer = new DataPoint[capacity];
            }

            public void Add(double x, double y)
            {
                lock (_lock)
                {
                    _buffer[_head] = new DataPoint { X = x, Y = y };
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

            public (DataPoint[] points, double minX, double maxX, double minY, double maxY, double actualMaxX) GetVisibleData(double windowSize)
            {
                lock (_lock)
                {
                    if (_count == 0)
                        return (Array.Empty<DataPoint>(), 0, 0, 0, 0, 0);

                    int lastIndex = (_head - 1 + _buffer.Length) % _buffer.Length;
                    double actualMaxX = _buffer[lastIndex].X;
                    double maxX = actualMaxX;

                    double minX;
                    if (actualMaxX <= windowSize)
                    {
                        // Static mode: first minute
                        minX = 0;
                        maxX = windowSize;
                    }
                    else
                    {
                        // Sliding mode: after first minute
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
                            if (point.Y < minY) minY = point.Y;
                            if (point.Y > maxY) maxY = point.Y;
                        }
                    }

                    if (visibleCount == 0)
                        return (Array.Empty<DataPoint>(), minX, maxX, 0, 0, actualMaxX);

                    var result = new DataPoint[visibleCount];
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

                    return (result, minX, maxX, minY, maxY, actualMaxX);
                }
            }
        }
    }
}