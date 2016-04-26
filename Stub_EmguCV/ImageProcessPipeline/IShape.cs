using System.Drawing;

namespace Stub_EmguCV
{
    public interface IShape
    {
        PointF[] Points { get; }

        RectangleF BoundingBox { get; }

        float BoundingBoxArea { get; }
    }
}