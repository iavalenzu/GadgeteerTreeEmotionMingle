/* WS2811 NEFMF Driver v0.3 (02/06/2013)
  Copyright 2013 Nicolas Ricquemaque
 
  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at
 
  http://www.apache.org/licenses/LICENSE-2.0
 
  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License. */

using System;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

/* This class bellow allows to drive a WS2811 Led strip using SPI with NETMF

 The WS2811 protocol is as follows :
 Send 24 bits (Green, then Red, then Blue) for each chip in the strip, all stuck together in one shot.
 There is two possible speed to do so : 400Khz and 800Khz, which is decided by the way the chips are hard wired.
 If you have got a 800Khz strip, you won't be able to address it using the 400Khz Timings.
 The logic 0, on the 800Khz strip, shout be sent using a 0,25us physical 1, followed by a 1   uS physical 0, +- 0,075uS
 The logic 1, on the 800Khz strip, shout be sent using a 0,6 us physical 1, followed by a 0,65uS physical 0, +- 0,075uS
 To reset the strip (go back to the first led), Send a minimum 50uS 0 physical value on the line.
 If you have a 400Khz version, double all numbers !
 
 To achieve those numbers using SPI (for the 800Khz), we select a clock speed of 3.2Mb/s, giving a 0,3125 bit time
 For the logical 0, we send 4 bits : 1000, giving 0,3125uS physical 1, followed by 0,9375uS physical 0, both within allowed error margins
 For the logical 1, we send 4 bits : 1100, giving 0,625uS physical 1, followed by 0,625uS physical 0, both within allowed error margins
 Therefore, we send 2 logical bits with each physical SPI byte. So to address a single color (value 0 to 255), we need 4 SPI bytes
 So to send the whole 24 bits GRB sequence, we will send 12 SPI bytes.
 If you have the 400Khz kind, just divide the SPI clock by two, to 1600Mb/s.
 
 To Achieve a good speed, when initialised, this class first computes a 1024 bytes lookup table to convert the BGR 
 light value (1 byte, 0 to 255) to the 4 SPI bytes. The class also keeps a memory buffer for all the leds
 which has a size of : number of leds * 3 (GRB) * 4 SPI bytes = number of leds *12.
 For an example, a 4 meters 240 leds light strip has a 2880 memory buffer.
 
 Each function provided in the class work in the buffer only ; When you are ready to submit the changes to the strip,
 Invoke the Transmit() function.
  
 New in v0.2 : - various optimizations
               - New function "Fade()"
               - New function "Rotate()"
               - Linearisation of the perceived luminosity built in the lookuptable (ajustable)

 New in v0.3 : - SetAll() function replaced by SetRange()
 
 */

namespace WS2811
{
    /// <summary>
    /// SPI Driver for a WS2811 Led strip.
    /// </summary>
    public class WS2811Led : IDisposable
    {
        #region Public enums and delegates
        // Public Enum to select the strip speed
        public enum WS2811Speed : uint { S400KHZ = 1600, S800KHZ = 3200 }
        #endregion

        #region private fields
        private byte[] _WS2811Table;    // The conversion table between byte value and SPI coding
        private byte[] _WS2811Buffer;   // The buffer allocated to store and send the led states (12x led number)
        private SPI.Configuration _WS2811SPIConfig; // The SPI configuration
        private SPI _WS2811SPI;         // The SPI bus
        private int _StripLength;       // The number of leds in the strip
        private int _StripLightNb;       // The number of lights in the strip (=_StripLength*3)
        private int _StripBufferLen;       // The number of lights in the strip (=_StripLength*12)
        private byte[] _tmpPixel = new byte[12]; // Temporary buffer
        #endregion

        #region Constructor and Destructor
        /// <summary>
        /// WS2811Led constructor.
        /// </summary>
        /// <param name="striplength">Number of leds in the strip. Warning : will alocate a memory buffer 12 times this number.</param>
        /// <param name="SPImodule">The SPI module the strip is connected to. Only the MOSI line is used. SPI1 is used if parameter omited.</param>
        /// <param name="speed">The Hardwired speed of the strip. 800Khz is used if parameter is omited.</param>
        /// <param name="linearizationfactor">If set to a value >0 (default value=2.25) will build the lookup table using human linear perceived luminosity instead of direct PWM values.</param>
        public WS2811Led(int striplength, SPI.SPI_module SPImodule = SPI.SPI_module.SPI1, WS2811Speed speed = WS2811Speed.S800KHZ, double linearizationfactor=2.25)
        {
            _StripLength = striplength;
            _StripLightNb = striplength * 3; //For later speed optimizations
            _StripBufferLen = striplength * 12; //For later speed optimizations

            // Initialize SPI
            _WS2811SPIConfig = new SPI.Configuration(Cpu.Pin.GPIO_NONE, false, 0, 0, false, true, (uint)speed, SPImodule);
            _WS2811SPI = new SPI(_WS2811SPIConfig);

            // SPI Transmit buffer = 4 output byte per color, 3 colors per light 
            _WS2811Buffer = new byte[4 * 3 * striplength];

            // Compute fast byte to SPI lookup table. By default the linearisation of human perceived luminosity is on (Weber–Fechner law)
            _WS2811Table = new byte[1024];
            BuildTable(linearizationfactor);

            //Clear all leds
            Clear();
            Transmit();
        }


        /// <summary>
        /// Dispose of the object.
        /// </summary>
        public void Dispose()
        {
            _WS2811Buffer = null;
            _WS2811Table = null;
            _WS2811SPI.Dispose();
            Debug.GC(true);
        }
        #endregion


        /// <summary>
        /// (re)build the lookup table to switch between human perceived luminosity to real PWM values.
        /// </summary>
        /// <param name="linearization">if >0, will linearize to human perception using this param as a factor (2.25 by default)</param>
        public void BuildTable(double linearization=2.25)
        {
            // Compute fast byte to SPI lookup table
            byte tmp;

            for (int i = 0, ptr = 0; i <= 255; i++)
            {
                // Depending on the value of linear, we use a formula to linearize or direct value
                tmp = (byte)(linearization > 0 ? System.Math.Round(System.Math.Pow(i, linearization) / System.Math.Pow(255, linearization) * 255) : i);
                for (int j = 6; j >= 0; j -= 2)
                {
                    switch (tmp >> j & 3)
                    {
                        case 0: _WS2811Table[ptr++] = 0x88; break;
                        case 1: _WS2811Table[ptr++] = 0x8C; break;
                        case 2: _WS2811Table[ptr++] = 0xC8; break;
                        case 3: _WS2811Table[ptr++] = 0xCC; break;
                    }
                }
            }
        }
  


        /// <summary>
        /// Transcode a part or a full GRB byte array into the WS2811 buffer
        /// </summary>
        /// <param name="GRBBuffer">Byte Array describing all the leds color. First 3 bytes = Green, Red the Blue values from 0 to 255 for the first let, etc.</param>
        /// <param name="startpos">Starting led position in the strip buffer (0 by default)</param>
        /// <param name="number">Number of led to copy (0 by default meaning all available in input buffer)</param>
        public void SetRange(byte[] GRBBuffer, int startpos=0, int number=0)
        {
            int ToTranscode = (number > 0) ? number*3 : GRBBuffer.Length;

            // for each value in the input array convert to the 4 bytes into the output buffer from the lookup table.
            for (int i = 0; i < ToTranscode; i++) 
                Array.Copy(_WS2811Table, GRBBuffer[i] << 2, _WS2811Buffer, (i << 2) + startpos*12, 4);          
        }



        /// <summary>
        /// Set all the leds values do 0 (dark) inside the buffer.
        /// </summary>
        public void Clear()
        {
            // for each value in the input array convert to the 4 bytes into the output buffer from the lookup table.
            for (int i = 0; i < _StripLength * 3; i++) 
                Array.Copy(_WS2811Table, 0, _WS2811Buffer, i << 2, 4);
        }



        /// <summary>
        /// Sets a single led color.
        /// </summary>
        /// <param name="index">Led number (0 is the first led)</param>
        /// <param name="r">Red value, from 0 = dark to 255 = brigther</param>
        /// <param name="g">Green value, from 0 = dark to 255 = brigther</param>
        /// <param name="b">Blue value, from 0 = dark to 255 = brigther</param>
        public void Set(int index, byte r, byte g, byte b)
        {
            // Transcode
            Array.Copy(_WS2811Table, g << 2, _WS2811Buffer, index * 12, 4);
            Array.Copy(_WS2811Table, r << 2, _WS2811Buffer, index * 12 + 4, 4);
            Array.Copy(_WS2811Table, b << 2, _WS2811Buffer, index * 12 + 8, 4);
        }



        /// <summary>
        /// Shift the strips from n pixels , adding black padding at the end.
        /// </summary>
        /// <param name="value">Number of pixels to shift to the right (if positive) or to the left (if negative)</param>
        public void Shift(int value=1)
        {
            if (value > 0) // Shift to the right
            {
                // Move the pixels to the right
                Array.Copy(_WS2811Buffer, 0, _WS2811Buffer, value * 12, (_StripLength - value) * 12);
                // Add black padding at the beggining
                for (int i = 0; i < (value*3); i++) Array.Copy(_WS2811Table, 0, _WS2811Buffer, i << 2, 4);
            }
            else // Shift to the left
            {
                // Move the pixels to the left
                Array.Copy(_WS2811Buffer, (0-value)*12, _WS2811Buffer, 0, (_StripLength + value) * 12);
                // Add black padding at the end
                for (int i = 0; i < ((0-value)*3); i++) Array.Copy(_WS2811Table, 0, _WS2811Buffer, (_StripLength + value) * 12 + (i << 2), 4);
            }            
        }



        /// <summary>
        /// Rotate the whole strip in one direction.
        /// </summary>
        /// <param name="direction">True : to the right (by default), False : to the left</param>
        public void Rotate(bool direction=true)
        {
            if (direction) // to the right
            {
                Array.Copy(_WS2811Buffer, (_StripLength - 1) * 12, _tmpPixel, 0, 12);
                Array.Copy(_WS2811Buffer, 0, _WS2811Buffer, 12, (_StripLength - 1) * 12);
                Array.Copy(_tmpPixel, 0, _WS2811Buffer, 0, 12);
            }
            else
            {           // To the left
                Array.Copy(_WS2811Buffer, 0, _tmpPixel, 0, 12);
                Array.Copy(_WS2811Buffer, 12, _WS2811Buffer, 0, (_StripLength - 1) * 12);
                Array.Copy(_tmpPixel, 0, _WS2811Buffer, (_StripLength - 1) * 12, 12);
            }
        }



        /// <summary>
        /// Decrease light by 50% on the whole strip buffer.
        /// </summary>
        public void Fade()
        {
            uint tmp;
            for (int i = 0; i < _StripBufferLen; i += 4)
            {               
                tmp = 0x80000000 | (uint)_WS2811Buffer[i] << 20 | (uint)_WS2811Buffer[i + 1] << 12 | (uint)_WS2811Buffer[i + 2] << 4 | ((uint)_WS2811Buffer[i + 3] >> 4);
                _WS2811Buffer[i] = (byte)(tmp >> 24);
                _WS2811Buffer[i + 1] = (byte)((tmp >> 16) & 0xFF);
                _WS2811Buffer[i + 2] = (byte)((tmp >> 8) & 0xFF);
                _WS2811Buffer[i + 3] = (byte)(tmp & 0xFF);
            }       
        }
       


        /// <summary>
        /// Transmit the buffer to the strip.
        /// </summary>
        public void Transmit()
        {
            _WS2811SPI.Write(_WS2811Buffer);
        }

    }
}
