using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal class ImageTexture : IDisposable {
    private static readonly FloatColor NoColor = FloatColor.Transparent;

    private readonly Image<Rgba32> _bitmap;
    private readonly int _left;
    private readonly int _right;
    private readonly int _top;
    private readonly int _bottom;

    public ImageTexture(Image<Rgba32> source, Rectangle rectangle) {
        _bitmap = source.Resized(rectangle.Size);
        _left = rectangle.Left;
        _right = rectangle.Right;
        _top = rectangle.Top;
        _bottom = rectangle.Bottom;
    }

    public FloatColor GetPixel(int x, int y) {
        if (x < _left || x >= _right || y < _top || y >= _bottom) return NoColor;
        
        return FloatColor.FromColor(_bitmap[x - _left, y - _top]);
    }

    public void Dispose() {
        _bitmap.Dispose();
    }
}