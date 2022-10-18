using System;
using System.Numerics;

internal class LinearGradientTexture {
    private readonly FloatColor _fromColor;
    private readonly FloatColor _toColor;
    private readonly Vector2 _from;
    private readonly Vector2 _direction;
    private readonly float _magnitude;

    public LinearGradientTexture(FloatColor fromColor, FloatColor toColor, Vector2 from, Vector2 to) {
        _fromColor = fromColor;
        _toColor = toColor;
        _from = from;
        var diff = to - from;
        _direction = Vector2.Normalize(diff);
        _magnitude = MathF.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
    }

    public FloatColor GetPixel(int x, int y) {
        var t = Vector2.Dot(new Vector2(x - _from.X, y - _from.Y), _direction) / _magnitude;
        return FloatColor.LerpClamped(_fromColor, _toColor, t);
    }
}