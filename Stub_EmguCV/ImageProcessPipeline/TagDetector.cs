using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stub_EmguCV.ImageProcessPipeline
{
    /// <summary>
    /// Class that detects if tag is present in a given collection of quadrilaterals.
    /// Quadrilaterals must be distributed over the area of suspected tag only.
    /// </summary>
    public class TagDetector
    {
        /// <summary>
        /// Result of tag detection.
        /// </summary>
        public struct TagDetectionResult
        {
            /// <summary>
            /// <see cref="Boolean.True"/> if tag is present.
            /// </summary>
            public bool IsTagPresent;

            /// <summary>
            /// Rotation angle of a tag.
            /// Angle is always between 0 and 360 degree.
            /// </summary>
            public float RotationAngle;

            /// <summary>
            /// Angle by what image need to be rotated to put markers into a normal position (both vertical markers up).
            /// </summary>
            public int ImageRotation;

            /// <summary>
            /// Indicates whether image is flipped horizontally.
            /// </summary>
            public bool IsFlippedHorizontally;
        }

        private struct MetaRect
        {
            public Quadrilateral Rect;
            public bool IsHorizontalLongMarker;
            public bool IsVerticalLongMarker;
        }

        private enum MarkersOrientation
        {
            None = -1,
            VV = 0,
            HV = 1,
            HH = 2,
            VH = 3
        }

        private readonly Size _imageSize;
        private readonly float _pixelToleranceWidth;
        private readonly float _pixelToleranceHeight;

        private readonly Matrix _rotationMatrix090 = new Matrix();
        private readonly Matrix _rotationMatrix180 = new Matrix();
        private readonly Matrix _rotationMatrix270 = new Matrix();

        /// <summary>
        /// Constructs a new instance of tag detector.
        /// </summary>
        /// <param name="imageSize">Size of the image.</param>
        public TagDetector(Size imageSize)
        {
            _imageSize = imageSize;
            _pixelToleranceWidth = _imageSize.Height * 0.05f;
            _pixelToleranceHeight = _imageSize.Height * 0.05f;

            _rotationMatrix090.RotateAt(90f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
            _rotationMatrix180.RotateAt(180f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
            _rotationMatrix270.RotateAt(270f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
        }

        /// <summary>
        /// Detects if tag is present in given image.
        /// </summary>
        /// <param name="rects">Rectangles detected in image.</param>
        /// <param name="sourceQuadrilateral">Quadrilateral in the original image from where sub-image for analysis was cut.</param>
        /// <returns></returns>
        public TagDetectionResult IsTag(Quadrilateral[] rects, Quadrilateral sourceQuadrilateral)
        {
            var angleIndex = 0;

            // Try to detect a tag with default orientation.
            var tagMarkersOrientation = IsTag(rects);

            // If tag not detected from default orientation we need to rotate it by 90 degree and try again.
            if (tagMarkersOrientation == MarkersOrientation.None)
            {
                var rotatedRects = new Quadrilateral[rects.Length];

                for (angleIndex = 1; angleIndex <= 3; angleIndex++)
                {
                    Matrix matrix = null;

                    switch (angleIndex)
                    {
                        case 1:
                            matrix = _rotationMatrix090;
                            break;
                        case 2:
                            matrix = _rotationMatrix180;
                            break;
                        case 3:
                            matrix = _rotationMatrix270;
                            break;
                    }

                    for (var i = 0; i < rects.Length; i++)
                    {
                        rotatedRects[i] = new Quadrilateral(rects[i]);
                        matrix.TransformPoints(rotatedRects[i].Points);
                    }

                    tagMarkersOrientation = IsTag(rotatedRects);
                    if (tagMarkersOrientation != MarkersOrientation.None)
                    {
                        break;
                    }
                }
            }

            if (tagMarkersOrientation != MarkersOrientation.None)
            {
                var rotatedSourceRect = sourceQuadrilateral;

                switch (angleIndex)
                {
                    case 1:
                        rotatedSourceRect = new Quadrilateral(sourceQuadrilateral.P1, sourceQuadrilateral.P2, sourceQuadrilateral.P3, sourceQuadrilateral.P0);
                        break;
                    case 2:
                        rotatedSourceRect = new Quadrilateral(sourceQuadrilateral.P2, sourceQuadrilateral.P3, sourceQuadrilateral.P0, sourceQuadrilateral.P1);
                        break;
                    case 3:
                        rotatedSourceRect = new Quadrilateral(sourceQuadrilateral.P3, sourceQuadrilateral.P0, sourceQuadrilateral.P1, sourceQuadrilateral.P2);
                        break;
                }

                var rotatedRadians01 = Math.Atan2(rotatedSourceRect.P0.X - rotatedSourceRect.P1.X, rotatedSourceRect.P0.Y - rotatedSourceRect.P1.Y);
                var rotatedAngle01 = rotatedRadians01 * (180D / Math.PI);

                var rotatedRadians03 = Math.Atan2(rotatedSourceRect.P0.X - rotatedSourceRect.P3.X, rotatedSourceRect.P0.Y - rotatedSourceRect.P3.Y);
                var rotatedAngle03 = rotatedRadians03 * (180D / Math.PI);

                var resultAngle = 0f;

                switch (tagMarkersOrientation)
                {
                    case MarkersOrientation.VV:
                        if (rotatedAngle01 > 0)
                        {
                            resultAngle = 180f - (float)rotatedAngle01;
                        }
                        else
                        {
                            resultAngle = 180f + Math.Abs((float)rotatedAngle01);
                        }

                        break;
                    case MarkersOrientation.VH:
                        if (rotatedAngle03 > 0)
                        {
                            resultAngle = 360f - (float)rotatedAngle03;
                        }
                        else
                        {
                            resultAngle = Math.Abs((float)rotatedAngle03);
                        }
                            
                        break;
                    case MarkersOrientation.HH:
                        if (rotatedAngle01 > 0)
                        {
                            resultAngle = 360f - (float)rotatedAngle01;
                        }
                        else
                        {
                            resultAngle = -(float)rotatedAngle01;
                        }

                        break;
                    case MarkersOrientation.HV:
                        resultAngle = 180f - (float)rotatedAngle03;
                        break;
                }

                if (resultAngle < 0)
                {
                    resultAngle = 360f + resultAngle;
                }

                if (resultAngle > 360f)
                {
                    resultAngle = resultAngle % 360f;
                }

                var isSourceFlippedHorizontally = sourceQuadrilateral.P0.X > sourceQuadrilateral.P2.X || sourceQuadrilateral.P1.X > sourceQuadrilateral.P3.X;
                var isSourceFlippedVertically = sourceQuadrilateral.P0.Y > sourceQuadrilateral.P2.Y || sourceQuadrilateral.P1.Y < sourceQuadrilateral.P3.Y;

                var imageRotation = (angleIndex + (int)tagMarkersOrientation) % 4;

                if (isSourceFlippedVertically || isSourceFlippedHorizontally)
                {
                    var sourceRadians01 = Math.Atan2(sourceQuadrilateral.P0.X - sourceQuadrilateral.P1.X, sourceQuadrilateral.P0.Y - sourceQuadrilateral.P1.Y);
                    var sourceRadians03 = Math.Atan2(sourceQuadrilateral.P0.X - sourceQuadrilateral.P3.X, sourceQuadrilateral.P0.Y - sourceQuadrilateral.P3.Y);

                    if (sourceRadians01 < 0 || sourceRadians03 > 0)
                    {
                        return new TagDetectionResult { IsTagPresent = true, RotationAngle = resultAngle, ImageRotation = imageRotation, IsFlippedHorizontally = true };
                    }
                }

                return new TagDetectionResult { IsTagPresent = true, RotationAngle = resultAngle, ImageRotation = imageRotation };
            }

            return new TagDetectionResult { IsTagPresent = false };
        }

        /// <summary>
        /// Detects if tag is present in given image.
        /// </summary>
        /// <param name="rects">Rectangles detected in image.</param>
        /// <returns>Orientation of markers when tag detected.</returns>
        private MarkersOrientation IsTag(Quadrilateral[] rects)
        {
            var metaRects = ClassifyRectangles(rects);
            if (!DetectMarkersPresent(metaRects))
            {
                return MarkersOrientation.None;
            }

            for (var i = 0; i < metaRects.Length; i++)
            {
                if (metaRects[i].IsHorizontalLongMarker)
                {
                    if (metaRects[i].Rect.MinX < _imageSize.Width * 0.3)
                    {
                        if (metaRects[i].Rect.MinY < _imageSize.Height * 0.25)
                        {
                            for (var j = 0; j < metaRects.Length; j++)
                            {
                                if (metaRects[j].Equals(metaRects[i]))
                                {
                                    continue;
                                }

                                if (metaRects[j].IsHorizontalLongMarker)
                                {
                                    if (Math.Abs(metaRects[j].Rect.MinY - metaRects[i].Rect.MinY) < _pixelToleranceHeight &&
                                        Math.Abs(metaRects[j].Rect.MaxY - metaRects[i].Rect.MaxY) < _pixelToleranceWidth)
                                    {
                                        if (metaRects[j].Rect.MinX > _imageSize.Width * 0.6 && metaRects[j].Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return MarkersOrientation.HH;
                                        }
                                    }
                                }

                                if (metaRects[j].IsVerticalLongMarker)
                                {
                                    if (Math.Abs(metaRects[j].Rect.MinY - metaRects[i].Rect.MinY) < _pixelToleranceHeight)
                                    {
                                        if (metaRects[j].Rect.MinX > _imageSize.Width * 0.7 && metaRects[j].Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return MarkersOrientation.HV;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (metaRects[i].IsVerticalLongMarker)
                {
                    if (metaRects[i].Rect.MinX < _imageSize.Width * 0.3)
                    {
                        if (metaRects[i].Rect.MinY < _imageSize.Height * 0.25)
                        {
                            for (var j = 0; j < metaRects.Length; j++)
                            {
                                if (metaRects[j].Equals(metaRects[i]))
                                {
                                    continue;
                                }

                                if (metaRects[j].IsHorizontalLongMarker)
                                {
                                    if (Math.Abs(metaRects[j].Rect.MinY - metaRects[i].Rect.MinY) < _pixelToleranceHeight)
                                    {
                                        if (metaRects[j].Rect.MinX > _imageSize.Width * 0.6 && metaRects[j].Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return MarkersOrientation.VH;
                                        }
                                    }
                                }

                                if (metaRects[j].IsVerticalLongMarker)
                                {
                                    if (Math.Abs(metaRects[j].Rect.MinY - metaRects[i].Rect.MinY) < _pixelToleranceHeight &&
                                        Math.Abs(metaRects[j].Rect.MaxY - metaRects[i].Rect.MaxY) < _pixelToleranceHeight)
                                    {
                                        if (metaRects[j].Rect.MinX > _imageSize.Width * 0.7 && metaRects[j].Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return MarkersOrientation.VV;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return MarkersOrientation.None;
        }

        private MetaRect[] ClassifyRectangles(Quadrilateral[] rects)
        {
            var metaRects = new MetaRect[rects.Length];

            // Classify rectangles by its orientation and size.
            for (var i = 0; i < rects.Length; i++)
            {
                var subrectWidth = rects[i].Width;
                var subrectHeight = rects[i].Height;

                // Filter markers by size.
                if ((subrectWidth > _imageSize.Width * 0.08 && subrectHeight > _imageSize.Height * 0.08) ||
                    (subrectWidth < _imageSize.Width * 0.03 || subrectHeight < _imageSize.Height * 0.03))
                {
                    continue;
                }

                var wtoh = subrectWidth / subrectHeight;
                var htow = subrectHeight / subrectWidth;

                var metaRect = new MetaRect
                {
                    Rect = rects[i],
                    IsHorizontalLongMarker = (wtoh > 2.0f) && (wtoh < 4.5f),
                    IsVerticalLongMarker = (htow > 2.0f) && (htow < 4.5f),
                };

                metaRects[i] = metaRect;
            }

            return metaRects;
        }

        private bool DetectMarkersPresent(MetaRect[] rects)
        {
            var presentHorizontalMarkerCount = 0;
            var presentVerticalMarkerCount = 0;

            // Detect some markers present.
            for (var i = 0; i < rects.Length; i++)
            {
                if (rects[i].IsHorizontalLongMarker)
                {
                    presentHorizontalMarkerCount++;
                }
                else if (rects[i].IsVerticalLongMarker)
                {
                    presentVerticalMarkerCount++;
                }
            }

            if (presentHorizontalMarkerCount >= 2 ||
                presentVerticalMarkerCount >= 2 ||
                (presentHorizontalMarkerCount >= 1 && presentVerticalMarkerCount >= 1))
            {
                return true;
            }

            return false;
        }
    }
}
