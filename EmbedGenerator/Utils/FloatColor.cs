using System;
using System.Drawing;
using System.Numerics;

public struct FloatColor {
    #region Constructor

    public float R;
    public float G;
    public float B;
    public float A;

    public FloatColor(float red, float green, float blue, float alpha)
    {
        R = red;
        G = green;
        B = blue;
        A = alpha;
    }

    #endregion

    #region Static

    public static FloatColor Transparent => new();
    public static FloatColor White => new(1, 1, 1, 1);
    public static FloatColor Black => new(0, 0, 0, 1);

    public static FloatColor LerpClamped(FloatColor a, FloatColor b, float t)
    {
        var invT = 1.0f - t;

        return t switch
        {
            < 0 => a,
            > 1 => b,
            _ => new FloatColor(
                a.R * invT + b.R * t,
                a.G * invT + b.G * t,
                a.B * invT + b.B * t,
                a.A * invT + b.A * t
            )
        };
    }

    #endregion

    #region Cast

    public static FloatColor FromColor(Color color)
    {
        return new FloatColor(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f
        );
    }

    public Color ToColor()
    {
        return Color.FromArgb(
            ConvertToByte(A),
            ConvertToByte(R),
            ConvertToByte(G),
            ConvertToByte(B)
        );
    }

    private static byte ConvertToByte(float value)
    {
        return value switch
        {
            > 1f => 0xFF,
            < 0f => 0x00,
            _ => (byte)(value * 0xFF)
        };
    }

    #endregion

    #region Math

    public void Add(FloatColor color)
    {
        R += color.R;
        G += color.G;
        B += color.B;
        A += color.A;
    }

    public void Multiply(FloatColor color)
    {
        R *= color.R;
        G *= color.G;
        B *= color.B;
        A *= color.A;
    }

    public void AlphaBlend(FloatColor color)
    {
        var invAlpha = 1 - color.A;
        R = color.R * color.A + R * invAlpha;
        G = color.G * color.A + G * invAlpha;
        B = color.B * color.A + B * invAlpha;
        A = color.A + A * invAlpha;
    }

    public void AlphaMask(FloatColor color)
    {
        A *= color.A;
    }

    #endregion

    #region HSB Transform

    private static Matrix3x3 RGB_YIQ = new(
        0.299f, 0.587f, 0.114f,
        0.5959f, -0.275f, -0.3213f,
        0.2115f, -0.5227f, 0.3112f
    );

    private static Matrix3x3 YIQ_RGB = new(
        1f, 0.956f, 0.619f,
        1f, -0.272f, -0.647f,
        1f, -1.106f, 1.702f
    );

    public void ApplyHsbTransform(float hueShiftRadians, float saturation, float brightness)
    {
        var rgb = new Vector3(R, G, B);
        var YIQ = Matrix3x3.Mul(RGB_YIQ, rgb);
        var hue = MathF.Atan2(YIQ.Z, YIQ.Y) - hueShiftRadians;
        var chroma = MathF.Sqrt(YIQ.Y * YIQ.Y + YIQ.Z * YIQ.Z) * saturation;
        var Y = YIQ.X + brightness;
        var I = chroma * MathF.Cos(hue);
        var Q = chroma * MathF.Sin(hue);
        var result = Matrix3x3.Mul(YIQ_RGB, new Vector3(Y, I, Q));
        R = result.X;
        G = result.Y;
        B = result.Z;
    }

    #endregion
}