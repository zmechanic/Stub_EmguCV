using System;
using System.Drawing;

namespace Stub_EmguCV
{
    /// <summary>
    /// Represents 4 points closed shape.
    /// </summary>
    public struct Quadrilateral : IShape
    {
        private readonly PointF[] _points;

        /// <summary>
        /// Constructs a new instance of <see cref="Quadrilateral"/>.
        /// </summary>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="p3"></param>
        public Quadrilateral(PointF p0, PointF p1, PointF p2, PointF p3)
        {
            _points = new PointF[4];
            _points[0] = p0;
            _points[1] = p1;
            _points[2] = p2;
            _points[3] = p3;
        }

        /// <summary>
        /// Constructs a new instance of <see cref="Quadrilateral"/> by cloning an existing instance of <see cref="Quadrilateral"/>.
        /// </summary>
        /// <param name="sourceQuadrilateral"></param>
        public Quadrilateral(Quadrilateral sourceQuadrilateral)
            : this(sourceQuadrilateral.P0, sourceQuadrilateral.P1, sourceQuadrilateral.P2, sourceQuadrilateral.P3)
        {
        }

        public bool IsEmpty => _points == null;

        public PointF P0 => _points[0];
        public PointF P1 => _points[1];
        public PointF P2 => _points[2];
        public PointF P3 => _points[3];

        public PointF[] Points => _points;
        public Rectangle BoundingBoxInt => Rectangle.FromLTRB((int)MinX, (int)MinY, (int)MaxX, (int)MaxY);
        public RectangleF BoundingBox => RectangleF.FromLTRB(MinX, MinY, MaxX, MaxY);
        public float BoundingBoxArea => BoundingBox.Width * BoundingBox.Height;

        public float MinX => Math.Min(Math.Min(Math.Min(P0.X, P1.X), P2.X), P3.X);
        public float MinY => Math.Min(Math.Min(Math.Min(P0.Y, P1.Y), P2.Y), P3.Y);
        public float MaxX => Math.Max(Math.Max(Math.Max(P0.X, P1.X), P2.X), P3.X);
        public float MaxY => Math.Max(Math.Max(Math.Max(P0.Y, P1.Y), P2.Y), P3.Y);

        public float Width
        {
            get
            {
                var x01 = Math.Abs(P0.X - P1.X);
                var x12 = Math.Abs(P1.X - P2.X);
                var x23 = Math.Abs(P2.X - P3.X);
                var x30 = Math.Abs(P3.X - P0.X);

                return Math.Max(Math.Max(Math.Max(x01, x12), x23), x30);
            }
        }

        public float Height
        {
            get
            {
                var y01 = Math.Abs(P0.Y - P1.Y);
                var y12 = Math.Abs(P1.Y - P2.Y);
                var y23 = Math.Abs(P2.Y - P3.Y);
                var y30 = Math.Abs(P3.Y - P0.Y);

                return Math.Max(Math.Max(Math.Max(y01, y12), y23), y30);
            }
        }
    }
}