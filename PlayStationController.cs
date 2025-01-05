using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;

namespace RetroUI
{
    public class PlayStationController : IDisposable
    {
        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(ref Guid hidGuid);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetInputReport(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, 
            [MarshalAs(UnmanagedType.LPWStr)] string enumerator, 
            IntPtr hwndParent, 
            uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid interfaceClassGuid;
            public int flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DevicePath;
        }

        private const int DIGCF_PRESENT = 0x0002;
        private const int DIGCF_DEVICEINTERFACE = 0x0010;

        // PS4 Controller IDs
        private const ushort PS4_VENDOR_ID = 0x054C;
        private const ushort PS4_PRODUCT_ID = 0x05C4; // DualShock 4
        private const ushort PS4_PRODUCT_ID_V2 = 0x09CC; // DualShock 4 v2

        // PS5 Controller IDs
        private const ushort PS5_VENDOR_ID = 0x054C;
        private const ushort PS5_PRODUCT_ID = 0x0CE6; // DualSense

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        private SafeFileHandle deviceHandle;
        private FileStream hidStream;
        private readonly byte[] inputBuffer = new byte[64];
        private bool _isConnected;
        private readonly Timer pollTimer;
        private bool isPS5Controller;
        private bool _inputDisabled;

        public bool InputDisabled
        {
            get => _inputDisabled;
            set => _inputDisabled = value;
        }

        public bool IsConnected 
        { 
            get 
            {
                Update();
                return _isConnected;
            }
            private set => _isConnected = value; 
        }

        public short LeftThumbX { get; private set; }
        public short LeftThumbY { get; private set; }
        public PSButtons Buttons { get; private set; }

        public PlayStationController()
        {
            Debug.WriteLine("Initializing PlayStation Controller...");
            InitializeController();
            pollTimer = new Timer(PollController, null, 0, 16);
        }

        private void InitializeController()
        {
            try
            {
                string devicePath = FindPlayStationController();
                if (!string.IsNullOrEmpty(devicePath))
                {
                    OpenDevice(devicePath);
                    SendInitializationSequence();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PlayStation Controller initialization error: {ex.Message}");
            }
        }

        private string FindPlayStationController()
        {
            Guid hidGuid = Guid.Empty;
            HidD_GetHidGuid(ref hidGuid);
            Debug.WriteLine("Searching for PlayStation controller...");

            var deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet != IntPtr.Zero)
            {
                var deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                uint deviceIndex = 0;
                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, deviceIndex, ref deviceInterfaceData))
                {
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, 
                            detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                        {
                            string devicePath = Marshal.PtrToStringAuto(IntPtr.Add(detailDataBuffer, 4));
                            var deviceHandle = CreateFile(devicePath,
                                GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                IntPtr.Zero,
                                OPEN_EXISTING,
                                FILE_FLAG_OVERLAPPED,
                                IntPtr.Zero);

                            if (!deviceHandle.IsInvalid)
                            {
                                var attributes = new HIDD_ATTRIBUTES();
                                attributes.Size = Marshal.SizeOf(attributes);

                                if (HidD_GetAttributes(deviceHandle.DangerousGetHandle(), ref attributes))
                                {
                                    // Check for both PS4 and PS5 controllers
                                    if ((attributes.VendorID == PS4_VENDOR_ID && 
                                        (attributes.ProductID == PS4_PRODUCT_ID || 
                                         attributes.ProductID == PS4_PRODUCT_ID_V2)) ||
                                        (attributes.VendorID == PS5_VENDOR_ID && 
                                         attributes.ProductID == PS5_PRODUCT_ID))
                                    {
                                        Debug.WriteLine($"Found PlayStation controller: VID={attributes.VendorID:X4}, PID={attributes.ProductID:X4}");
                                        deviceHandle.Close();
                                        return devicePath;
                                    }
                                }
                                deviceHandle.Close();
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                    deviceIndex++;
                }
            }
            Debug.WriteLine("No PlayStation controller found");
            return null;
        }

        private void SendInitializationSequence()
        {
            if (hidStream != null)
            {
                try
                {
                    if (isPS5Controller)
                    {
                        // PS5 DualSense initialization
                        byte[] initCommand = new byte[64];
                        initCommand[0] = 0x02; // Report ID
                        hidStream.Write(initCommand, 0, initCommand.Length);
                    }
                    else
                    {
                        // PS4 DualShock 4 initialization
                        byte[] initCommand = new byte[64];
                        initCommand[0] = 0x05; // Report ID
                        hidStream.Write(initCommand, 0, initCommand.Length);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error sending initialization sequence: {ex.Message}");
                }
            }
        }

        private void PollController(object state)
        {
            Update();
        }

        public void Update()
        {
            if (hidStream != null && hidStream.CanRead)
            {
                try
                {
                    if (HidD_GetInputReport(deviceHandle.DangerousGetHandle(), inputBuffer, (uint)inputBuffer.Length))
                    {
                        ParseInputReport(inputBuffer);
                        _isConnected = true;
                        
                        // Clear inputs if disabled
                        if (_inputDisabled)
                        {
                            LeftThumbX = 0;
                            LeftThumbY = 0;
                            Buttons = PSButtons.None;
                        }
                    }
                    else
                    {
                        _isConnected = false;
                    }
                }
                catch
                {
                    _isConnected = false;
                }
            }
        }

        private void ParseInputReport(byte[] report)
        {
            Buttons = PSButtons.None;

            if (isPS5Controller)
            {
                // PS5 DualSense button mapping
                if ((report[8] & 0x20) != 0) Buttons |= PSButtons.Cross;
                if ((report[8] & 0x40) != 0) Buttons |= PSButtons.Circle;
                if ((report[8] & 0x10) != 0) Buttons |= PSButtons.Square;
                if ((report[8] & 0x80) != 0) Buttons |= PSButtons.Triangle;
                
                // Analog sticks
                LeftThumbX = (short)(report[1] - 128);
                LeftThumbY = (short)(report[2] - 128);
            }
            else
            {
                // PS4 DualShock 4 button mapping
                if ((report[5] & 0x20) != 0) Buttons |= PSButtons.Cross;
                if ((report[5] & 0x40) != 0) Buttons |= PSButtons.Circle;
                if ((report[5] & 0x10) != 0) Buttons |= PSButtons.Square;
                if ((report[5] & 0x80) != 0) Buttons |= PSButtons.Triangle;
                
                // Analog sticks
                LeftThumbX = (short)(report[1] - 128);
                LeftThumbY = (short)(report[2] - 128);
            }
        }

        private void OpenDevice(string devicePath)
        {
            Debug.WriteLine($"Attempting to open PlayStation controller: {devicePath}");
            
            deviceHandle = CreateFile(devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (!deviceHandle.IsInvalid)
            {
                Debug.WriteLine("Device handle created successfully");
                hidStream = new FileStream(deviceHandle, FileAccess.ReadWrite, 64, true);
                _isConnected = true;
                
                // Determine if it's a PS5 controller
                var attributes = new HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(attributes);
                
                if (HidD_GetAttributes(deviceHandle.DangerousGetHandle(), ref attributes))
                {
                    isPS5Controller = attributes.VendorID == PS5_VENDOR_ID && 
                                     attributes.ProductID == PS5_PRODUCT_ID;
                    Debug.WriteLine($"Connected to {(isPS5Controller ? "PS5" : "PS4")} controller");
                }
            }
            else
            {
                Debug.WriteLine($"Failed to open device. Error code: {Marshal.GetLastWin32Error()}");
                _isConnected = false;
            }
        }

        public void Dispose()
        {
            pollTimer?.Dispose();
            hidStream?.Dispose();
            deviceHandle?.Dispose();
        }
    }

    [Flags]
    public enum PSButtons
    {
        None = 0,
        Cross = 1 << 0,
        Circle = 1 << 1,
        Square = 1 << 2,
        Triangle = 1 << 3,
        L1 = 1 << 4,
        R1 = 1 << 5,
        L2 = 1 << 6,
        R2 = 1 << 7,
        Share = 1 << 8,
        Options = 1 << 9,
        L3 = 1 << 10,
        R3 = 1 << 11,
        PS = 1 << 12,
        TouchPad = 1 << 13,
        DPadUp = 1 << 14,
        DPadDown = 1 << 15,
        DPadLeft = 1 << 16,
        DPadRight = 1 << 17
    }
} 