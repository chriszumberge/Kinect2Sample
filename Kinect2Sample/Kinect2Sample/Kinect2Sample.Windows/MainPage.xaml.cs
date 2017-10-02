using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WindowsPreview.Kinect;
using Windows.UI.Xaml.Media.Imaging;
using System.ComponentModel;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Kinect2Sample
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        // Size of the RGB pixel in the bitmap
        private const int BytesPerPixel = 4;

        private KinectSensor kinectSensor = null;
        private string statusText = null;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        //Infrared Frame variables...
        public event PropertyChangedEventHandler PropertyChanged;
        public string StatusText
        {
            get { return this.statusText; }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new
                 PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        // Infrared Frame
        private InfraredFrameReader infraraedFrameReader = null;
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;

        /// <summary>
        /// The highest value that can be returned in the InfraredFrame.
        /// It is cast to a float for readability in the visualization code.
        /// </summary>
        private const float InfraredSourceValueMaximum =
            (float)ushort.MaxValue;

        /// </summary>
        /// Used to set the lower limit, post processing, of the
        /// infrared data that we will render.
        /// Increasing or decreasing this value sets a brightness
        /// "wall" either closer or further away.
        /// </summary>
        private const float InfraredOutputValueMinimum = 0.01f;

        /// <summary>
        /// The upper limit, post processing, of the
        /// infrared data that will render.
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;

        /// <summary>
        /// The InfraredSceneValueAverage value specifies the 
        /// average infrared value of the scene. 
        /// This value was selected by analyzing the average
        /// pixel intensity for a given scene.
        /// This could be calculated at runtime to handle different
        /// IR conditions of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneValueAverage = 0.08f;

        /// <summary>
        /// The InfraredSceneStandardDeviations value specifies 
        /// the number of standard deviations to apply to
        /// InfraredSceneValueAverage.
        /// This value was selected by analyzing data from a given scene.
        /// This could be calculated at runtime to handle different
        /// IR conditions of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneStandardDeviations = 3.0f;

        public MainPage()
        {
            // one sensor is currently supported 
            this.kinectSensor = KinectSensor.GetDefault();

            // get the infraredFrameDescription from the InfraredFrameSource
            FrameDescription infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
            
            // open the reader for the ifrared frames
            this.infraraedFrameReader = this.kinectSensor.InfraredFrameSource.OpenReader();

            // wire handler for frame arrival
            this.infraraedFrameReader.FrameArrived += InfraraedFrameReader_FrameArrived;

            // allocate space to put the pixels being received and converted
            this.infraredFrameData = new ushort[infraredFrameDescription.Width * infraredFrameDescription.Height];
            this.infraredPixels = new byte[infraredFrameDescription.Width * infraredFrameDescription.Height * BytesPerPixel];

            // create the bitmap to display
            this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height);

            this.CurrentFrameDescription = infraredFrameDescription;

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += KinectSensor_IsAvailableChanged;

            // use the window object as the view model in this example
            this.DataContext = this;

            // open the sensor
            this.kinectSensor.Open();

            this.InitializeComponent();
        }

        private void KinectSensor_IsAvailableChanged(KinectSensor sender, IsAvailableChangedEventArgs args)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? "Running" : "Not Available";
        }

        private void InfraraedFrameReader_FrameArrived(InfraredFrameReader sender, InfraredFrameArrivedEventArgs args)
        {
            bool infraredFrameProcessed = false;

            // InfraredFrame is IDisposable
            using (InfraredFrame infraredFrame = args.FrameReference.AcquireFrame())
            {
                if (infraredFrame != null)
                {
                    FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;

                    // verify data and write the new infrared frame to the display bitmap
                    if (((infraredFrameDescription.Width * infraredFrameDescription.Height) == this.infraredFrameData.Length) &&
                        (infraredFrameDescription.Width == this.bitmap.PixelWidth) && (infraredFrameDescription.Height == this.bitmap.PixelHeight))
                    {
                        // Copy the pixel data from the image to a temporary array
                        infraredFrame.CopyFrameDataToArray(this.infraredFrameData);

                        infraredFrameProcessed = true;
                    }
                }
            }

            // we got a frame, convert and render
            if (infraredFrameProcessed)
            {
                ConvertInfraredDataToPixels();
                RenderPixelArray(this.infraredPixels);
            }
        }

        private void ConvertInfraredDataToPixels()
        {
            // Convert the infrared to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < this.infraredFrameData.Length; ++i)
            {
                // normalize the incoming infrared data (ushort) to 
                // a float ranging from InfraredOutputValueMinimum
                // to InfraredOutputValueMaximum] by

                // 1. dividing the incoming value by the 
                // source maximum value
                float intensityRatio = (float)this.infraredFrameData[i] /
             InfraredSourceValueMaximum;

                // 2. dividing by the 
                // (average scene value * standard deviations)
                intensityRatio /=
                 InfraredSceneValueAverage * InfraredSceneStandardDeviations;

                // 3. limiting the value to InfraredOutputValueMaximum
                intensityRatio = Math.Min(InfraredOutputValueMaximum,
                    intensityRatio);

                // 4. limiting the lower value InfraredOutputValueMinimum
                intensityRatio = Math.Max(InfraredOutputValueMinimum,
                    intensityRatio);

                // 5. converting the normalized value to a byte and using 
                // the result as the RGB components required by the image
                byte intensity = (byte)(intensityRatio * 255.0f);
                this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                this.infraredPixels[colorPixelIndex++] = intensity; //Green
                this.infraredPixels[colorPixelIndex++] = intensity; //Red
                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha           
            }
        }

        private void RenderPixelArray(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            FrameDisplayImage.Source = this.bitmap;
        }
    }
}
