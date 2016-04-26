using Emgu.CV;
using Emgu.CV.Structure;
using System;

namespace Stub_EmguCV
{
    public class ExtendedImage : IDisposable
    {
        private readonly Mat _mat;

        private Image<Rgb, byte> _rgbImage;
        private Image<Bgr, byte> _bgrImage;

        private Image<Gray, byte> _grayscaleImage;
        
        private bool _isChannelSplit;
        
        private Image<Gray, byte> _channelR;
        private Image<Gray, byte> _channelG;
        private Image<Gray, byte> _channelB;

        private Image<Gray, byte> _channelRMinusB;
        private Image<Gray, byte> _channelRMinusG;
        private Image<Gray, byte> _channelGMinusB;
        private Image<Gray, byte> _channelGMinusR;
        private Image<Gray, byte> _channelBMinusR;
        private Image<Gray, byte> _channelBMinusG;

        public ExtendedImage(Mat mat)
        {
            _mat = mat;
        }

        public Image<Gray, byte> GrayscaleImage
        {
            get
            {
                if (_grayscaleImage == null)
                {
                    _grayscaleImage = BgrImage.Convert<Gray, byte>();
                }

                return _grayscaleImage;
            }
        }

        public Image<Bgr, byte> BgrImage
        {
            get
            {
                if (_bgrImage == null)
                {
                    _bgrImage = _mat.ToImage<Bgr, byte>();
                }

                return _bgrImage;
            }
        }

        public Image<Rgb, byte> RgbImage
        {
            get
            {
                if (_rgbImage == null)
                {
                    _rgbImage = _mat.ToImage<Rgb, byte>();
                }

                return _rgbImage;
            }
        }

        public Image<Gray, byte> ChannelR
        {
            get
            {
                if (!_isChannelSplit)
                {
                    SplitChannels();
                }

                return _channelR;
            }
        }

        public Image<Gray, byte> ChannelG
        {
            get
            {
                if (!_isChannelSplit)
                {
                    SplitChannels();
                }

                return _channelG;
            }
        }

        public Image<Gray, byte> ChannelB
        {
            get
            {
                if (!_isChannelSplit)
                {
                    SplitChannels();
                }

                return _channelB;
            }
        }

        public Image<Gray, byte> ChannelRMinusB
        {
            get
            {
                return _channelRMinusB ?? (_channelRMinusB = ChannelR - ChannelB);
            }
        }

        public Image<Gray, byte> ChannelRMinusG
        {
            get
            {
                return _channelRMinusG ?? (_channelRMinusG = ChannelR - ChannelG);
            }
        }

        public Image<Gray, byte> ChannelGMinusB
        {
            get
            {
                return _channelGMinusB ?? (_channelGMinusB = ChannelG - ChannelB);
            }
        }

        public Image<Gray, byte> ChannelGMinusR
        {
            get
            {
                return _channelGMinusR ?? (_channelGMinusR = ChannelG - ChannelR);
            }
        }

        public Image<Gray, byte> ChannelBMinusR
        {
            get
            {
                return _channelBMinusR ?? (_channelBMinusR = ChannelB - ChannelR);
            }
        }

        public Image<Gray, byte> ChannelBMinusG
        {
            get
            {
                return _channelBMinusG ?? (_channelBMinusG = ChannelB - ChannelG);
            }
        }

        public Image<Gray, byte> IsolateColorBlack(double threshold = 80)
        {
            return GrayscaleImage.ThresholdBinary(new Gray(threshold), new Gray(255));
        }

        public Image<Gray, byte> IsolateColorBlackByAverage(double threshold = 80)
        {
            var mask = ChannelR.ThresholdBinaryInv(new Gray(threshold), new Gray(255)).And(ChannelG.ThresholdBinaryInv(new Gray(threshold), new Gray(255))).And(ChannelB.ThresholdBinaryInv(new Gray(threshold), new Gray(255)));
            return mask;
        }

        /// <summary>
        /// Generates new image that contains wider range of green colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorGreenByOtherColorsExclusion1()
        {
            return (ChannelGMinusB + ChannelGMinusR) - IsolateColorYellowByOtherColorsExclusion1();
        }

        /// <summary>
        /// Generates new image that contains narrower range of green colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorGreenByOtherColorsExclusion2()
        {
            return (ChannelGMinusB + ChannelGMinusR) - IsolateColorYellowByOtherColorsExclusion2();
        }

        /// <summary>
        /// Generates new image that contains wider range of red colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorRedByOtherColorsExclusion1()
        {
            return (ChannelR - ChannelB.ThresholdBinary(new Gray(50), new Gray(255)) - ChannelG.ThresholdBinary(new Gray(50), new Gray(255))) - IsolateColorYellowByOtherColorsExclusion1();
        }

        /// <summary>
        /// Generates new image that contains narrower range of red colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorRedByOtherColorsExclusion2()
        {
            return (ChannelRMinusB.ThresholdToZero(new Gray(120)) + ChannelRMinusG) - IsolateColorYellowByOtherColorsExclusion2();
        }

        /// <summary>
        /// Generates new image that contains wider range of yellow colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorYellowByOtherColorsExclusion1()
        {
            return ChannelGMinusB + ChannelRMinusB;
        }

        /// <summary>
        /// Generates new image that contains narrower range of yellow colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorYellowByOtherColorsExclusion2()
        {
            return (ChannelGMinusB + ChannelRMinusB) - (ChannelRMinusG + ChannelGMinusR);
        }

        /// <summary>
        /// Generates new image that contains only blue colors.
        /// </summary>
        /// <returns>New processed image.</returns>
        public Image<Gray, byte> IsolateColorBlueByOtherColorsExclusion()
        {
            var blue1 = (ChannelGMinusB + ChannelRMinusB).Not().ThresholdToZero(new Gray(200));
            var blue2 = (ChannelGMinusR + ChannelBMinusR).ThresholdToZero(new Gray(20));
            return blue1 & blue2;
        }

        private void SplitChannels()
        {
            var channels = BgrImage.Split();
            _channelB = channels[0];
            _channelG = channels[1];
            _channelR = channels[2];

            _isChannelSplit = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bgrImage?.Dispose();
                _rgbImage?.Dispose();
                _grayscaleImage?.Dispose();

                _channelR?.Dispose();
                _channelG?.Dispose();
                _channelB?.Dispose();

                _channelRMinusB?.Dispose();
                _channelRMinusG?.Dispose();
                _channelGMinusB?.Dispose();
                _channelGMinusR?.Dispose();
                _channelBMinusR?.Dispose();
                _channelBMinusG?.Dispose();
            }
}
    }
}