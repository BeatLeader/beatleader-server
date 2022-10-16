using System.Numerics;

public struct Matrix3x3 {
    private Vector3 _c0;
    private Vector3 _c1;
    private Vector3 _c2;

    public Matrix3x3(Vector3 c0, Vector3 c1, Vector3 c2) {
        _c0 = c0;
        _c1 = c1;
        _c2 = c2;
    }

    public Matrix3x3(
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22
    ) {
        _c0 = new Vector3(m00, m10, m20);
        _c1 = new Vector3(m01, m11, m21);
        _c2 = new Vector3(m02, m12, m22);
    }

    public static Vector3 Mul(Matrix3x3 a, Vector3 b) {
        return a._c0 * b.X + a._c1 * b.Y + a._c2 * b.Z;
    }

    public static Vector3 Mul(Vector3 a, Matrix3x3 b) {
        return new Vector3(
            a.X * b._c0.X + a.Y * b._c0.Y + a.Z * b._c0.Z,
            a.X * b._c1.X + a.Y * b._c1.Y + a.Z * b._c1.Z,
            a.X * b._c2.X + a.Y * b._c2.Y + a.Z * b._c2.Z
        );
    }
}