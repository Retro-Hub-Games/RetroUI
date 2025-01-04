using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;

namespace RetroUI
{
    public class SwitchProController : IDisposable
    {
        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(ref Guid hidGuid);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetInputReport(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, 
            [MarshalAs(UnmanagedType.LPWStr)] string enumerator, 
            IntPtr hwndParent, 
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

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
        private const ushort SWITCH_PRO_VENDOR_ID = 0x057E;
        private const ushort SWITCH_PRO_PRODUCT_ID_1 = 0x2009; // Original Pro Controller
        private const ushort SWITCH_PRO_PRODUCT_ID_2 = 0x2007; // Charging Grip
        private const ushort SWITCH_PRO_PRODUCT_ID_3 = 0x2017; // HORI variant
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
        public SwitchProButtons Buttons { get; private set; }

        public SwitchProController()
        {
            pollTimer = new Timer(PollController, null, 0, 16); // Poll every 16ms
            InitializeController();
        }

        private void InitializeController()
        {
            try
            {
                string devicePath = FindSwitchProController();
                if (!string.IsNullOrEmpty(devicePath))
                {
                    OpenDevice(devicePath);
                    SendInitializationSequence();
                    Debug.WriteLine("Switch Pro Controller initialized successfully");
                }
                else
                {
                    Debug.WriteLine("No Switch Pro Controller found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Switch Pro Controller initialization error: {ex.Message}");
            }
        }

        private string FindSwitchProController()
        {
            Guid hidGuid = Guid.Empty;
            HidD_GetHidGuid(ref hidGuid);

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
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 5); // cbSize must be 5 for 32-bit or 8 for 64-bit

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
                                    if (attributes.VendorID == SWITCH_PRO_VENDOR_ID && 
                                        (attributes.ProductID == SWITCH_PRO_PRODUCT_ID_1 || 
                                         attributes.ProductID == SWITCH_PRO_PRODUCT_ID_2 ||
                                         attributes.ProductID == SWITCH_PRO_PRODUCT_ID_3))
                                    {
                                        Debug.WriteLine($"Found Switch Pro Controller: VID={attributes.VendorID:X4}, PID={attributes.ProductID:X4}");
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
            return null;
        }

        private void OpenDevice(string devicePath)
        {
            deviceHandle = CreateFile(devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (!deviceHandle.IsInvalid)
            {
                hidStream = new FileStream(deviceHandle, FileAccess.ReadWrite, 64, true);
                IsConnected = true;
            }
        }

        private void SendInitializationSequence()
        {
            if (hidStream != null)
            {
                try
                {
                    // Standard Switch Pro initialization sequence
                    byte[] handshake = new byte[] { 0x80, 0x04 };
                    byte[] enableIMU = new byte[] { 0x40, 0x01 };
                    byte[] enableVibration = new byte[] { 0x48, 0x01 };
                    byte[] getDeviceInfo = new byte[] { 0x02 };

                    hidStream.Write(handshake, 0, handshake.Length);
                    Thread.Sleep(100);
                    hidStream.Write(enableIMU, 0, enableIMU.Length);
                    Thread.Sleep(100);
                    hidStream.Write(enableVibration, 0, enableVibration.Length);
                    Thread.Sleep(100);
                    hidStream.Write(getDeviceInfo, 0, getDeviceInfo.Length);
                    
                    Debug.WriteLine("Switch Pro Controller initialization sequence sent");
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
                        Debug.WriteLine("Successfully read Switch Pro Controller input");
                    }
                    else
                    {
                        _isConnected = false;
                        Debug.WriteLine("Failed to read Switch Pro Controller input");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading Switch Pro Controller: {ex.Message}");
                    _isConnected = false;
                }
            }
            else
            {
                _isConnected = false;
            }
        }

        private void ParseInputReport(byte[] report)
        {
            // Parse button states from the report
            Buttons = SwitchProButtons.None;

            // Example button mapping (adjust based on actual report format)
            if ((report[3] & 0x01) != 0) Buttons |= SwitchProButtons.A;
            if ((report[3] & 0x02) != 0) Buttons |= SwitchProButtons.B;
            if ((report[3] & 0x04) != 0) Buttons |= SwitchProButtons.X;
            if ((report[3] & 0x08) != 0) Buttons |= SwitchProButtons.Y;

            // Parse analog sticks
            LeftThumbX = (short)((report[6] << 8) | report[5]);
            LeftThumbY = (short)((report[8] << 8) | report[7]);
        }

        public void Dispose()
        {
            pollTimer?.Dispose();
            hidStream?.Dispose();
            deviceHandle?.Dispose();
        }
    }

    [Flags]
    public enum SwitchProButtons
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        X = 1 << 2,
        Y = 1 << 3,
        Plus = 1 << 4,
        Minus = 1 << 5,
        Home = 1 << 6,
        Capture = 1 << 7,
        L = 1 << 8,
        R = 1 << 9,
        ZL = 1 << 10,
        ZR = 1 << 11,
        Left = 1 << 12,
        Right = 1 << 13,
        Up = 1 << 14,
        Down = 1 << 15
    }
} 
