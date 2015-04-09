using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware;

using System.Text;
using System.IO.Ports;

using Microsoft.SPOT.Presentation;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;

using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;

using uPLibrary.Nfc;
using uPLibrary.Hardware.Nfc;
using uPLibrary.Utilities;


namespace GadgeteerTreeEmotionMingle
{
    public partial class Program
    {
        static private INfcReader nfc;
        SerialPort UART;
        Random rnd;
        int count = 0;


        public static Bluetooth.Host server;

        public static Bluetooth.Client client;
        String response = "";

        void ProgramStarted()
        {
            client = bluetooth.ClientMode;
            //server = bluetooth.HostMode;
            Thread.Sleep(2000);

            // need a handler for state changes and data recieved.
            bluetooth.BluetoothStateChanged += new Bluetooth.BluetoothStateChangedHandler(bluetooth_BluetoothStateChanged);
            bluetooth.DataReceived += new Bluetooth.DataReceivedHandler(bluetooth_DataReceived);
            bluetooth.PinRequested += new Bluetooth.PinRequestedHandler(bluetooth_PinRequest);

            // set up bluetooth module connection parameters
            bluetooth.SetDeviceName("EmotionMingle");    // change this to whatever name you want
            bluetooth.SetPinCode("5678");           //likewise, set whatever PIN you want.
            Thread.Sleep(2000);


            // put the device in pairing mode as part of a normal execution
            client.EnterPairingMode();
            Thread.Sleep(2000);

            // HSU Communication layer 
            // MOSI/SDA (TX) --> DIGITAL 0 (RX COM1) 
            // SCL/RX (RX) --> DIGITAL 1 (TX COM1) 
            //IPN532CommunicationLayer commLayer = new PN532CommunicationHSU(SerialPorts.COM1);

            // SPI Communication layer 
            // SCK --> DIGITAL 13 - Socket S Pin9 
            // MISO --> DIGITAL 12 - Socket S Pin8 
            // MOSI/SDA --> DIGITAL 11 - Socket S Pin7
            // SCL/RX -> DIGITAL 10 - Socket S Pin6
            // IRQ --> DIGITAL 8 - Socket S Pin5

            //SPI.SPI_module spi = GT.Socket.GetSocket(9, true, null, null).SPIModule;
            //Cpu.Pin pin6 = GT.Socket.GetSocket(9, true, null, null).CpuPins[6];
            //Cpu.Pin pin5 = GT.Socket.GetSocket(9, true, null, null).CpuPins[5];

            //IPN532CommunicationLayer commLayer = new PN532CommunicationSPI(spi, pin6, pin5);

            // I2C Communication layer 
            // MOSI/SDA --> ANALOG 4 (SDA) 
            // SCL/RS --> ANALOG 5 (SCL) 
            // IRQ --> DIGITAL 8 
            //IPN532CommunicationLayer commLayer = new PN532CommunicationI2C(Cpu.Pin.GPIO_Pin5); 

            //nfc = new NfcPN532Reader(commLayer);
            //nfc.TagDetected += nfc_TagDetected;
            //nfc.TagLost += nfc_TagLost;
            //nfc.Open(NfcTagType.MifareUltralight);


            GT.Socket socket = GT.Socket.GetSocket(4, true, null, null);

            Debug.Print("SerialPortName: " + socket.SerialPortName);

            UART = new SerialPort(socket.SerialPortName, 4800);
            UART.Open();

            rnd = new Random();

            Debug.Print("Program Started");

        }

        void bluetooth_PinRequest(Bluetooth sender)
        {
            Debug.Print("bluetooth_PinRequest!!");
        }
        
        private void bluetooth_DeviceInquired(Bluetooth sender, string mac_address, string name)
        {
            Debug.Print("DeviceInquired: Address: " + mac_address + " Name: " + name );
        }

        private void bluetooth_DataReceived(Bluetooth sender, string data)
        {
            Debug.Print("Data: " + data);

            byte[] buffer;

            buffer = Encoding.UTF8.GetBytes(data);
            UART.Write(buffer, 0, buffer.Length);

                  
        }

        void bluetooth_BluetoothStateChanged(Bluetooth sender, Bluetooth.BluetoothState btState)
        {
            // If the state is now "connected", we can do stuff over the link.
            if (btState == Bluetooth.BluetoothState.Connected)
            {
                Debug.Print("Connected");
            }
            // if the state is now "disconnected", you might need to stop other processes but for this example we'll just confirm that in the debug output window
            if (btState == Bluetooth.BluetoothState.Disconnected)
            {
                Debug.Print("Disconnected");
            }
        }
      


        /*
        static void button_OnInterrupt(uint data1, uint data2, DateTime time)
        {
            //nfc.Close();
        }
         */ 
        /*
        void nfc_TagLost(object sender, NfcTagEventArgs e)
        {
            Debug.Print("LOST " + HexToString(e.Connection.ID));

            count++;

            if (count > 30)
            {
                turnOffTree();
            }
        }
        */

        void turnOffTree()
        {

            string cmd = "";
            byte[] buffer;

            cmd = "off:\n";

            buffer = Encoding.UTF8.GetBytes(cmd);
            UART.Write(buffer, 0, buffer.Length);


        }

        void turnOnRandomLeaf()
        {

            Random rnd = new Random();
            string cmd = "";
            byte[] buffer;

            cmd = "hoja:" + (rnd.Next(8) + 1) + ":" + rnd.Next(10) + "\n";

            buffer = Encoding.UTF8.GetBytes(cmd);

            UART.Write(buffer, 0, buffer.Length);

        }

        void turnOnRandomBarra()
        {

            string cmd = "";
            byte[] buffer;
        
            int sad = rnd.Next(100);
            int tired = rnd.Next(100);
            int stressed = rnd.Next(100);
            int angry = rnd.Next(100);
            int happy = rnd.Next(100);
            int energetic = rnd.Next(100);
            int relaxed = rnd.Next(100);
            int calmed = rnd.Next(100);

            cmd = "barra:" + sad + ":" + tired + ":" + stressed + ":" + angry + ":" + happy + ":" + energetic + ":" + relaxed + ":" + calmed + "\n";
            buffer = Encoding.UTF8.GetBytes(cmd);
            UART.Write(buffer, 0, buffer.Length);

        }

        /*
        void nfc_TagDetected(object sender, NfcTagEventArgs e)
        {

            Debug.Print("DETECTED " + HexToString(e.Connection.ID));

            turnOnRandomLeaf();
            turnOnRandomBarra();

            byte[] data;

            switch (e.NfcTagType)
            {
                case NfcTagType.MifareClassic1k:

                    NfcMifareTagConnection mifareConn = (NfcMifareTagConnection)e.Connection;
                    mifareConn.Authenticate(MifareKeyAuth.KeyA, 0x08, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
                    mifareConn.Read(0x08);

                    data = new byte[16];
                    for (byte i = 0; i < data.Length; i++)
                        data[i] = i;

                    mifareConn.Write(0x08, data);

                    mifareConn.Read(0x08);
                    break;

                case NfcTagType.MifareUltralight:

                    NfcMifareUlTagConnection mifareUlConn = (NfcMifareUlTagConnection)e.Connection;

                    for (byte i = 0; i < 16; i++)
                    {
                        byte[] read = mifareUlConn.Read(i);
                    }

                    mifareUlConn.Read(0x08);

                    data = new byte[4];
                    for (byte i = 0; i < data.Length; i++)
                        data[i] = i;

                    mifareUlConn.Write(0x08, data);

                    mifareUlConn.Read(0x08);
                    break;

                default:
                    break;
            }
        }
        */

        /*
        static private string hexChars = "0123456789ABCDEF";

        /// <summary> 
        /// Convert hex byte array in a hex string 
        /// </summary> 
        /// <param name="value">Byte array with hex values</param> 
        /// <returns>Hex string</returns> 
        static internal string HexToString(byte[] value)
        {
            StringBuilder hexString = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                hexString.Append(hexChars[(value[i] >> 4) & 0x0F]);
                hexString.Append(hexChars[value[i] & 0x0F]);
            }
            return hexString.ToString();
        }
         */
    }



}
