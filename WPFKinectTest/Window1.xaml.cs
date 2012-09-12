///----------------------------------------------------------
///Kinect Depth Example
///Date: February 12th, 2012
///--------------------
///Authors:
/// - Jesus Dominguez:
/// 
/// - Angel Hernandez:
///     @mifulapirus
///     www.tupperbot.com
///-----------------------
///All code is based on the Kinect Explorer Example. 
///-----------------------------------------------------------------------------------------------
///Summary:
///This is just a simplification of the Explorer Example code that comes
///with the final version of the Kinect SDK released by Microsoft in February.
///This example shows the minimum code required to get the depth image drawn in a WPF program.
///It is for sure not a perfect and super safe code, but it is just intended to show how to do 
///this in a simple way.
///-----------------------------------------------------------------------------------------------
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

namespace WPFKinectTest
{
    // State object for reading client data asynchronously
    public class StateObject
    {
        // Client  socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();

        public ManualResetEvent isReadyEvent = new ManualResetEvent(true);
    }


    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        //Declare some global variables
        private short[] pixelData;
        private byte[] depthFrame32;
        private int[] actualDepthFrame;
        
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

        
        private ManualResetEvent allDone = new ManualResetEvent(false);

        BackgroundWorker bw = new BackgroundWorker();

        List<StateObject> listeners = new List<StateObject>();

        ConcurrentQueue<StateObject> newListeners = new ConcurrentQueue<StateObject>();

        public Window1()
        {
            InitializeComponent();

            //Select the first kinect found
            kinectSensor = KinectSensor.KinectSensors[0];   
            //Set up the depth stream to be the largest possible
            kinectSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);  
            //Initialize the Kinect Sensor
            kinectSensor.Start();
            //Subscribe to an event that will be triggered every time a new frame is ready
            kinectSensor.DepthFrameReady += new EventHandler<DepthImageFrameReadyEventArgs>(DepthImageReady);
            //Read the elevation value of the Kinect and assign it to the slider so it doesn't look weird when the program starts 
            slider1.Value = kinectSensor.ElevationAngle;

            try
            {

                bw.DoWork += new DoWorkEventHandler(bw_DoWork);
                bw.RunWorkerAsync();
            }
            catch (Exception)
            {
                Console.WriteLine("toplevel exception");
            }
            

        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {

                StartListening();
            }
            catch (Exception)
            {
                Console.WriteLine("toplevel exception 2");
            }
            
        }

        public void StartListening()
        {
            // Data buffer for incoming data.
            byte[] bytes = new Byte[1024];

            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 1111);

            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    Console.WriteLine("Loop entry");
                    // Set the event to nonsignaled state.
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);

                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();

                    Console.WriteLine("Got connection");
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("xxx " + e.ToString());
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket) ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.
            StateObject state = new StateObject();

            newListeners.Enqueue(state);

            state.workSocket = handler;
            //handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
            //handler.BeginDisconnect(false, new AsyncCallback(DisconnectCallback), state);
        }

        public static void DisconnectCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            Console.WriteLine("disconnect from " + handler + " : " + state);

            handler.EndDisconnect(ar);
        }


        public static bool IsConnected(Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) {
                Console.WriteLine("SocketException in IsConnected");
                return false;
            }
            catch (Exception)
            {
                Console.WriteLine("other Exception in IsConnected");
                return false;
            }
        }

        /// <summary>
        /// DepthImageReady:
        /// This function will be called every time a new depth frame is ready
        /// </summary>
        private void DepthImageReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame imageFrame = e.OpenDepthImageFrame())
            {
            	//We expect this to be always true since we are coming from a triggered event
                if (imageFrame != null)
                {
                    //Check if the format of the image has changed.
                    //This always happens when you run the program for the first time and every time you minimize the window
                    bool NewFormat = this.lastImageFormat != imageFrame.Format;

                    if (NewFormat)
                    {
                        //Update the image to the new format
                        this.pixelData = new short[imageFrame.PixelDataLength];
                        this.depthFrame32 = new byte[imageFrame.Width * imageFrame.Height * Bgr32BytesPerPixel];
                        this.actualDepthFrame = new int[imageFrame.Width * imageFrame.Height];
                        
                        //Create the new Bitmap
                        this.outputBitmap = new WriteableBitmap(
                           imageFrame.Width,
                           imageFrame.Height,
                           96,  // DpiX
                           96,  // DpiY
                           PixelFormats.Bgr32,
                           null);

                        this.kinectDepthImage.Source = this.outputBitmap;
                    }

                    //Copy the stream to its short version
                    imageFrame.CopyPixelDataTo(this.pixelData);


                    //Convert the pixel data into its RGB Version.
                    //Here is where the magic happens
                    this.ConvertDepthFrame2(this.pixelData);


                    //Console.WriteLine(this.actualDepthFrame[19]);
                    //Console.WriteLine(String.Join(":", this.actualDepthFrame).Length);
                    var testValue = this.actualDepthFrame[19].ToString();
                    var toRemove = new List<StateObject>();
                    Console.WriteLine("having " + listeners.Count);
                    foreach (StateObject listener in listeners)
                    {
                        var oSerializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        //string sJSON = oSerializer.Serialize(this.actualDepthFrame);
                        string sJSON = string.Join(",", this.actualDepthFrame);

                        byte[] bytes = Encoding.Default.GetBytes(sJSON + "\n");
                        Socket socket = listener.workSocket;
                        try {
                                Console.WriteLine("waiting for listener to receive possible earlier sends");
                                listener.isReadyEvent.WaitOne();
                                Console.WriteLine("listener is now free");
                                try
                                {
                                    socket.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, EndSend, listener);
                                }
                                catch (SocketException ex)
                                {
                                    
                                    Console.WriteLine("fail fail fail");
                                    toRemove.Add(listener);
                                    Console.WriteLine("fail here " + ex.ToString());
                                }
                                Thread.Sleep(1000);
                        } catch (ObjectDisposedException) {
                            Console.WriteLine("disconnected");
                            toRemove.Add(listener);
                        }
                    }
                    Console.WriteLine("Removing " + toRemove.Count);
                    foreach (StateObject listener in toRemove)
                    {
                        listeners.Remove(listener);
                    }
                    Console.WriteLine("now having " + listeners.Count);
                    {
                        StateObject listenerToAdd;
                        while (newListeners.TryDequeue(out listenerToAdd))
                        {
                            listeners.Add(listenerToAdd);
                        }
                    }

                    ////Copy the RGB matrix to the bitmap to make it visible
                    //this.outputBitmap.WritePixels(
                    //    new Int32Rect(0, 0, imageFrame.Width, imageFrame.Height), 
                    //    convertedDepthBits,
                    //    imageFrame.Width * Bgr32BytesPerPixel,
                    //    0);

                    // Update the Format
                    this.lastImageFormat = imageFrame.Format;
                }

                //Since we are coming from a triggered event, we are not expecting anything here, at least for this short tutorial.
                else { }
            }
        }


        private void EndSend(IAsyncResult ar)
        {
            try
            {
                StateObject state = (StateObject)ar.AsyncState;
                state.workSocket.EndSend(ar);
                Console.WriteLine("setting listener free");
                state.isReadyEvent.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine("yy " + e.ToString());
            }
            Console.WriteLine("yy Okay");
        }

        private void ConvertDepthFrame2(short[] depthFrame)
        {
            for (int i16 = 0; i16 < depthFrame.Length; i16++)
            {
                // Lowest 3 bits are player info, we ignore that
                int realDepth = depthFrame[i16] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                this.actualDepthFrame[i16] = realDepth;
            }
        }



        /// <summary>
        /// ConvertDepthFrame:
        /// Converts the depth frame into its RGB version taking out all the player information and leaving only the depth.
        /// </summary>
        private byte[] ConvertDepthFrame(short[] depthFrame, DepthImageStream depthStream)
        {
            //Run through the depth frame making the correlation between the two arrays
            for (int i16 = 0, i32 = 0; i16 < depthFrame.Length && i32 < this.depthFrame32.Length; i16++, i32 += 4)
            {
                //We don't care about player's information here, so we are just going to rule it out by shifting the value.
                int realDepth = depthFrame[i16] >> DepthImageFrame.PlayerIndexBitmaskWidth;

                this.actualDepthFrame[i16] = realDepth;

                //We are left with 13 bits of depth information that we need to convert into an 8 bit number for each pixel.
                //There are hundreds of ways to do this. This is just the simplest one.
                //Lets create a byte variable called Distance. 
                //We will assign this variable a number that will come from the conversion of those 13 bits.
                byte Distance = 0;
                
                //XBox Kinects (default) are limited between 800mm and 4096mm.
                int MinimumDistance = 800;
                int MaximumDistance = 4096;

                //XBox Kinects (default) are not reliable closer to 800mm, so let's take those useless measurements out.
                //If the distance on this pixel is bigger than 800mm, we will paint it in its equivalent gray
                if (realDepth > MinimumDistance)
                {
                    //Convert the realDepth into the 0 to 255 range for our actual distance.
                    //Use only one of the following Distance assignments 
                    //White = Far
                    //Black = Close
                    //Distance = (byte)(((realDepth - MinimumDistance) * 255 / (MaximumDistance-MinimumDistance)));

                    //White = Close
                    //Black = Far
                    Distance = (byte)(255-((realDepth - MinimumDistance) * 255 / (MaximumDistance - MinimumDistance)));
                    
                    //Use the distance to paint each layer (R G & B) of the current pixel.
                    //Painting R, G and B with the same color will make it go from black to gray
                    this.depthFrame32[i32 + RedIndex] = (byte)(Distance);
                    this.depthFrame32[i32 + GreenIndex] = (byte)(Distance);
                    this.depthFrame32[i32 + BlueIndex] = (byte)(Distance);
                }

                //If we are closer than 800mm, the just paint it red so we know this pixel is not giving a good value
                else
                {
                    this.depthFrame32[i32 + RedIndex] = 150;
                    this.depthFrame32[i32 + GreenIndex] = 0;
                    this.depthFrame32[i32 + BlueIndex] = 0;
                }
            }
            //Now that we are done painting the pixels, we can return the byte array to be painted
            return this.depthFrame32;
        }

        //If you move the wheel of your mouse after the slider got the focus, you will move the motor of the kinect.
        //We have to be very careful doing this since the kinect might get unresponsive if we send this command too fast.
        private void slider1_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //Calculate the new value based on the wheel movement
            if (e.Delta > 0) { slider1.Value = slider1.Value + 5; }
            else { slider1.Value = slider1.Value - 5; }
            //Send the new elevation value to our Kinect
            kinectSensor.ElevationAngle = (int)slider1.Value;
        }

   }
}