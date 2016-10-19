using System;
using System.Drawing;
using System.Drawing.Drawing2D;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace Stub_EmguCV.ImageProcessPipeline
{
    /// <summary>
    /// Class that detects if tag is present in a given collection of quadrilaterals.
    /// Quadrilaterals must be distributed over the area of suspected tag only.
    /// </summary>
    public class TagDetector : IDisposable
    {
        /// <summary>
        /// Result of tag detection.
        /// </summary>
        public struct TagDetectionResult
        {
            /// <summary>
            /// <see cref="bool.True"/> if tag is present.
            /// </summary>
            public bool IsTagPresent;

            /// <summary>
            /// Rotation angle of a tag.
            /// Angle is always between 0 and 360 degree.
            /// </summary>
            public double RotationAngle;

            /// <summary>
            /// Angle in multiples of 90 degree, by what image need to be rotated to put markers into a normal position (both vertical markers up).
            /// </summary>
            public int ImageRotation;

            /// <summary>
            /// Indicates whether image is flipped horizontally.
            /// </summary>
            public bool IsFlippedHorizontally;

            /// <summary>
            /// Orientation of markers on the image.
            /// </summary>
            public Quadrilateral[] Markers;

            /// <summary>
            /// Angle in multiples of 90 degree, by what of markers need to be rotated to correspond with upright image orientation.
            /// </summary>
            public int MarkersRotation;

            /// <summary>
            /// Level of confidence ranging from 50% to 100%.
            /// </summary>
            public double Confidence;
        }

        private struct InternalTagDetectionResult
        {
            public MarkersOrientation Orientation;
            public double Confidence;
        }

        private struct MetaRect
        {
            public Quadrilateral Rect;
            public bool IsHorizontalLongMarker;
            public bool IsVerticalLongMarker;
        }

        // ReSharper disable InconsistentNaming
        private enum MarkersOrientation
        {
            None = -1,
            VV = 0,
            HV = 1,
            HH = 2,
            VH = 3
        }
        // ReSharper restore InconsistentNaming

        private const float MarkerMinEdgeMultiplier = 0.1F;

        private readonly Size _imageSize;
        private readonly float _pixelToleranceWidth;
        private readonly float _pixelToleranceHeight;

        private readonly int _pixelsToMarkerMinEdge;
        private readonly int _pixelsToMarkerMaxEdge;

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

            _pixelToleranceWidth = _imageSize.Width * 0.05f;
            _pixelToleranceHeight = _imageSize.Height * 0.05f;

            _pixelsToMarkerMinEdge = (int)(Math.Max(_imageSize.Width, _imageSize.Height) * MarkerMinEdgeMultiplier);
            _pixelsToMarkerMaxEdge = Math.Max(_imageSize.Width, _imageSize.Height) - _pixelsToMarkerMinEdge;

            _rotationMatrix090.RotateAt(90f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
            _rotationMatrix180.RotateAt(180f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
            _rotationMatrix270.RotateAt(270f, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));
        }

        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Detects if tag is present in given image.
        /// </summary>
        /// <param name="rects">Rectangles detected in image.</param>
        /// <param name="sourceQuadrilateral">Quadrilateral in the original image from where sub-image for analysis was cut.</param>
        /// <returns></returns>
        public TagDetectionResult IsTag(Quadrilateral[] rects, Quadrilateral sourceQuadrilateral)
        {
            int angleIndex;
            MetaRect[] suspectedMarkerMetaRects = null;
            var tagDetectionInfo = new InternalTagDetectionResult();

            for (angleIndex = 0; angleIndex <= 3; angleIndex++)
            {
                Quadrilateral[] suspectedMarkerRectangles;

                if (angleIndex == 0)
                {
                    suspectedMarkerRectangles = rects;
                }
                else
                {
                    Matrix matrix;

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
                        default:
                            throw new InvalidOperationException();
                    }

                    suspectedMarkerRectangles = new Quadrilateral[rects.Length];

                    for (var i = 0; i < rects.Length; i++)
                    {
                        suspectedMarkerRectangles[i] = new Quadrilateral(rects[i]);
                        matrix.TransformPoints(suspectedMarkerRectangles[i].Points);
                    }
                }

                suspectedMarkerMetaRects = ClassifyRectangles(suspectedMarkerRectangles);
                tagDetectionInfo = IsTag(suspectedMarkerMetaRects);
                if (tagDetectionInfo.Orientation != MarkersOrientation.None)
                {
                    break;
                }
            }

            if (tagDetectionInfo.Orientation == MarkersOrientation.None)
            {
                return new TagDetectionResult { IsTagPresent = false };
            }

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

            var resultAngle = 0D;

            switch (tagDetectionInfo.Orientation)
            {
                case MarkersOrientation.VV:
                    if (rotatedAngle01 > 0)
                    {
                        resultAngle = 180D - rotatedAngle01;
                    }
                    else
                    {
                        resultAngle = 180D + Math.Abs(rotatedAngle01);
                    }

                    break;
                case MarkersOrientation.VH:
                    if (rotatedAngle03 > 0)
                    {
                        resultAngle = 360D - rotatedAngle03;
                    }
                    else
                    {
                        resultAngle = Math.Abs(rotatedAngle03);
                    }
                            
                    break;
                case MarkersOrientation.HH:
                    if (rotatedAngle01 > 0)
                    {
                        resultAngle = 360D - rotatedAngle01;
                    }
                    else
                    {
                        resultAngle = -rotatedAngle01;
                    }

                    break;
                case MarkersOrientation.HV:
                    resultAngle = 180D- rotatedAngle03;
                    break;
            }

            if (resultAngle < 0)
            {
                resultAngle = 360D + resultAngle;
            }

            if (resultAngle > 360D)
            {
                resultAngle = resultAngle % 360D;
            }

            var isSourceFlippedHorizontally = sourceQuadrilateral.P0.X > sourceQuadrilateral.P2.X || sourceQuadrilateral.P1.X > sourceQuadrilateral.P3.X;
            var isSourceFlippedVertically = sourceQuadrilateral.P0.Y > sourceQuadrilateral.P2.Y || sourceQuadrilateral.P1.Y < sourceQuadrilateral.P3.Y;

            var isImageFlipped = false;

            if (isSourceFlippedVertically || isSourceFlippedHorizontally)
            {
                var sourceRadians01 = Math.Atan2(sourceQuadrilateral.P0.X - sourceQuadrilateral.P1.X, sourceQuadrilateral.P0.Y - sourceQuadrilateral.P1.Y);
                var sourceRadians03 = Math.Atan2(sourceQuadrilateral.P0.X - sourceQuadrilateral.P3.X, sourceQuadrilateral.P0.Y - sourceQuadrilateral.P3.Y);

                if (sourceRadians01 < 0 || sourceRadians03 > 0)
                {
                    isImageFlipped = true;
                }
            }

            // ReSharper disable once PossibleNullReferenceException
            var markerRectangles = new Quadrilateral[suspectedMarkerMetaRects.Length];

            for (var markerRectangleIndex = 0; markerRectangleIndex < suspectedMarkerMetaRects.Length; markerRectangleIndex++)
            {
                if (suspectedMarkerMetaRects[markerRectangleIndex].IsHorizontalLongMarker || suspectedMarkerMetaRects[markerRectangleIndex].IsVerticalLongMarker)
                {
                    markerRectangles[markerRectangleIndex] = suspectedMarkerMetaRects[markerRectangleIndex].Rect;
                }
            }

            var imageRotation = (angleIndex + (int)tagDetectionInfo.Orientation) % 4;
            var markersRotation = imageRotation - angleIndex;

            return new TagDetectionResult
            {
                IsTagPresent = true,
                RotationAngle = resultAngle,
                ImageRotation = imageRotation,
                MarkersRotation = markersRotation,
                Markers = markerRectangles,
                IsFlippedHorizontally = isImageFlipped,
                Confidence = tagDetectionInfo.Confidence
            };
        }

        /// <summary>
        /// Rotates tag image to upright position based on the markers orientation
        /// and extracts image portion between tags to discard any border on the original image.
        /// </summary>
        /// <typeparam name="TDepth">Image depth.</typeparam>
        /// <param name="input">Original non-rotated tag image.</param>
        /// <param name="tagDetectionResult">Result of the previous tag detection produced by <see cref="IsTag"/> method.</param>
        /// <param name="removePadding">Indicates that resulting image should have padding being removed.</param>
        /// <param name="removeMarkers">Indicates that resulting image should have markers.</param>
        /// <returns></returns>
        public Image<Gray, TDepth> NormalizeTagImage<TDepth>(
            Image<Gray, TDepth> input,
            TagDetectionResult tagDetectionResult,
            bool removePadding,
            bool removeMarkers)
            where TDepth : new()
        {
            // Rotate/flip cropped image so that it always upright and correctly left-to-right oriented.
            var normalizedTagImage = input.Rotate(tagDetectionResult.ImageRotation * 90, new Gray());
            if (tagDetectionResult.IsFlippedHorizontally)
            {
                var temp = normalizedTagImage.Flip(FlipType.Horizontal);
                normalizedTagImage.Dispose();
                normalizedTagImage = temp;
            }

            var minPadding = 0;
            var narrowMarkerSize = 0;
            var widestMarkerSize = 0;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < tagDetectionResult.Markers.Length; i++)
            {
                var marker = tagDetectionResult.Markers[i];
                if (marker.IsEmpty) continue;

                if (marker.MinX < _pixelsToMarkerMinEdge)
                {
                    minPadding = Math.Max((int)marker.MinX, minPadding);
                }

                if (marker.MinY < _pixelsToMarkerMinEdge)
                {
                    minPadding = Math.Max((int)marker.MinY, minPadding);
                }

                if (marker.MaxX > _pixelsToMarkerMaxEdge)
                {
                    minPadding = Math.Max(_imageSize.Width - (int)marker.MaxX, minPadding);
                }

                if (marker.MaxY > _pixelsToMarkerMaxEdge)
                {
                    minPadding = Math.Max(_imageSize.Height - (int)marker.MaxY, minPadding);
                }

                narrowMarkerSize = Math.Max((int)Math.Min(marker.Width, marker.Height), narrowMarkerSize);
                widestMarkerSize = Math.Max((int)Math.Max(marker.Width, marker.Height), widestMarkerSize);
            }

            var clearColor = new Gray(255);
            var padding = minPadding;
            narrowMarkerSize += 5;
            widestMarkerSize += 5;

            if (removeMarkers)
            {
                if (!removePadding)
                {
                    normalizedTagImage.Draw(new Rectangle(0, 0, _imageSize.Width, padding), clearColor, -1);
                    normalizedTagImage.Draw(new Rectangle(0, _imageSize.Height - padding, _imageSize.Width, padding), clearColor, -1);
                    normalizedTagImage.Draw(new Rectangle(0, padding, padding, _imageSize.Height - (padding * 2)), clearColor, -1);
                    normalizedTagImage.Draw(new Rectangle(_imageSize.Width - padding, padding, padding, _imageSize.Height - (padding * 2)), clearColor, -1);
                }

                normalizedTagImage.Draw(new Rectangle(padding, padding, narrowMarkerSize, widestMarkerSize), clearColor, -1);
                normalizedTagImage.Draw(new Rectangle(_imageSize.Width - padding - narrowMarkerSize, padding, narrowMarkerSize, widestMarkerSize), clearColor, -1);
                normalizedTagImage.Draw(new Rectangle(padding, _imageSize.Height - padding - narrowMarkerSize, widestMarkerSize, narrowMarkerSize), clearColor, -1);
                normalizedTagImage.Draw(new Rectangle(_imageSize.Width - padding - widestMarkerSize, _imageSize.Height - padding - narrowMarkerSize, widestMarkerSize, narrowMarkerSize), clearColor, -1);
            }

            if (removePadding)
            {
                var temp = normalizedTagImage.Copy(new Rectangle(padding, padding, _imageSize.Width - padding - padding, _imageSize.Height - padding - padding));
                normalizedTagImage.Dispose();
                normalizedTagImage = temp;
            }

            return normalizedTagImage;
        }

        /// <summary>
        /// Rotates tag markers into upright position.
        /// </summary>
        /// <param name="tagDetectionResult">Result of the previous tag detection produced by <see cref="IsTag"/> method.</param>
        /// <returns>Collection of markers.</returns>
        public Quadrilateral[] NormalizeTagMarkers(TagDetectionResult tagDetectionResult)
        {
            var markers = new Quadrilateral[tagDetectionResult.Markers.Length];

            var matrix = new Matrix();
            matrix.RotateAt(tagDetectionResult.MarkersRotation * 90, new PointF(_imageSize.Width / 2f, _imageSize.Height / 2f));

            for (var i = 0; i < tagDetectionResult.Markers.Length; i++)
            {
                if (tagDetectionResult.Markers[i].IsEmpty)
                {
                    markers[i] = tagDetectionResult.Markers[i];
                    continue;
                }

                Quadrilateral marker;

                if (tagDetectionResult.MarkersRotation == 0)
                {
                    marker = tagDetectionResult.Markers[i];
                }
                else
                {
                    marker = new Quadrilateral(tagDetectionResult.Markers[i]);
                    matrix.TransformPoints(marker.Points);
                }

                if (tagDetectionResult.IsFlippedHorizontally)
                {
                    marker = new Quadrilateral(
                        new PointF(_imageSize.Width - marker.P0.X, marker.P0.Y),
                        new PointF(_imageSize.Width - marker.P1.X, marker.P1.Y),
                        new PointF(_imageSize.Width - marker.P2.X, marker.P2.Y),
                        new PointF(_imageSize.Width - marker.P3.X, marker.P3.Y));
                }

                markers[i] = marker;
            }

            return markers;
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rotationMatrix090.Dispose();
                _rotationMatrix180.Dispose();
                _rotationMatrix270.Dispose();
            }
        }

        /// <summary>
        /// Detects if tag is present in given image.
        /// </summary>
        /// <param name="metaRects">Rectangles detected in image.</param>
        /// <returns>Orientation of markers when tag detected.</returns>
        private InternalTagDetectionResult IsTag(MetaRect[] metaRects)
        {
            if (!DetectMarkersPresent(metaRects))
            {
                return new InternalTagDetectionResult { Orientation = MarkersOrientation.None };
            }

            for (var ri1 = 0; ri1 < metaRects.Length; ri1++)
            {
                var r1 = metaRects[ri1];

                if (r1.IsHorizontalLongMarker)
                {
                    if (r1.Rect.MinX < _imageSize.Width * 0.3)
                    {
                        if (r1.Rect.MinY < _imageSize.Height * 0.25)
                        {
                            for (var ri2 = 0; ri2 < metaRects.Length; ri2++)
                            {
                                if (ri2 == ri1)
                                {
                                    continue;
                                }

                                var r2 = metaRects[ri2];

                                if (r2.IsHorizontalLongMarker)
                                {
                                    if (Math.Abs(r2.Rect.MinY - r1.Rect.MinY) < _pixelToleranceHeight &&
                                        Math.Abs(r2.Rect.MaxY - r1.Rect.MaxY) < _pixelToleranceHeight)
                                    {
                                        if (r2.Rect.MinX > _imageSize.Width * 0.6 && r2.Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return ExtraMarkerCheck(MarkersOrientation.HH, metaRects, ri1, ri2);
                                        }
                                    }
                                }

                                if (r2.IsVerticalLongMarker)
                                {
                                    if (Math.Abs(r2.Rect.MinY - r1.Rect.MinY) < _pixelToleranceHeight)
                                    {
                                        if (r2.Rect.MinX > _imageSize.Width * 0.7 && r2.Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return ExtraMarkerCheck(MarkersOrientation.HV, metaRects, ri1, ri2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (r1.IsVerticalLongMarker)
                {
                    if (r1.Rect.MinX < _imageSize.Width * 0.3)
                    {
                        if (r1.Rect.MinY < _imageSize.Height * 0.25)
                        {
                            for (var ri2 = 0; ri2 < metaRects.Length; ri2++)
                            {
                                if (ri2 == ri1)
                                {
                                    continue;
                                }

                                var r2 = metaRects[ri2];

                                if (r2.IsHorizontalLongMarker)
                                {
                                    if (Math.Abs(r2.Rect.MinY - r1.Rect.MinY) < _pixelToleranceHeight)
                                    {
                                        if (r2.Rect.MinX > _imageSize.Width * 0.6 && r2.Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return ExtraMarkerCheck(MarkersOrientation.VH, metaRects, ri1, ri2);
                                        }
                                    }
                                }

                                if (r2.IsVerticalLongMarker)
                                {
                                    if (Math.Abs(r2.Rect.MinY - r1.Rect.MinY) < _pixelToleranceHeight &&
                                        Math.Abs(r2.Rect.MaxY - r1.Rect.MaxY) < _pixelToleranceHeight)
                                    {
                                        if (r2.Rect.MinX > _imageSize.Width * 0.7 && r2.Rect.MaxX > _imageSize.Width * 0.7)
                                        {
                                            return ExtraMarkerCheck(MarkersOrientation.VV, metaRects, ri1, ri2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return new InternalTagDetectionResult { Orientation = MarkersOrientation.None };
        }

        private InternalTagDetectionResult ExtraMarkerCheck(MarkersOrientation detectedMarkersOrientation, MetaRect[] metaRects, int ri1, int ri2)
        {
            var confidence = 0.5D;

            var r1 = metaRects[ri1];
            var r2 = metaRects[ri2];

            MetaRect rX = new MetaRect();

            for (var ri3 = 0; ri3 < metaRects.Length; ri3++)
            {
                if (ri3 == ri1 || ri3 == ri2)
                {
                    continue;
                }

                var r3 = metaRects[ri3];

                if ((r3.IsHorizontalLongMarker || r3.IsVerticalLongMarker) && r3.Rect.MaxY > _imageSize.Height * 0.75)
                {
                    if (r3.Rect.MinX < _imageSize.Width * 0.3 &&
                        Math.Abs(r3.Rect.MinX - r1.Rect.MinX) < _pixelToleranceWidth)
                    {
                        if ((detectedMarkersOrientation == MarkersOrientation.VV && r3.IsHorizontalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.HH && r3.IsVerticalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.VH && r3.IsVerticalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.HV && r3.IsHorizontalLongMarker))
                        {
                            confidence += 0.15D;

                            if (!rX.Rect.IsEmpty && rX.Rect.MaxX > _imageSize.Width * 0.7)
                            {
                                if (Math.Abs(r3.Rect.MaxY - rX.Rect.MaxY) < _pixelToleranceHeight)
                                {
                                    confidence += 0.2D;
                                }

                                break;
                            }

                            rX = r3;
                        }
                    }

                    if (r3.Rect.MaxX > _imageSize.Width * 0.7 &&
                        Math.Abs(r3.Rect.MaxX - r2.Rect.MaxX) < _pixelToleranceWidth)
                    {
                        if ((detectedMarkersOrientation == MarkersOrientation.VV && r3.IsHorizontalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.HH && r3.IsVerticalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.VH && r3.IsHorizontalLongMarker) ||
                            (detectedMarkersOrientation == MarkersOrientation.HV && r3.IsVerticalLongMarker))
                        {
                            confidence += 0.15D;

                            if (!rX.Rect.IsEmpty && rX.Rect.MinX < _imageSize.Width * 0.3)
                            {
                                if (Math.Abs(r3.Rect.MaxY - rX.Rect.MaxY) < _pixelToleranceHeight)
                                {
                                    confidence += 0.2D;
                                }

                                break;
                            }

                            rX = r3;
                        }
                    }
                }
            }

            return new InternalTagDetectionResult { Orientation = detectedMarkersOrientation, Confidence = confidence };
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
                    IsVerticalLongMarker = (htow > 2.0f) && (htow < 4.5f)
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
