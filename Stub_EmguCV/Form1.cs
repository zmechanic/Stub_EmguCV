using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.UI;

using Stub_EmguCV.ImageProcessPipeline;

namespace Stub_EmguCV
{
    public partial class Form1 : Form
    {
        private ImageViewer[] _viewers = new ImageViewer[3];
        private Capture _capture;

        private Image<Gray, byte> _scaledDownFrameOneColorImagePrev;
        private Image<Gray, byte> _storedImage;

        public Form1()
        {
            InitializeComponent();

            for (var i = 0; i < 3; i++)
            {
                _viewers[i] = new ImageViewer();
                _viewers[i].StartPosition = FormStartPosition.Manual;
                _viewers[i].Location = new Point(i * 600, 0);
                _viewers[i].Show();
            }

            //_capture = new Capture(@"C:\Temp\111.bmp");
            _capture = new Capture(0);
            //_capture.SetCaptureProperty(CapProp.FrameWidth, 1920);
            //_capture.SetCaptureProperty(CapProp.FrameHeight, 1080);

            _capture.ImageGrabbed += CaptureOnImageGrabbed;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_capture == null)
            {
                return;
            }

            _capture.Grab();
        }

        private void CaptureOnImageGrabbed(object sender, EventArgs eventArgs)
        {
            using (var frame = new Mat())
            {
                ((Capture) sender).Retrieve(frame);

                //DetectCircles(frame);

                CalculateColorInFrameCenter(frame);

                //FindTags(frame);

                //Process(frame);
            }
        }

        private void FindTags(Mat frame)
        {
            Debug.WriteLine("");

            using (var frameImage = frame.ToPipelineImage())
            {
                var imageRGB = frameImage.RgbImage.Clone();

                var cropImageSize = new Size(128, 128);
                var detectSubrectArea = 25 * ((cropImageSize.Width * cropImageSize.Height) / (64 * 64));
                var tagDetector = new TagDetector(cropImageSize);
                var frameOneColorImageFiltered = frameImage.IsolateColorBlack(100);
                var rects = frameOneColorImageFiltered.Clone().FindRectangles(1000);

                foreach (var rect in rects.OrderByDescending(_ => _.BoundingBoxArea))
                {
                    var rectBoundingBox = rect.BoundingBox;

                    // Ignore largest rectangles detected at the edge of image due to contrasting area.
                    if (rectBoundingBox.Left < 5 || rectBoundingBox.Top < 5 ||
                        rectBoundingBox.Right > frameOneColorImageFiltered.Width - 5 || rectBoundingBox.Bottom > frameOneColorImageFiltered.Height - 5)
                    {
                        continue;
                    }

                    // Attempt to detect if there is large inner rectangle. It may only indicate inner boundary of tag,
                    // otherwise it's presence will render tag unusable.
                    var hasLargeInnerRect = false;

                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (var otherRect in rects)
                    {
                        if (otherRect.Equals(rect))
                        {
                            continue;
                        }

                        var otherRectBoundingBox = otherRect.BoundingBox;
                        if (rectBoundingBox.Contains(otherRectBoundingBox))
                        {
                            if ((otherRectBoundingBox.Width * otherRectBoundingBox.Height) / (rectBoundingBox.Width * rectBoundingBox.Height) > 0.7)
                            {
                                hasLargeInnerRect = true;
                                break;
                            }
                        }
                    }

                    // Skip current rectangle if we have detected a large ineer rectangle.
                    if (hasLargeInnerRect)
                    {
                        continue;
                    }

                    imageRGB.Draw(rect.BoundingBoxInt, new Rgb(255, 0, 0));

                    // Extract image part defined by rectangle and normalize it so it's axis aligned.
                    using (var rectImage = frameOneColorImageFiltered
                        .GetAxisAlignedImagePart(
                            rect,
                            new Quadrilateral(new PointF(0, 0), new PointF(0, cropImageSize.Height), new PointF(cropImageSize.Width, cropImageSize.Height), new PointF(cropImageSize.Width, 0)),
                            cropImageSize))
                    {
                        _viewers[1].Image = rectImage;

                        // Find rectangles within cropped image.
                        var detectedSubrects = rectImage.FindRectangles(detectSubrectArea);

                        // We must have at least two rectangles to proceed with tag detection.
                        if (detectedSubrects.Length >= 2)
                        {
                            for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
                            {
                                imageRGB.Draw(cornerIndex.ToString(), new Point((int)rect.Points[cornerIndex].X, (int)rect.Points[cornerIndex].Y), FontFace.HersheyPlain, 1.5D, new Rgb(Color.SpringGreen), 2);
                                imageRGB.Draw(new CircleF(new PointF(rect.Points[cornerIndex].X, rect.Points[cornerIndex].Y), 5), new Rgb(Color.Magenta), 2);
                            }

                            // Check if cropped image contains a tag markers.
                            var tagDetectionResult = tagDetector.IsTag(detectedSubrects, rect);
                            if (tagDetectionResult.IsTagPresent)
                            {
                                Debug.WriteLine("{0} ||| {1} ||| X={2},Y={3}", tagDetectionResult.RotationAngle, tagDetectionResult.Confidence, rect.MinX, rect.MinY);

                                using (var normalizedTagImage = tagDetector.NormalizeTagImage(rectImage, tagDetectionResult, true, true))
                                {
                                    if (_storedImage == null)
                                    {
                                        _storedImage = normalizedTagImage.Copy(new Rectangle(5, 5, normalizedTagImage.Width - 10, normalizedTagImage.Height - 10));
                                    }

                                    using (var matchResult = normalizedTagImage.MatchTemplate(_storedImage, TemplateMatchingType.CcoeffNormed))
                                    {
                                        double[] minValues, maxValues;
                                        Point[] minLocations, maxLocations;
                                        matchResult.MinMax(out minValues, out maxValues, out minLocations, out maxLocations);

                                        if (maxValues[0] > 0.8)
                                        {
                                            Debug.WriteLine("!!!!!!!!!!!!!!! {0}    {1}", maxValues[0], maxLocations[0]);
                                        }
                                    }

                                    //Debug.WriteLine(normalizedTagImage.GetSubRect(new Rectangle(0, 0, cropImageSize.Width, 5)).CountNonzero()[0]);

                                    //normalizedTagImage.Draw(new Rectangle(0, 0, (int)(cropImageSize.Width * 0.2), (int)(cropImageSize.Height * 0.2)), new Gray(255), -1);

                                    _viewers[2].Image = normalizedTagImage;
                                }
                            }
                        }
                    }
                }

                _viewers[0].Image = imageRGB;

                imageRGB.Dispose();
            }
        }

        private void CalculateColorInFrameCenter(Mat frame)
        {
            using (var frameImage = frame.ToPipelineImage())
            {
                Rgb averageColor;

                using (var roiImage =
                    frameImage.RgbImage.Copy(
                        new Rectangle((frameImage.RgbImage.Width / 2) - 20, (frameImage.RgbImage.Height / 2) - 20, 40, 40)))
                {
                    averageColor = roiImage.GetAverage();
                }

                var unitR = averageColor.Red / 255;
                var unitG = averageColor.Green / 255;
                var unitB = averageColor.Blue / 255;
                var minChannelValue = Math.Min(unitR, Math.Min(unitG, unitB));
                var maxChannelValue = Math.Max(unitR, Math.Max(unitG, unitB));
                var delta = maxChannelValue - minChannelValue;
                var normalizedR = unitR / maxChannelValue;
                var normalizedG = unitG / maxChannelValue;
                var normalizedB = unitB / maxChannelValue;
                var brightness =
                    Math.Sqrt(0.299 * (unitR * unitR) +
                              0.587 * (unitG * unitG) +
                              0.114 * (unitB * unitB));

                // Monochromatic color
                if (1D - normalizedR < 0.15 &&
                    1D - normalizedG < 0.15 &&
                    1D - normalizedB < 0.15)
                {
                    if (brightness < 0.45)
                    {
                        Console.WriteLine("black");
                    }
                    else if (brightness > 0.65)
                    {
                        Console.WriteLine("white");
                    }
                    else
                    {
                        Console.WriteLine("gray");
                    }
                }
                else
                {
                    double hue;

                    if (Math.Abs(unitR - maxChannelValue) < 0.01)
                    {
                        // between yellow & magenta
                        hue = (unitG - unitB) / delta;
                    }
                    else if (Math.Abs(unitG - maxChannelValue) < 0.01)
                    {
                        // between cyan & yellow
                        hue = 2 + (unitB - unitR) / delta;
                    }
                    else
                    {
                        // between magenta & cyan
                        hue = 4 + (unitR - unitG) / delta;
                    }

                    // convert to degrees
                    hue *= 60;
                    if (hue < 0)
                    {
                        hue += 360;
                    }

                    if (hue >= 330)
                    {
                        Console.WriteLine("red");
                    }
                    else if (hue >= 285)
                    {
                        Console.WriteLine("magenta");
                    }
                    else if (hue >= 260)
                    {
                        Console.WriteLine("violet");
                    }
                    else if (hue >= 190)
                    {
                        Console.WriteLine("blue");
                    }
                    else if (hue >= 170)
                    {
                        Console.WriteLine("cyan");
                    }
                    else if (hue >= 90)
                    {
                        Console.WriteLine("green");
                    }
                    else if (hue >= 45)
                    {
                        Console.WriteLine("yellow");
                    }
                    else if (hue >= 30)
                    {
                        Console.WriteLine("orange");
                    }
                    else
                    {
                        Console.WriteLine("red");
                    }

                    Console.WriteLine("hue {0}", hue);
                }

                frameImage.RgbImage.Draw(
                    new Rectangle((frameImage.RgbImage.Width / 2) - 20, (frameImage.RgbImage.Height / 2) - 20, 40, 40),
                    new Rgb(255, 255, 255));

                //Console.WriteLine("{0} {1} {2}", (int)averageColor.Red, (int)averageColor.Green, (int)averageColor.Blue);
                //Console.WriteLine("{0} {1} {2} {3}", brightness, normalizedR, normalizedG, normalizedB);

                _viewers[0].Image = frameImage.RgbImage;
            }
        }

        private void DetectCircles(Mat frame)
        {
            using (var frameImage = frame.ToPipelineImage())
            {
                var colorSeparated = frameImage.HsvImage.InRange(new Hsv(240 / 2f, 0, 80), new Hsv(320 / 2f, 120, 255));

                var img = colorSeparated.Clone();

                img = img.Dilate(3);
                img = img.Erode(3);
                img = img.SmoothGaussian(5);
                img = img.Erode(3);
                img = img.Dilate(3);

                img = img.InRange(new Gray(120), new Gray(255));

                var circleses = img.HoughCircles(new Gray(150), new Gray(50), 2.2, 50, 15, 120);

                var testImage = frameImage.BgrImage.Clone();

                foreach (var circles in circleses)
                {
                    foreach (var circle in circles)
                    {
                        testImage.Draw(circle, new Bgr(255, 255, 0), 3);
                    }
                }

                _viewers[0].Image = testImage;
                _viewers[1].Image = img;

                testImage.Dispose();
                colorSeparated.Dispose();
                img.Dispose();
            }
        }

        private void Process(Mat frame)
        {
            var imageHSV = frame.ToImage<Hsv, byte>();
            var imageRGB = frame.ToImage<Rgb, byte>();

            var channels = imageRGB.Split();
            var blue = channels[0];
            var green = channels[1];
            var red = channels[2];

            //var colorSeparated = red - green;  //red
            //var colorSeparated = green - red;  //blue
            //var colorSeparated = blue - green;  //purple
            //var colorSeparated = blue - red;  //blue
            //var colorSeparated = red - blue;  //yellow (!blue)
            //var colorSeparated = green - blue;  //green/yellow (!blue/!red)

            //var colorSeparated = blue - ((red - blue) + (green - blue));

            ////blue colour by exclusion
            var blue1 = (green - blue) + (red - blue);
            blue1 = blue1.Not();
            blue1 = blue1.ThresholdToZero(new Gray(250));
            var blue2 = (green - red) + (blue - red);
            blue2 = blue2.InRange(new Gray(80), new Gray(255));
            var colorSeparated = blue1 & blue2;

            //var green1 = green.InRange(new Gray(100), new Gray(255)) & blue.InRange(new Gray(0), new Gray(100)) & red.InRange(new Gray(0), new Gray(100));
            //var colorSeparated = green1;

            //var red1 = red - blue;
            //var colorSeparated = red1;

            var img = colorSeparated;

            img = img.Erode(3);
            img = img.Dilate(3);
            img = img.SmoothGaussian(7);
            img = img.Dilate(3);
            img = img.Erode(3);

            img = img.InRange(new Gray(50), new Gray(255));

            var result = img.HoughCircles(new Gray(150), new Gray(50), 2.2, 50, 15, 120);
            foreach (var circles in result)
            {
                foreach (var circle in circles)
                {
                    img.Draw(circle, new Gray(255), 3);
                }
            }

            //_viewers[1].Image = blue2;
            //_viewers[2].Image = img;

            var yellow1 = (green - blue) + (red - blue);
            yellow1 = yellow1.Erode(5);
            yellow1 = yellow1.Dilate(5);
            yellow1 = yellow1.SmoothGaussian(7);
            yellow1 = yellow1.Dilate(5);
            yellow1 = yellow1.Erode(5);
            yellow1 = yellow1.SmoothGaussian(7);
            yellow1 = yellow1.ThresholdBinary(new Gray(120), new Gray(255));

            //var lineSegments = yellow1.HoughLines(50, 250, 2.5, Math.PI / 45.0, 20, 30, 40);
            //var segmentCount = 0;
            //foreach (var lineSegment in lineSegments)
            //{
            //    Rgb color;

            //    switch (segmentCount)
            //    {
            //        case 0:
            //            color = new Rgb(255, 0, 0);
            //            break;
            //        case 1:
            //            color = new Rgb(0, 255, 0);
            //            break;
            //        case 2:
            //            color = new Rgb(255, 255, 0);
            //            break;
            //        case 3:
            //            color = new Rgb(255, 0, 255);
            //            break;
            //        default:
            //            color = new Rgb(255, 255, 255);
            //            break;
            //    }

            //    foreach (var line in lineSegment)
            //    {
            //        imageRGB.Draw(line, color, 3, LineType.FourConnected);
            //    }

            //    segmentCount++;
            //}





            //using (var contoursDetected = new VectorOfVectorOfPoint())
            //{
            //    CvInvoke.FindContours(
            //        yellow1,
            //        contoursDetected,
            //        null,
            //        RetrType.List,
            //        ChainApproxMethod.ChainApproxNone);

            //    var rects = new List<PointF[]>();

            //    for (var i = 0; i < contoursDetected.Size; i++)
            //    {
            //        using (var contour = contoursDetected[i])
            //        {
            //            using (var approxContour = new VectorOfPoint())
            //            {
            //                CvInvoke.ApproxPolyDP(
            //                    contour,
            //                    approxContour,
            //                    CvInvoke.ArcLength(contour, true) * 0.05,
            //                    true);

            //                var numberOfCorners = approxContour.Size;
            //                if (numberOfCorners == 4)
            //                {
            //                    var contourArea = CvInvoke.ContourArea(approxContour);
            //                    if (contourArea > 500)
            //                    {
            //                        var rect = new PointF[4];

            //                        for (var cornerIndex = 0; cornerIndex < numberOfCorners; cornerIndex++)
            //                        {
            //                            rect[cornerIndex] = new PointF(approxContour[cornerIndex].X, approxContour[cornerIndex].Y);
            //                        }

            //                        rects.Add(rect);
            //                    }
            //                }

            //                foreach (var rect in rects)
            //                {
            //                    for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            //                    {
            //                        imageRGB.Draw(cornerIndex.ToString(), new Point((int)rect[cornerIndex].X, (int)rect[cornerIndex].Y), FontFace.HersheyPlain, 1.5D, new Rgb(Color.SpringGreen), 2);
            //                        imageRGB.Draw(new CircleF(new PointF(rect[cornerIndex].X, rect[cornerIndex].Y), 5), new Rgb(Color.Magenta), 2);
            //                    }

            //                    var src = new[] { rect[0], rect[1], rect[2], rect[3] };
            //                    var dst = new[] { new PointF(0, 0), new PointF(0, 400), new PointF(400, 400), new PointF(400, 0) };

            //                    var tmp1 = new UMat();
            //                    var matrix = CvInvoke.GetPerspectiveTransform(src, dst);
            //                    CvInvoke.WarpPerspective(imageRGB, tmp1, matrix, new Size(400, 400));
            //                    _viewers[2].Image = tmp1;
            //                }
            //            }
            //        }


            //        //var contoursArray = new List<Point[]>();

            //        //using (var currContour = contoursDetected[i])
            //        //{
            //        //    var pointsArray = new Point[currContour.Size];

            //        //    for (var j = 0; j < currContour.Size; j++)
            //        //    {
            //        //        pointsArray[j] = currContour[j];
            //        //    }

            //        //    contoursArray.Add(pointsArray);
            //        //}
            //    }


            //    //foreach (var contourPoints in contoursArray)
            //    //{
            //    //    yellow1.Draw(contourPoints, new Gray(128), 3, LineType.FourConnected);
            //    //}
            //}

            var frameImage = frame.ToPipelineImage();
            var frameOneColorImage = frameImage.IsolateColorBlackByAverage(80);

            var scaleDownFactor = 5F;
            var scaledDownFrameOneColorImage = frameOneColorImage.Resize(1D / scaleDownFactor, Inter.Cubic);

            if (_scaledDownFrameOneColorImagePrev != null)
            {
                var horizontalWindowCount = 5;
                var verticalWindowCount = 5;
                var averageRegionFlow = new PointF[horizontalWindowCount, verticalWindowCount];

                var flowResult = new Mat();
                CvInvoke.CalcOpticalFlowFarneback(_scaledDownFrameOneColorImagePrev, scaledDownFrameOneColorImage, flowResult, 0.5, 3, 15, 5, 1, 1.2, OpticalflowFarnebackFlag.Default);

                var flowResultChannels = flowResult.Split();
                var flowResultX = flowResultChannels[0];
                var flowResultY = flowResultChannels[1];

                var flowWindowHeight = (float)Math.Ceiling(flowResult.Rows / (float)verticalWindowCount);
                var flowWindowWidth = (float)Math.Ceiling(flowResult.Cols / (float)horizontalWindowCount);

                var flowWindowVerticalIndexCounter = 0;
                var flowWindowRowCounter = 0;

                for (var r = 0; r < flowResult.Rows; r++)
                {
                    var flowWindowHorizontalIndexCounter = 0;
                    var flowWindowColCounter = 0;
                    var horizontalLineFlowTotal = new PointF(0, 0);

                    for (var c = 0; c < flowResult.Cols; c++)
                    {
                        var xyValues = new float[2];
                        Marshal.Copy(flowResultX.DataPointer + (((r * flowResultX.Cols) + c) * flowResultX.ElementSize), xyValues, 0, 1);
                        Marshal.Copy(flowResultY.DataPointer + (((r * flowResultY.Cols) + c) * flowResultY.ElementSize), xyValues, 1, 1);
                        var xShift = xyValues[0];
                        var yShift = xyValues[1];

                        if (flowWindowColCounter >= flowWindowWidth || c == flowResult.Cols - 1)
                        {
                            averageRegionFlow[flowWindowHorizontalIndexCounter, flowWindowVerticalIndexCounter].X += horizontalLineFlowTotal.X;
                            averageRegionFlow[flowWindowHorizontalIndexCounter, flowWindowVerticalIndexCounter].Y += horizontalLineFlowTotal.Y;

                            horizontalLineFlowTotal = new PointF(0, 0);
                            flowWindowHorizontalIndexCounter++;
                            flowWindowColCounter = 0;
                        }

                        horizontalLineFlowTotal.X += xShift;
                        horizontalLineFlowTotal.Y += yShift;

                        flowWindowColCounter++;
                    }

                    if (flowWindowRowCounter >= flowWindowHeight || r == flowResult.Rows - 1)
                    {
                        flowWindowVerticalIndexCounter++;
                        flowWindowRowCounter = 0;
                    }

                    flowWindowRowCounter++;
                }

                for (var j = 0; j < verticalWindowCount; j++)
                {
                    for (var i = 0; i < horizontalWindowCount; i++)
                    {
                        averageRegionFlow[i, j].X /= flowWindowHeight * flowWindowWidth;
                        averageRegionFlow[i, j].Y /= flowWindowHeight * flowWindowWidth;

                        var sx = (int)(i * flowWindowWidth + flowWindowWidth / 2);
                        var sy = (int)(j * flowWindowHeight + flowWindowHeight / 2);

                        imageRGB.Draw(new LineSegment2D(new Point(sx, sy), new Point(sx + (int)averageRegionFlow[i, j].X, sy + (int)averageRegionFlow[i, j].Y)), new Rgb(Color.LawnGreen), 2);
                    }
                }
            }

            _scaledDownFrameOneColorImagePrev = scaledDownFrameOneColorImage;



            //var featuresDetector = new Emgu.CV.Features2D.GFTTDetector();
            //var kp = featuresDetector.Detect(frameOneColorImage);
            //foreach (var keyPoint in kp)
            //{
            //    frameOneColorImage.Draw(new Ellipse(keyPoint.Point, new SizeF(2, 2), 0), new Gray(255), 3);
            //}

            //var cornerImage = new Image<Gray, float>(yellow1.Size);
            //yellow1.CornerHarris(yellow1, cornerImage, 11, 11, 0.05, BorderType.Reflect);
            //cornerImage = cornerImage.ThresholdBinary(new Gray(0.005), new Gray(255));



            //_viewers[1].Image = frameOneColorImageFiltered;

            //for (var i = 0; i < 3; i++)
            //{
            //    var img = channels[i] & mask;

            //    img = img.Erode(3);
            //    img = img.Dilate(3);
            //    img = img.Dilate(3);
            //    img = img.Erode(3);
            //    img = img.SmoothGaussian(7);

            //    var result = img.HoughCircles(new Gray(150), new Gray(50), 2.2, 50, 15, 100);
            //    foreach (var circles in result)
            //    {
            //        foreach (var circle in circles)
            //        {
            //            img.Draw(circle, new Gray(255), 3);
            //        }
            //    }

            //    _viewers[i].Image = img;
            //}
        }
    }
}
