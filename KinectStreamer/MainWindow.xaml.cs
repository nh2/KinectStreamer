///
/// Adapted from http://www.tupperbot.com/?p=133
///
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Kinect;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Web.Script.Serialization;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace KinectStreamer
{
    /* Window visualizing the Kinect data we are streaming. */
    public partial class MainWindow : Window
    {
        // 
        private short[] pixelData;
        private byte[] colorArray;
        private int[] depthArray;

        //The bitmap that will contain the actual converted depth into an image
        private WriteableBitmap outputBitmap;
        private static readonly int Bgr32BytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        //Format of the last Depth Image. 
        //This changes when you first run the program or whenever you minimize the window 
        private DepthImageFormat lastImageFormat;

        //Identify each color layer on the R G B
        private const int RedIndex = 2;
        private const int GreenIndex = 1;
        private const int BlueIndex = 0;

        //Declare our Kinect Sensor!
        KinectSensor kinectSensor;

        PortStreamer portStreamer = new PortStreamer(1111, 100);

        public MainWindow()
        {
            InitializeComponent();

            // Only use the first Kinect
            // Subscribe to receiving depth data (highest resolution)
            //Initialize the Kinect Sensor
            kinectSensor = KinectSensor.KinectSensors[0];
            kinectSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
            kinectSensor.Start();

            // Initialize GUI slider to the current Kinect angle
            slider1.Value = kinectSensor.ElevationAngle;

            // Set up callbacks
            kinectSensor.DepthFrameReady += new EventHandler<DepthImageFrameReadyEventArgs>(DepthImageReadyCallback);

            // TODO re-enable
            //portStreamer.RunBackground();
        }

        /* Called when a depth image has been received from the Kinect. */
        private void DepthImageReadyCallback(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame imageFrame = e.OpenDepthImageFrame())
            {
                // We subscribed to depth events
                Debug.Assert(imageFrame != null);

                //Check if the format of the image has changed.
                //This always happens when you run the program for the first time and every time you minimize the window
                bool NewFormat = this.lastImageFormat != imageFrame.Format;

                if (NewFormat)
                {
                    //Update the image to the new format
                    this.pixelData = new short[imageFrame.PixelDataLength];

                    this.colorArray = new byte[imageFrame.Width * imageFrame.Height * Bgr32BytesPerPixel];
                    this.depthArray = new int[imageFrame.Width * imageFrame.Height];

                    // Create the new Bitmap and connect it to the Image
                    this.outputBitmap = new WriteableBitmap(imageFrame.Width, imageFrame.Height, 96, 96, PixelFormats.Bgr32, null);
                    this.kinectDepthImage.Source = this.outputBitmap;
                }

                //Copy the stream to its short version
                imageFrame.CopyPixelDataTo(this.pixelData);


                //Convert the pixel data into its RGB Version.
                //Here is where the magic happens
                this.UpdateDepthArray(this.pixelData);


                // TODO run this expensive stuff in a background thread
                //string sJSON = string.Join(",", this.depthArray);

                //portStreamer.Send(sJSON + "\n");

                // Gives important informatin like min/max depth.
                DepthImageStream depthStream = ((KinectSensor)sender).DepthStream;

                //ConvertDepthFrameXX(this.pixelData, depthStream);

                this.UpdateColorArray(depthStream);

                //Console.WriteLine(string.Join(",", this.colorArray));

                // Draw the color array to the image.
                this.outputBitmap.WritePixels(
                    new Int32Rect(0, 0, imageFrame.Width, imageFrame.Height),
                    this.colorArray,
                    imageFrame.Width * Bgr32BytesPerPixel,
                    0);

                // Update the Format
                this.lastImageFormat = imageFrame.Format;
                

                //Since we are coming from a triggered event, we are not expecting anything here, at least for this short tutorial.
                else { }
            }
        }




        private void UpdateDepthArray(short[] depthFrame)
        {
            for (int i = 0; i < depthFrame.Length; i++)
            {
                // Lowest 3 bits are player info, we ignore that. The rest is the depth.
                int realDepth = depthFrame[i] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                this.depthArray[i] = realDepth;
            }
        }


        private void UpdateColorArray(DepthImageStream depthImageStream)
        {
            // Kinects depth recognition is limited (e.g. 800mm to 4000mm).
            // Take that into account for scaling the colors.
            int minDist = depthImageStream.MinDepth;
            int maxDist = depthImageStream.MaxDepth;

            for (int i = 0, i8 = 0; i < depthArray.Length; i++, i8 += 4)
            {
                int depth = depthArray[i];

                byte colorScaled8Bit;

                // depth can even be -1!
                if (depth < minDist)
                {
                    colorScaled8Bit = 0;
                }
                else if (depth > maxDist)
                {
                    colorScaled8Bit = 255;
                }
                else
                {
                    int range = maxDist - minDist;
                    int whereInThatRange = depth - minDist;

                    colorScaled8Bit = (byte)(255 * whereInThatRange / range);
                }

                // Set R, G, B to the color (gives us gray)
                this.colorArray[i8 + RedIndex] = colorScaled8Bit;
                this.colorArray[i8 + GreenIndex] = colorScaled8Bit;
                this.colorArray[i8 + BlueIndex] = colorScaled8Bit;
            }
        }


        //If you move the wheel of your mouse after the slider got the focus, you will move the motor of the kinect.
        //We have to be very careful doing this since the kinect might get unresponsive if we send this command too fast.
        private void slider1_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //Calculate the new value based on the wheel movement
            if (e.Delta > 0)
            {
                slider1.Value = slider1.Value + 5;
            }
            else
            {
                slider1.Value = slider1.Value - 5;
            }
            //Send the new elevation value to our Kinect
            kinectSensor.ElevationAngle = (int)slider1.Value;
        }

    }
}
