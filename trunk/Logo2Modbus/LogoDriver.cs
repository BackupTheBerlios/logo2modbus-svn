/* Copyright (c) 2008 Tobias Mache
 * 
 * This file is part of Logo2Modbus.
 * 
 * Logo2Modbus is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * Logo2Modbus is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with Logo2Modbus.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;
using System.Threading;
using Modbus.Data;
using Modbus.Utility;

namespace Logo2Modbus
{
    public delegate void LogoStatusChangeHandler();

    public enum LogoStatus
    {
        /* Offline : Verbindung getrennt.
         * Online  : Verbindung soll hergestellt werden.
         * Instop  : Verbindung zur Logo ist hergestellt, aber die Logo ist nicht in RUN.
         * Connect : Verbindung steht, Logo ist in RUN und Werte werden gelesen.
         */
        Offline, Online, InStop, Connected
    } 

    class LogoDriver
    {
        static byte[] INITCODE = { 0x02, 0x1F, 0x02 }; //Codesequenz zum starten der Übertragung
        static byte[] INITANSWER = { 0x06, 0x03, 0x1F, 0x02, 0x42 }; //Antwort auf INITCODE
        static byte[] ASKRUNSTOP = { 0x55, 0x17, 0x17, 0xAA }; 
        static byte[] ANSWERRUN = { 0x06, 0x01 };
        static byte[] ASKSTATUS = { 0x55, 0x13, 0x13, 0x00, 0xAA };
        static int WAITTIME = 300;

        private SerialPort serialPort;
        private Thread commThread;
        private bool running, shouldstop = false;
        
        public LogoStatus status;
        public event LogoStatusChangeHandler logoStatusChanged; 
        
        public int cykleTime = 300;

        private void setStatus(LogoStatus status)
        {
            if (this.status != status)
            {
                this.status = status;
                logoStatusChanged();
            }
        }

        public readonly DataStore dataImage;

        public LogoDriver()
        {
            serialPort = new SerialPort();
            serialPort.BaudRate = 9600;
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.Even;
            serialPort.ReadTimeout = 200;
            serialPort.RtsEnable = true;
            serialPort.DtrEnable = true;

            running = false;
            shouldstop = false;
            setStatus(LogoStatus.Offline);

            // Datenspeicher/Prozessabbild initalisieren.
            dataImage = new DataStore();
            dataImage.InputDiscretes = CollectionUtility.CreateDefaultCollection<ModbusDataCollection<bool>,bool>(false, 64);
            dataImage.InputRegisters = CollectionUtility.CreateDefaultCollection<ModbusDataCollection<ushort>, ushort>(0, 80);
            dataImage.HoldingRegisters = CollectionUtility.CreateDefaultCollection<ModbusDataCollection<ushort>, ushort>(0, 32);
            dataImage.CoilDiscretes = CollectionUtility.CreateDefaultCollection<ModbusDataCollection<bool>, bool>(false, 0);
        }

        public void setPortName(String name)
        {
            serialPort.PortName = name;
        }

        public void start()
        {
            if (!running)
            {
                running = true;
                setStatus(LogoStatus.Online);
                shouldstop = false;
                serialPort.Open();
                commThread = new Thread(run);
                commThread.Start();
                
            }
        }

        public void stop()
        {
            shouldstop = true;
        }

        private void run()
        {
            byte[] readBuffer = new byte[80];
            int readCount;
            setStatus(LogoStatus.Online);

            while (!shouldstop)
            {
                try
                {
                    if (status == LogoStatus.Online)
                    {
                        serialPort.Write(INITCODE, 0, INITCODE.Length);
                        Thread.Sleep(WAITTIME);
                        readCount = serialPort.Read(readBuffer, 0, 80);
                        setStatus(LogoStatus.InStop);
                    }
                    else
                        if (status == LogoStatus.InStop)
                        {
                            serialPort.Write(ASKRUNSTOP, 0, ASKRUNSTOP.Length);
                            Thread.Sleep(WAITTIME);
                            readCount = serialPort.Read(readBuffer, 0, 80);
                            if (readCount >= 2)
                                if (readBuffer[0] == ANSWERRUN[0])
                                    if (readBuffer[1] == ANSWERRUN[1])
                                        setStatus(LogoStatus.Connected);
                                    else Thread.Sleep(cykleTime);
                                else Thread.Sleep(cykleTime);
                            else Thread.Sleep(cykleTime);
                        }
                        else
                        {
                            serialPort.Write(ASKSTATUS, 0, ASKSTATUS.Length);
                            Thread.Sleep(WAITTIME);
                            readCount = serialPort.Read(readBuffer, 0, 80);
                            if (!decode(readBuffer,readCount))
                            {
                                setStatus(LogoStatus.Online);
                            }
                            Thread.Sleep(cykleTime);
                        }
                }
                catch (TimeoutException ex)
                {
                    setStatus(LogoStatus.Online);
                    Thread.Sleep(cykleTime * 2);
                }
            }
            
            serialPort.Close();
            running = false;
            setStatus(LogoStatus.Offline);
        }

        private bool decode(byte[] data, int count)
        {
            if (count > 68)
            {
                if (data[0] == 0x06)
                {
                    //Digitalwerte
                    //Eingänge 24(3 byte), Ausgänge 16(2 byte), Merker 24(3 byte)
                    for (int i = 0; i < 8; i++)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            int index = i * 8 + j+1;
                            dataImage.InputDiscretes[index] = (0 != (data[28 + i] & (byte) 0x0001 << j));
                            // !Workaround zur Darstellung von Binärsignalen in Kurven. Alle diskreten Werte werden nochmal als Analogwert (High:100/Low:0) gespeichert.
                            dataImage.InputRegisters[16 + index] = (ushort) (dataImage.InputDiscretes[index] ? 100 : 0);
                        }
                    }
                    
                    //Analogwerte
                    //Eingänge 8, Ausgänge 2, Merker 6
                    for (int i = 0; i < 16; i++)
                    {
                        dataImage.InputRegisters[i+1] = BitConverter.ToUInt16(data,38+2*i);
                        // !Workaround für WinCC flexible: Alle Analogwerte werden als 32bit float in den HoldingRegister nochmal gespeichert.
                        dataImage.HoldingRegisters[i*2+1] = BitConverter.ToUInt16(BitConverter.GetBytes((float)dataImage.InputRegisters[i+1]), 0);
                        dataImage.HoldingRegisters[i*2+2] = BitConverter.ToUInt16(BitConverter.GetBytes((float)dataImage.InputRegisters[i+1]), 2);
                    }

                    return true;
                }
            }
            return false;
        }
    }
}
