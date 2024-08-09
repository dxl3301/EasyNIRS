
// -----------------------------------------------------------------------------------------------------------------------------------------------------
//------------------------------------------------------------START OF LIBRARY IMPORTS------------------------------------------------------------------
// -----------------------------------------------------------------------------------------------------------------------------------------------------

using Android;
using Android.App;
using Android.OS;
using Android.Content;
using Android.Hardware;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.View;
using Android.Support.V4.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Graphics;

// Bluetooth specific imports
using System;
using Android.Bluetooth;
using Java.Util;
using Java.IO;
using System.Linq;

// Plotting specific library
using MikePhil.Charting.Charts;
using MikePhil.Charting.Data;
using MikePhil.Charting.Components;
using MikePhil.Charting.Interfaces.Datasets;
using System.Collections.Generic;
using System.Threading;
using Android.Widget;
using Timer = System.Threading.Timer;
using System.Net.Mail;
using System.Net;
using System.IO;
using System.Threading.Tasks;
using Android.Support.V4.Content;

// -----------------------------------------------------------------------------------------------------------------------------------------------------
//--------------------------------------------------------------END OF LIBRARY IMPORTS------------------------------------------------------------------
// -----------------------------------------------------------------------------------------------------------------------------------------------------

namespace fNIRStreamingApp
{
    [Activity(Label = "fNIRS")]

    public class MainActivity : AppCompatActivity
    {
        // Global variables defined for Firefly fNIR Headband Project
        long startTime = 0;
        int data;
        long[] data1 = new long[3];
        int count = 0;
        int[] data_array = new int[40]; // array for *each* data point
        double real_th = 0.002;
        Byte switchm = 0;
        int updateGraph = 0;

        public System.Timers.Timer Timer1 = new System.Timers.Timer();

        // Global variables for Plotting 
        bool streamNumber = false;
        Timer timer;
        LineChart HbChart;
        int dataStatus; // values can be 0 (no data), 1 (1st data), 2 (more than one piece of data)
        LineData HbData;

        // Data streaming related variables
        static readonly object _syncLock = new object();

        // Set global values for working with Bluetooth Adapter...
        string NameOfTheDevice = "PlaceholderDeviceName";
        BluetoothAdapter adapter = BluetoothAdapter.DefaultAdapter; //create new instance of bluetooth adapter, BT Radio has already been established to be ON
        // List of paired devices
        List<string> pairedDevices = new List<string>();
        // Variable for connection state
        BluetoothSocket _socket;
        bool connectionState=false;
        // Thread for BT Adapter
        Thread btThread;

        // GUI Features
        public static Button button;
        public static Button saveButton;
        public static TextView StatusText;
        public static Switch toggleStimulus;

        // Global variables for current fNIR values
        float curr740nm = 0;
        float curr850nm = 0;
        int stimulusState = 0;
        bool newValue = false;

        // Global variable for file 
        public static Java.IO.File currDataFile;

        // File saving related variables
        Java.IO.File NIRSDir;

        protected async override void OnCreate(Bundle savedInstanceState)
        {
            StrictMode.VmPolicy.Builder builder = new StrictMode.VmPolicy.Builder();
            StrictMode.SetVmPolicy(builder.Build());

            // Ask for device permissions
            await TryToGetPermissions();

            // Deploy application layout
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            //-----------------------------Setup Bluetooth Device Selector Menu------------------------------

            //Populate list of paired bluetooth items
            pairedDevices.Add("SELECT DEVICE...");
            foreach (BluetoothDevice currDevice in adapter.BondedDevices)
            {
                pairedDevices.Add(currDevice.Name);
            }

            // Get handle for UI 'spinner' item 'btDeviceList'
            Spinner deviceList = FindViewById<Spinner>(Resource.Id.btDeviceList);

            // Create adapter for deviceList
            var deviceListAdapter = new ArrayAdapter<string>(
                this, Resource.Layout.btDeviceName, pairedDevices);

            // Assign adapter to deviceList dropdown menu
            deviceList.Adapter = deviceListAdapter;

            // Define what happens when an item from the drop down menu is selected using EventHandler
            deviceList.ItemSelected += new EventHandler<AdapterView.ItemSelectedEventArgs>(btDeviceSelected);

            //------------------------------------------------------------------------------------------------

            // Create the data plot
            HbChart = FindViewById<LineChart>(Resource.Id.HbChart);

            // Define line drawing parameters {line length, space length, phase}
            float[] lineParams740 = new float[3] { 30f, 0f, 0f };
            float[] lineParams850 = new float[3] { 30f, 10f, 0f };
            float[] lineParamsStim = new float[3] { 30f, 0f, 0f };

            // Define the data structure
            var sets = new List<ILineDataSet>
            {
                CreateLineDataSet(Color.Black, "740nm", 3f, false, false, lineParams740),
                CreateLineDataSet(Color.Red , "850nm", 3f, false, false, lineParams850),
                CreateLineDataSet(Color.Blue , "Stimulus", 0f, true, false, lineParamsStim)
            };
            HbData = new LineData(sets);

            // Initialize using dummy data of 0s..
            AddDataEntry();
            InitializeLineChart(HbChart);

            // Initialize status monitor
            StatusText = FindViewById<TextView>(Resource.Id.StatusText);
            StatusText.Text = "...Initializing...";

            // Set Timer Settings
            Timer1.Interval = 100; //Update every 100ms
            Timer1.Elapsed += new System.Timers.ElapsedEventHandler(OnTimerEvent);

            //---------------------------------------------------------------------------------------------------------------------------------------------------
            // BUTTON FUNCTIONALITY DEFINITIONS
            //---------------------------------------------------------------------------------------------------------------------------------------------------

            // 1. DATA STREAMING BUTTON

            //button.Text = "Wait for Connection";
            button = FindViewById<Button>(Resource.Id.ControlButton);
            button.Click += (o, e) => {
                if (streamNumber == false)
                {
                    deviceList.Enabled = false;
                    button.Text = "Pause Streaming";
                    Toast.MakeText(Application.Context, "Streaming", ToastLength.Short).Show();
                    streamNumber = true;
                    Timer1.Enabled = true;
                }
                else
                {
                    streamNumber = false;
                    Timer1.Enabled = false;
                    button.Text = "Resume Streaming";
                    Toast.MakeText(Application.Context, "Streaming Stopped", ToastLength.Short).Show();
                }
            };

            // 2. SAVE DATA BUTTON

            saveButton = FindViewById<Button>(Resource.Id.SaveData);
            saveButton.Click += (o, e) =>
            {
                // GET FILE NAME OF CURRENT SESSION's file
                Timer1.Enabled = false;
                var currfile = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), @"DataFile.txt");
                SaveFileLocally(currfile);

                // Clear chart
                HbChart.Invalidate();
                HbChart.Clear();

                // Reset Button label to Start Streaming to collect data on new session
                // TODO: Clear data window
                button.Text = "Start Streaming";
            };

            // 3. SETUP STIMULUS TOGGLE BUTTON

            toggleStimulus = FindViewById<Switch>(Resource.Id.toggleStimulus);

            toggleStimulus.CheckedChange += delegate (object sender, CompoundButton.CheckedChangeEventArgs e) {

                String state = (e.IsChecked ? "off" : "on");

                if (state.Equals("off")){
                    stimulusState = 1;
                }
                else {
                    stimulusState = 0;
                }

            };

        } // end of OnCreate Function


    private void btDeviceSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            Spinner spinner = (Spinner)sender;
            string deviceName = string.Format("{0}", spinner.GetItemAtPosition(e.Position));
            NameOfTheDevice = deviceName;

            if (!NameOfTheDevice.Equals("SELECT DEVICE...")){

                // Start Bluetooth data streaming on separate thread...
                Toast.MakeText(this, deviceName, ToastLength.Long).Show();
                new Thread(new ThreadStart(streamBluetoothClassicData)).Start();
            }

        }

        //------------------------------------------------------------------------------------------------------------------------
        // Request storage permissions 
        //------------------------------------------------------------------------------------------------------------------------

        readonly string[] PermissionsStorage =
        {
          Manifest.Permission.WriteExternalStorage,
          Manifest.Permission.ReadExternalStorage
        };

        const int RequestStorageId = 0;

        async Task TryToGetPermissions()
        {
            if ((int)Build.VERSION.SdkInt >= 23)
            {
                await GetStoragePermissionAsync();
                return;
            }
        }

        async Task GetStoragePermissionAsync()
        {
            //Finally request permissions with the list of permissions and Id
            RequestPermissions(PermissionsStorage, RequestStorageId);
        }

        public override async void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            switch (requestCode)
            {
                case RequestStorageId:
                    {
                        if (grantResults[0] == (int)Android.Content.PM.Permission.Granted)
                        {
                            Toast.MakeText(this, "Storage permissions granted", ToastLength.Short).Show();

                        }
                        else
                        {
                            //Permission Denied :(
                            Toast.MakeText(this, "Storage permissions denied", ToastLength.Short).Show();

                        }
                    }
                    break;
            }
            //base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }


        //---------------------------------------------------------------------------------------------------------------------END

        public string SaveFileLocally(string fileName)
        {

            // Define variable for DCIM Directory
            Java.IO.File DCIMDir = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDcim);
            NIRSDir = new Java.IO.File(DCIMDir + "/NIRS/");

            // CREATE NIRS Data Directory inside DCIM
            if (!NIRSDir.IsDirectory)//only create directory if not already there!
            {
                NIRSDir.Mkdir();
            }
            
            // Create session folder names (one folder per day)
            string sessionID = "/" + DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" + DateTime.Now.Day.ToString() + "/";
            string sessionDirName = NIRSDir + "/" + sessionID;

            // Create new File object
            Java.IO.File sessionDir = new Java.IO.File(sessionDirName);

            //Create session directory if it doesn't exist!
            if (!sessionDir.IsDirectory)
            {
                sessionDir.Mkdir();
            }

            // Create filename for specific session (multiple files per day)
            string fileID = DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" + DateTime.Now.Day.ToString() + "-" + DateTime.Now.Hour.ToString() + "-" + DateTime.Now.Minute.ToString() + ".csv";
            string destFile = sessionDir + fileID;

            // Copy and rename local DataFile.txt to DCIM/NIRS folder
            System.IO.File.Copy(fileName, destFile);

            // Notify the user/programmer of file creation
            System.Console.WriteLine("CREATED FILE: " + destFile + "/n");
            Toast.MakeText(Application.Context, "Data Saved", ToastLength.Short).Show();

            return destFile;
        }

        // This function writes the data to the local DataFile.txt
        public void writeDataToFile(string destination, string dataString)
        {
            System.IO.File.AppendAllText(destination, dataString);
            System.IO.File.AppendAllText(destination, "\r");
            System.Diagnostics.Debug.WriteLine(System.IO.File.ReadAllText(destination));
        }

        private void Timer1_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            throw new NotImplementedException();
        }

        protected override void OnResume()
        {
            base.OnResume();
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        private void InitializeLineChart(LineChart mChart)
        {

            mChart.Description.Enabled = false;
            mChart.SetTouchEnabled(false);
            mChart.SetScaleEnabled(false);
            var legend = mChart.Legend;
            legend.Enabled = true;
            legend.TextSize = 12f;
            mChart.SetDrawGridBackground(false);
            mChart.SetDrawBorders(false);
            mChart.SetHardwareAccelerationEnabled(true);

            // Define line drawing parameters {line length, space length, phase}
            float[] lineParams740 = new float[3] { 30f, 0f, 0f };
            float[] lineParams850 = new float[3] { 30f, 10f, 0f };
            float[] lineParamsStim = new float[3] { 30f, 0f, 0f };

            var sets = new List<ILineDataSet>
            {                
                CreateLineDataSet(Color.Black, "740nm", 3f, false, false, lineParams740),
                CreateLineDataSet(Color.Red , "850nm", 3f, false, false, lineParams850),
                CreateLineDataSet(Color.Blue , "Stimulus", 0f, true, false, lineParamsStim)
            };

            // Define charting data
            LineData data = new LineData(sets);
            mChart.Data = HbData;

            // Enable x
            XAxis xl = mChart.XAxis;
            xl.SetDrawGridLines(true);
            xl.SetAvoidFirstLastClipping(true);
            xl.Enabled = true;
            xl.Position = XAxis.XAxisPosition.Bottom;
            xl.TextSize = 20f;

            // Enable left axis
            YAxis leftAxis = mChart.AxisLeft;
            leftAxis.SetDrawGridLines(true);
            leftAxis.SetAxisMinValue(0f);
            leftAxis.SetLabelCount(4, true);
            leftAxis.TextSize = 20f;

            // Disable right axis
            YAxis rightAxis = mChart.AxisRight;
            rightAxis.Enabled = false;

        }

        // Create data structure for storing line chart data...
        private static LineDataSet CreateLineDataSet(Color mcolor, string mLabel, float lineThickness, bool highlight, bool markCircles, float[] lineParams)
        {
            LineDataSet set = new LineDataSet(null, "Data")
            {
                AxisDependency = YAxis.AxisDependency.Left,
                LineWidth = lineThickness,
                Color = mcolor,
                HighlightEnabled = highlight,
                Label = mLabel
        };
            set.EnableDashedLine(lineParams[0], lineParams[1], lineParams[2]); // line length, space length, phase
            set.EnableDashedHighlightLine(lineParams[0], lineParams[1], lineParams[2]); // line length, space length, phase
            //et.SetColor(Color.Gray, 0);
            set.SetDrawValues(false);
            set.SetDrawCircles(markCircles);
            set.SetCircleColor(mcolor);
            set.SetDrawFilled(highlight);
            set.SetMode(LineDataSet.Mode.CubicBezier);
            set.CubicIntensity = 0.2f;
            return set;
        }

        private void OnTimerEvent(object sender, EventArgs e)
        {
            streamingData();
        }

        public void streamingData()
        {
            RunOnUiThread(() =>
            {
                AddDataEntry();
            });
        }

        // Add datapoint for line chart...
        public void AddDataEntry()
        {

            // Get path to 
            var currfile = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), @"DataFile.txt");
                
            // Condition: App is past initialization
            // Only write data to storage file if this is NOT about initialization
            if (streamNumber)
            {
                // Convert data to string
                string dataString = getTimeString() + "," + getTimeStamp().ToString() + "," + curr850nm.ToString() + "," + curr740nm.ToString() + "," + stimulusState.ToString();
                // Append data to file
                writeDataToFile(currfile, dataString);
            }

            ILineDataSet set = (ILineDataSet)HbData.DataSets[0];
            HbData.AddEntry(new Entry(set.EntryCount, curr740nm), 0); // get last data value
            HbData.AddEntry(new Entry(set.EntryCount, curr850nm), 1); // get last data value
            HbData.AddEntry(new Entry(set.EntryCount, stimulusState), 2); // get last data value

            // Get maximum value
            //float maxValue = HbData.GetYMax(HbChart.AxisLeft.GetAxisDependency());
            //if (maxValue < 0.05) { maxValue = 0.2f; }

            // Inform charting listener that values are updated
            HbChart.NotifyDataSetChanged();
            // limit the number of visible entries
            HbChart.SetVisibleXRangeMaximum(100);
            // move to the latest entry
            HbChart.MoveViewToX(HbData.EntryCount);
            // set y axis limit
            //HbChart.SetVisibleYRangeMaximum(maxValue, HbChart.AxisLeft.GetAxisDependency());
            // refresh plot
            HbChart.Invalidate();

        }

        // streamBluetoothData | Accesses bluetooth data stream from device
        public async void streamBluetoothClassicData()
        {

            // Define new object to save data to
            // DataSet = new fNIR.Models.OxyPlotInfo();
            // List<fNIR.Models.OxyPlotItem> DataItems = new List<fNIR.Models.OxyPlotItem>();

            // Talk to bluetooth socket
            // Source: https://brianpeek.com/connect-to-a-bluetooth-device-with-xamarinandroid/

            // Stop Bluetooth device discovery
            adapter.CancelDiscovery();
            
            BluetoothDevice device = (from bd in adapter.BondedDevices where bd.Name == NameOfTheDevice select bd).FirstOrDefault(); // select device to be paired here..

            // Check if device is in list of paired devices...
            if (device == null)
            {
                System.Diagnostics.Debug.WriteLine("Device NOT paired....Pair Device First");
                StatusText.Text = "Pair Device First";
            }
            else
            {
                try {
                    // Setup RF Communication Socket With Android for communication
                    UUID uuid = UUID.FromString("00001101-0000-1000-8000-00805f9b34fb");
                    //BluetoothSocket _socket = device.CreateRfcommSocketToServiceRecord(uuid); //for this device, create a RF communication socket
                    _socket = device.CreateInsecureRfcommSocketToServiceRecord(uuid); //for this device, create a RF communication socket

                    // Check if socket was created successfully
                    if (_socket != null)
                    {
                        // Stream bluetooth data....
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("Attempting to connect device.....");
                            // Setup connection to socket...

                            await _socket.ConnectAsync();

                            if (_socket.IsConnected)
                            {
                                connectionState = true;
                                System.Diagnostics.Debug.WriteLine("DEVICE CONNECTED!!!");
                                StatusText.Text = "Device Connected";
                                StatusText.SetTextColor(Color.ParseColor("blue"));

                                // Start data timer...
                                startTime = getTimeStamp();

                                while (true) //
                                {
                                    data = _socket.InputStream.ReadByte();

                                    // Extract complete packets of data from the given datastream...
                                    GetDataPoint(data);

                                }

                            }
                        }
                        catch (System.IO.IOException)
                        {
                            connectionState = false;
                            StatusText.Text = "Connection failed...";
                            _socket.Dispose();
                        }

                    }

                }
                catch (System.IO.IOException) {
                    connectionState = false;
                    System.Diagnostics.Debug.WriteLine("Connection failed...");
                    StatusText.Text = "Connection failed...";
                }
            }
        }

        // GetDataPoint | Extracts relevant data points from bluetooth data stream
        private void GetDataPoint(int data) // Extract complete packets of data from the given datastream...
        {
            int resR, resIR;
            int chip;
            long D1R, D1IR, D2R, D2IR, D3R, D3IR, D4R, D4IR;
            int res0R, res0IR;
            double D1Rn, D1IRn, D2Rn, D2IRn, D3Rn, D3IRn, D4Rn, D4IRn;

            try
            {

                if (data == 13) // Endpoint of the data has been reached
                {
                    data1[0] = data;
                    data1[1] = 0;
                    data1[2] = 0;
                }
                else //We have not reached the endpoint..
                {
                    if (((data1[0] == 13) && (data == 17)) || ((data1[0] == 13) && (data == 11))) //Select 
                    {
                        data1[1] = data;
                        data1[2] = 0;
                        if (data == 17) //5348 Chip
                        {
                            chip = 0;
                        }
                        else
                        {
                            if (data == 11) // 6626 Chip
                            {
                                chip = 1;
                            }
                        }
                    }
                    else
                    {
                        if (((data1[0] == 13) && (data1[1] == 17) && (data == 19)) || ((data1[0] == 13) && (data1[1] == 11) && (data == 19)))
                        {
                            data1[2] = (int)data;
                            data1[0] = 0;
                            data1[1] = 0;
                        }
                        else
                        {
                            data1[0] = 0;
                            data1[1] = 0;
                            data1[2] = 0;
                        }
                    }
                }

                // Check if the data arrays have been filled
                if (data1[2] != 19) // i.e. if the arrays are incomplete...
                {
                    data_array[count] = data;
                    count++;
                    if (count > 40)
                    {    // Just precaution
                        count = 0;
                    }

                }
                else //i.e. if the data array is complete...
                {
                    data1[2] = 0;
                    count = 0;

                    D1R = data_array[0] + 256 * data_array[1] + 65536 * data_array[2] + 256 * 65536 * data_array[3];
                    D2R = data_array[4] + 256 * data_array[5] + 65536 * data_array[6] + 256 * 65536 * data_array[7];
                    D3R = data_array[8] + 256 * data_array[9] + 65536 * data_array[10] + (256 * 65536 * data_array[11]);
                    D1IR = data_array[12] + 256 * data_array[13] + 65536 * data_array[14] + 256 * 65536 * data_array[15];
                    D2IR = data_array[16] + 256 * data_array[17] + 65536 * data_array[18] + 256 * 65536 * data_array[19];
                    D3IR = data_array[20] + 256 * data_array[21] + 65536 * data_array[22] + 256 * 65536 * data_array[23];
                    D4R = data_array[24] + 256 * data_array[25] + 65536 * data_array[26] + 256 * 65536 * data_array[27];
                    D4IR = data_array[28] + 256 * data_array[29] + 65536 * data_array[30] + 256 * 65536 * data_array[31];

                    resR = data_array[32] + 256 * data_array[33];
                    resIR = data_array[34] + 256 * data_array[35];

                    res0R = resR;
                    res0IR = resR;

                    if (res0R > 460)
                    {
                        // Current array is looking at data from 5438a chip
                        D1Rn = D1R * 2.5 / (res0R * 4095);
                        D2Rn = D2R * 2.5 / (res0R * 4095);
                        D3Rn = D3R * 2.5 / (res0R * 4095);
                        D4Rn = D4R * 2.5 / (res0R * 4095);

                        D1IRn = D1IR * 2.5 / (res0IR * 4095);
                        D2IRn = D2IR * 2.5 / (res0IR * 4095);
                        D3IRn = D3IR * 2.5 / (res0IR * 4095);
                        D4IRn = D4IR * 2.5 / (res0IR * 4095);

                        // Get current timestamp
                        double currTimeSeconds = (getTimeStamp() - startTime) / ((double)1000);
                        int indicatorTime = (int)currTimeSeconds;

                        if ((D4Rn < 3.3) && (D4IRn < 3.3)) {

                            if ((D4Rn > 0) && (D4IRn > 0))
                            {
                                // Add data to plot
                                curr740nm = Convert.ToSingle(D4Rn);
                                curr850nm = Convert.ToSingle(D4IRn);
                            }
                        }

                    }
                }
            }

            catch (TimeoutException)
            {

            }

        }

        /*-----------------------------------------------------------------------------------------------------------------------------
        // FNIR PROCESSING RELATED FUNCTIONS
        // ----------------------------------------------------------------------------------------------------------------------------
        // INPUT: time_stamp, red_voltage, IR_voltage
        // OUTPUT: time_stamp, H2Hb, HbO
        -----------------------------------------------------------------------------------------------------------------------------*/

        // Get current time stamp for data
        private long getTimeStamp()
        {
            int hour = DateTime.Now.Hour * 60 * 60 * 1000;
            int minute = DateTime.Now.Minute * 60 * 1000;
            int second = DateTime.Now.Second * 1000;
            int milisecond = DateTime.Now.Millisecond;
            long timeStamp = hour + minute + second + milisecond;
            return timeStamp;
        }

        private string getTimeString()
        {
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;
            int day = DateTime.Now.Day;
            int hour = DateTime.Now.Hour;
            int minute = DateTime.Now.Minute;
            int second = DateTime.Now.Second;
            int milisecond = DateTime.Now.Millisecond;
            string timeStamp = year.ToString() + "_" + month.ToString() + "_" + day.ToString() + "," + hour.ToString() + ":" + minute.ToString() + ":" + second.ToString() + ":" + milisecond.ToString();
            return timeStamp;
        }

        // Convert data point to Hb values
        private double[] calculate_dHbValues(double D1_740, double D1_850, double D4_740, double D4_850)
        {
            // Coefficients
            double d = 2.8;
            double e_Hb_740 = 1.1;
            double e_HbO2_740 = 0.45;
            double e_Hb_850 = 0.7;
            double e_HbO2_850 = 1.05;
            double DPF_740 = 218.5716;
            double DPF_850 = 220.4923;

            // Calculate dHb and dHbO2 values
            double dHb = (1 / d) * (e_HbO2_850 * D4_740 * DPF_850 - e_HbO2_740 * D4_850 * DPF_740);
            double dHbO2 = (1 / d) * (-e_Hb_850 * D4_740 * DPF_850 + e_Hb_740 * D4_850 * DPF_740);

            // Return Hemoglobin Data arrays
            double[] HbArray = { dHb, dHbO2 };
            return HbArray;
        }

    }

}

