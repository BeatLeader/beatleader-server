using System;
using System.Drawing;
using System.Numerics;

internal class RoundedRectangleTexture {
    private static readonly FloatColor NoColor = new();

    private readonly int _left;
    private readonly int _right;
    private readonly int _top;
    private readonly int _bottom;

    private readonly FloatColor _color;
    private readonly Vector2 _rectangleCenter;
    private readonly Vector2 _localCornerCenter;
    private readonly int _maximalRadius;

    public RoundedRectangleTexture(FloatColor color, Rectangle rectangle, int radius) {
        _left = rectangle.Left;
        _right = rectangle.Right;
        _top = rectangle.Top;
        _bottom = rectangle.Bottom;

        var halfWidth = (rectangle.Width - 1) / 2f;
        var halfHeight = (rectangle.Height - 1) / 2f;

        _color = color;
        _maximalRadius = radius + 1;
        _rectangleCenter = new Vector2(rectangle.X + halfWidth, rectangle.Y + halfHeight);
        _localCornerCenter = new Vector2(halfWidth - radius, halfHeight - radius);
    }

    public FloatColor GetPixel(int x, int y) {
        if (x < _left || x >= _right || y < _top || y >= _bottom) return NoColor;

        var relativePos = new Vector2(
            MathF.Abs(x - _rectangleCenter.X) - _localCornerCenter.X,
            MathF.Abs(y - _rectangleCenter.Y) - _localCornerCenter.Y
        );
        if (relativePos.X < 0 || relativePos.Y < 0) return _color;

        var ratio = _maximalRadius - MathF.Sqrt(relativePos.X * relativePos.X + relativePos.Y * relativePos.Y);
        if (ratio < 0) return NoColor;
        if (ratio > 1) ratio = 1;
        var col = _color;
        col.A *= ratio;
        return col;
    }
}