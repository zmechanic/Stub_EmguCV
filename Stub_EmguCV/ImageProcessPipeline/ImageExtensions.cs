using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

using System.Collections.Generic;
using System.Drawing;

namespace Stub_EmguCV
{
    public static class ImageExtensions
    {
        public static ExtendedImage ToPipelineImage(this Mat input)
        {
            return new ExtendedImage(input);
        }

        /// <summary>
        /// The get axis aligned image part.
        /// </summary>
        /// <typeparam name="TColor">
        /// Image color type.
        /// </typeparam>
        /// <typeparam name="TDepth">
        /// Image depth type.
        /// </typeparam>
        /// <param name="input">
        /// The input image.
        /// </param>
        /// <param name="rectSrc">
        /// The rectangle area to extract.
        /// </param>
        /// <param name="rectDst">
        /// The target image area.
        /// </param>
        /// <param name="targetImageSize">
        /// The target image size.
        /// </param>
        /// <returns>
        /// New image clipped by rectangular area and normalized to take entire space of <paramref name="targetImageSize"/>.
        /// </returns>
        public static Image<TColor, TDepth> GetAxisAlignedImagePart<TColor, TDepth>(
            this Image<TColor, TDepth> input,
            Quadrilateral rectSrc,
            Quadrilateral rectDst,
            Size targetImageSize)
            where TColor : struct, IColor
            where TDepth : new()
        {
            var src = new[] { rectSrc.P0, rectSrc.P1, rectSrc.P2, rectSrc.P3 };
            var dst = new[] { rectDst.P0, rectDst.P1, rectDst.P2, rectDst.P3 };

            using (var matrix = CvInvoke.GetPerspectiveTransform(src, dst))
            {
                using (var cutImagePortion = new Mat())
                {
                    CvInvoke.WarpPerspective(input, cutImagePortion, matrix, targetImageSize, Inter.Cubic);
                    return cutImagePortion.ToImage<TColor, TDepth>();
                }
            }
        }

        /// <summary>
        /// Generates new image with color blob being smoothed and converted to binary image for pixels over particular threshold.
        /// <para>
        /// The algorithm is as following:<br/>
        /// Step 1: Errode => Dilate => Smooth.<br/>
        /// Step 2: Dilate => Errode => Smooth.<br/>
        /// Step 3: Binary Threshold.
        /// </para>
        /// </summary>
        /// <param name="input">
        /// The input image.
        /// </param>
        /// <param name="errodeDilateIterationsPass1">
        /// Number of iteration for errode/dilate for 1st pass.
        /// </param>
        /// <param name="gaussianSmoothKernelSizePass1">
        /// Kernel size for gaussian blur for 1st pass.
        /// </param>
        /// <param name="errodeDilateIterationsPass2">
        /// Number of iteration for errode/dilate for 2nd pass.
        /// </param>
        /// <param name="gaussianSmoothKernelSizePass2">
        /// Kernel size for gaussian blur for 2nd pass.
        /// </param>
        /// <param name="loThreshold">
        /// Lowest threshold of pixel values to be allowed in result image.
        /// </param>
        /// <returns>
        /// New processed image.
        /// </returns>
        public static Image<TColor, TDepth> RemoveNoise<TColor, TDepth>(
            this Image<TColor, TDepth> input,
            int errodeDilateIterationsPass1 = 5,
            int gaussianSmoothKernelSizePass1 = 7,
            int errodeDilateIterationsPass2 = 5,
            int gaussianSmoothKernelSizePass2 = 7,
            int loThreshold = 120)
            where TColor : struct, IColor
            where TDepth : new()
        {
            var result = input.Erode(errodeDilateIterationsPass1);
            result = result.Dilate(errodeDilateIterationsPass1);
            result = result.SmoothGaussian(gaussianSmoothKernelSizePass1);
            result = result.Dilate(errodeDilateIterationsPass2);
            result = result.Erode(errodeDilateIterationsPass2);
            result = result.SmoothGaussian(gaussianSmoothKernelSizePass2);

            return result;
        }

        /// <summary>
        /// Generates new image by applying binary threshold.
        /// </summary>
        /// <param name="input">
        /// The input image.
        /// </param>
        /// <param name="loThreshold">
        /// Lowest threshold of pixel values to be allowed in result image.
        /// </param>
        /// <returns>
        /// New processed image.
        /// </returns>
        public static Image<Gray, TDepth> BinaryMaximize<TDepth>(
            this Image<Gray, TDepth> input,
            int loThreshold = 120)
            where TDepth : new()
        {
            return input.ThresholdBinary(new Gray(loThreshold), new Gray(255));
        }

        /// <summary>
        /// Finds rectangles in image. The image will be cloned internally and content will not change.
        /// </summary>
        /// <param name="input">
        /// The input image.
        /// </param>
        /// <param name="minRectangleArea">
        /// Minimum area of rectangle to be recognized as valid.
        /// </param>
        /// <returns>
        /// List of detected rectangles.
        /// </returns>
        public static Quadrilateral[] FindRectangles<TColor, TDepth>(
            this Image<TColor, TDepth> input,
            double minRectangleArea)
            where TColor : struct, IColor
            where TDepth : new()
        {
            using (var temp = input.Clone())
            {
                return FindRectanglesDestructive(temp, minRectangleArea);
            }
        }

        /// <summary>
        /// Finds rectangles in image. The image content will change.
        /// </summary>
        /// <param name="input">
        /// The input image.
        /// </param>
        /// <param name="minRectangleArea">
        /// Minimum area of rectangle to be recognized as valid.
        /// </param>
        /// <returns>
        /// List of detected rectangles.
        /// </returns>
        public static Quadrilateral[] FindRectanglesDestructive<TColor, TDepth>(
            this Image<TColor, TDepth> input,
            double minRectangleArea)
            where TColor : struct, IColor
            where TDepth : new()
        {
            var result = new List<Quadrilateral>();

            using (var contoursDetected = new VectorOfVectorOfPoint())
            {
                CvInvoke.FindContours(
                    input,
                    contoursDetected,
                    null,
                    RetrType.List,
                    ChainApproxMethod.ChainApproxSimple);

                for (var i = 0; i < contoursDetected.Size; i++)
                {
                    using (var contour = contoursDetected[i])
                    {
                        using (var approxContour = new VectorOfPoint())
                        {
                            CvInvoke.ApproxPolyDP(
                                contour,
                                approxContour,
                                CvInvoke.ArcLength(contour, true) * 0.05,
                                true);

                            var numberOfCorners = approxContour.Size;
                            if (numberOfCorners == 4)
                            {
                                var contourArea = CvInvoke.ContourArea(approxContour);
                                if (contourArea > minRectangleArea)
                                {
                                    result.Add(new Quadrilateral(
                                        new PointF(approxContour[0].X, approxContour[0].Y),
                                        new PointF(approxContour[1].X, approxContour[1].Y),
                                        new PointF(approxContour[2].X, approxContour[2].Y),
                                        new PointF(approxContour[3].X, approxContour[3].Y)));
                                }
                            }
                        }
                    }
                }
            }

            return result.ToArray();
        }
    }
}